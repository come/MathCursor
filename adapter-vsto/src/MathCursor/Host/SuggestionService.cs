using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Threading;
using MathCursor.Core;
using MathCursor.Core.Lattice;
using MathCursor.Detection;
using MathCursor.HostContract;
using MathCursor.UI;
// Moteur lattice : enchaîne Lex → TopK → Parse → Render. Façade côté core
// qui expose ILatexEngine, donc l'adapter VSTO reste agnostique de l'algo.
using Engine = MathCursor.Core.LatticeEngine;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host
{
    /// <summary>
    /// Surveille en continu le paragraphe courant (timer 200 ms) et affiche
    /// une popup WPF avec les zones math détectées par le modèle NER.
    ///
    /// Pivot pivot ML : la popup affiche maintenant ce que le modèle NER détecte
    /// (zones math + confiance), pas le résultat du pipeline heuristique.
    /// </summary>
    public sealed class SuggestionService : IDisposable
    {
        private const int PollIntervalMs = 200;

        // Préfixe bookmark : chaque OMath inséré par MathCursor est entouré d'un
        // bookmark "mcEq_<handleId>" pour (a) identifier que l'équation nous
        // appartient et (b) retrouver la source brute en CustomXMLPart au moment
        // où le caret revient dessus (mode édition).
        private const string BookmarkPrefix = "mcEq_";

        private readonly Word.Application _app;
        private readonly WordContextReader _contextReader;
        private readonly MathNerDetector _ner;
        private readonly Engine _engine;
        private readonly ZoneResolver _resolver;
        private readonly IEquationStore _store;

        private SuggestionPopupWindow _popup;
        private DispatcherTimer _pollTimer;
        private string _lastParagraph = "";
        private int _lastCaretPos = -1;
        private bool _installed;
        // Inférence asynchrone : on évite de bloquer le thread UI
        private bool _inferenceInFlight;

        // Garde "popup silencieuse au démarrage" : à l'ouverture d'un doc on
        // ne montre rien tant que l'utilisateur n'a pas cliqué ou tapé. Signal
        // = le caret a bougé depuis la position observée au tout premier tick.
        // One-shot : une fois levée, popup normale pour le reste de la session.
        // Cf. ADR 2026-04-24-UX-popup-silent-until-interaction.
        private int _initialCaretPos = -1;
        private bool _userInteracted;

        // État de la dernière popup affichée — nécessaire pour commit sur Enter :
        // on a besoin des positions absolues dans le document (pas juste offsets
        // paragraphe), des choix présentés, et de la source brute pour la store.
        private int _lastZoneAbsStart = -1;
        private int _lastZoneAbsEnd = -1;
        private string _lastZoneSource = "";

        // Snapshot de la dernière action user (popup + commit éventuel) pour
        // pré-remplir la fenêtre "Signaler une erreur". Mis à jour à chaque
        // ShowPopup et juste avant InsertOMathAt. Cf. LastActionSnapshot.
        private LastActionSnapshot _lastAction;

        // État mode édition : la popup _editPopup est affichée pour proposer
        // « Revenir à la saisie initiale » sur l'OMath au caret. _editHandle
        // identifie l'OMath en cours d'édition pour le revert action.
        private EquationHandle _editHandle;
        private EditModePopupWindow _editPopup;

        // ANTI-SPAM POPUP — modèle commun aux 2 popups (suggestion et édition) :
        // une fois qu'une popup a été affichée pour une zone donnée, on retient
        // l'identifiant de cette zone. Tant que le caret reste dans la MÊME
        // zone, on ne re-spawn pas la popup au tick suivant (200 ms = 5 Hz).
        // Le flag est reset uniquement quand le caret QUITTE la zone — Esc
        // n'efface pas le flag, donc la popup ne réapparaît pas tant qu'on
        // n'est pas sorti et revenu.
        //
        //  - _editingOMathStart : start position de l'OMath sous le caret
        //    (mode édition). -1 = pas dans un OMath traité.
        //  - _dismissedZoneStart/End : zone NER pour laquelle on a déjà
        //    affiché (ou que l'utilisateur a fermée par Esc) la popup
        //    suggestion. -1 = aucune zone bloquée.
        private int _editingOMathStart = -1;
        private int _dismissedZoneStart = -1;
        private int _dismissedZoneEnd = -1;

        // État d'extension itérative (ADR 29-04). Activé au 1er Ctrl+Espace
        // qui ouvre la popup ; chaque appui suivant tant que la popup est
        // ouverte étend la zone d'un cran vers la gauche.
        // Reset à HidePopup ou OnSelectionChange.
        //  - _iterativeParagraph : snapshot du texte du paragraphe
        //  - _iterativeParaAbsStart : start absolu du paragraphe dans le doc
        //  - _iterativeSpanStart : offset paragraph du début de la span courante
        //  - _iterativeSpanEnd : offset paragraph de la fin (= caret au 1er trigger)
        //  - _iterativeOMaths : snapshot des regions OMath du paragraphe
        private string _iterativeParagraph;
        private int _iterativeParaAbsStart = -1;
        private int _iterativeSpanStart = -1;
        private int _iterativeSpanEnd = -1;
        private IReadOnlyList<(int start, int end)> _iterativeOMaths;

        // Cooldown post-commit : après une insertion, le caret peut rester
        // momentanément DANS l'OMath créé (NudgeCursorOutOfMath n'est pas
        // toujours capable de le faire sortir, surtout en display-mode).
        // Sans cette garde, on entre immédiatement en mode édition de l'OMath
        // qu'on vient d'insérer, et la popup re-spam (cf. log : "edit mode" 25×).
        // 500 ms = le temps que l'utilisateur tape ou bouge.
        private DateTime _lastCommitUtc = DateTime.MinValue;
        private const int PostCommitCooldownMs = 500;

        // ID de session : même GUID tout au long d'une session Word. Sert à
        // corréler plusieurs feedbacks consécutifs du même utilisateur sans
        // le tracker — il disparaît quand Word est redémarré.
        private readonly string _sessionId = Guid.NewGuid().ToString("D");

        public SuggestionService(Word.Application app, MathNerDetector ner, Engine engine, IEquationStore store)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _ner = ner ?? throw new ArgumentNullException(nameof(ner));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _resolver = new ZoneResolver(_engine);
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _contextReader = new WordContextReader(_app);
        }

        /// <summary>
        /// Retourne le snapshot de la dernière action (popup + commit éventuel)
        /// pour pré-remplir la fenêtre "Signaler une erreur". Renvoie null si
        /// aucune action depuis le démarrage de Word.
        /// </summary>
        public LastActionSnapshot GetLastAction() => _lastAction;

        /// <summary>
        /// Lit le texte du paragraphe Word courant pour le snapshot du report.
        /// Tronqué à 2000 chars pour ne pas bourrer le payload si l'user a un
        /// méga-paragraphe. Retourne "" en cas d'erreur (jamais throw).
        /// </summary>
        private string ReadParagraphContextForReport()
        {
            try
            {
                var sel = _app?.Selection;
                if (sel == null) return string.Empty;
                var paraRange = sel.Paragraphs[1].Range;
                var text = paraRange.Text ?? string.Empty;
                if (text.Length > 2000) text = text.Substring(0, 2000) + "[…]";
                return text;
            }
            catch { return string.Empty; }
        }

        public void Install()
        {
            if (_installed) return;
            _app.WindowSelectionChange += OnSelectionChange;
            _app.WindowDeactivate += OnWindowDeactivate;
            _app.WindowActivate += OnWindowActivate;

            _pollTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(PollIntervalMs),
            };
            _pollTimer.Tick += (_, __) => CheckAndUpdate();
            _pollTimer.Start();
            _installed = true;
        }

        public void Dispose()
        {
            try { if (_installed) _app.WindowSelectionChange -= OnSelectionChange; } catch { }
            try { if (_installed) _app.WindowDeactivate -= OnWindowDeactivate; } catch { }
            try { if (_installed) _app.WindowActivate -= OnWindowActivate; } catch { }
            try { _pollTimer?.Stop(); } catch { }
            try { _popup?.Close(); } catch { }
            try { _editPopup?.Close(); } catch { }
            _popup = null;
            _editPopup = null;
            _pollTimer = null;
            _installed = false;
        }

        // IsPopupVisible ne couvre QUE la popup de suggestion (clavier
        // intercepté pour nav). En mode édition d'OMath, les flèches et Enter
        // sont laissées à Word pour la nav math native — la popup edit se
        // contente d'un click souris pour valider l'action revert.
        public bool IsPopupVisible => (_popup?.IsVisible == true);
        public bool IsEditPopupVisible => (_editPopup?.IsVisible == true);
        // Pour Esc : ferme l'une ou l'autre.
        public bool IsAnyPopupVisible => IsPopupVisible || IsEditPopupVisible;
        public bool IsNavMode => (_popup?.IsNavMode == true);

        public void MoveSelection(int delta) => _popup?.MoveSelection(delta);
        public bool MoveSelectionHorizontal(int delta)
            => _popup?.MoveSelectionHorizontal(delta) == true;
        public void EnterNavMode() => _popup?.EnterNavMode();
        public void HidePopup()
        {
            // Hide explicite (Esc / commit / sortie zone) → reset des caches
            // de résolution et de préférences de règles dans la popup, ET
            // des préférences source-mutation dans le résolveur (V→forall, etc.).
            // Au prochain trigger l'utilisateur repart d'une page blanche.
            _popup?.HidePopup(resetCaches: true);
            _editPopup?.HidePopup();
            _resolver?.Clear();
            ResetIterativeExpansion();
        }

        /// <summary>
        /// Hide « transient » : NER ne détecte temporairement pas la zone
        /// (ex: pendant la frappe entre deux caractères), mais on ne veut pas
        /// reset les choix d'ambiguïté de l'utilisateur. Au prochain tick où
        /// la zone redevient détectable, les substitutions précédemment
        /// validées s'appliqueront à nouveau.
        /// </summary>
        private void HidePopupTransient()
        {
            _popup?.HidePopup(resetCaches: false);
            _editPopup?.HidePopup();
        }

        private void OnSelectionChange(Word.Selection sel)
        {
            // Try-catch défensif : Word désactive l'add-in après une exception
            // non-gérée dans un event handler. CheckAndUpdate fait beaucoup de
            // travail (lecture paragraphe, NER, etc.) et peut échouer.
            try
            {
                // Caret bougé volontairement → reset l'état d'extension itérative
                // (cf. ADR 29-04) : le prochain Ctrl+Espace repart d'une détection
                // neuve. Le polling CheckAndUpdate continue normalement.
                ResetIterativeExpansion();
                CheckAndUpdate();
            }
            catch (Exception ex)
            {
                LogDiag("on_selection_change_error: " + ex.Message);
            }
        }

        /// <summary>
        /// OMath dans lequel se trouve strictement le caret (pas seulement au
        /// bord). Utilisé pour distinguer "vraiment dans l'équation" → mode
        /// édition, de "juste à côté" → zone texte libre.
        ///
        /// Critère "strictement dedans" :
        ///  - Word signale la sélection comme étant inside un OMath (sel.OMaths),
        ///  - OU caret strictement dans ]r.Start, r.End[ (bords exclus).
        /// Le bord droit (r.End) est la position juste APRÈS l'OMath, qu'on veut
        /// considérer comme "en zone texte" pour laisser taper la suite.
        /// </summary>
        private Word.OMath FindOMathAtCaret()
        {
            try
            {
                var sel = _app.Selection;
                if (sel.OMaths != null && sel.OMaths.Count > 0)
                {
                    foreach (Word.OMath om in sel.OMaths) return om;
                }
                var para = sel.Paragraphs[1].Range;
                int caretPos = sel.Start;
                foreach (Word.OMath om in para.OMaths)
                {
                    var r = om.Range;
                    if (caretPos > r.Start && caretPos < r.End) return om;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Cherche un bookmark "mcEq_..." couvrant l'OMath donné, et renvoie son
        /// handle (sans le préfixe) — ou null si l'OMath n'est pas à nous.
        /// </summary>
        private string FindOurHandleForOMath(Word.OMath om)
        {
            try
            {
                var doc = _app.ActiveDocument;
                int omStart = om.Range.Start;
                int omEnd = om.Range.End;
                foreach (Word.Bookmark bm in doc.Bookmarks)
                {
                    if (!bm.Name.StartsWith(BookmarkPrefix, StringComparison.Ordinal)) continue;
                    var r = bm.Range;
                    // Bookmark couvre ou touche l'OMath (tolérance 1 char pour l'espace trailing).
                    if (r.Start <= omStart && r.End >= omEnd - 1)
                        return bm.Name.Substring(BookmarkPrefix.Length);
                }
            }
            catch { }
            return null;
        }

        private void OnWindowDeactivate(Word.Document doc, Word.Window wnd)
        {
            HidePopup();
            try { _pollTimer?.Stop(); } catch { }
        }

        private void OnWindowActivate(Word.Document doc, Word.Window wnd)
        {
            try { _pollTimer?.Start(); } catch { }
        }

        private void CheckAndUpdate()
        {
            if (_inferenceInFlight) return;
            try
            {
                if (_app.Documents.Count == 0)
                {
                    HidePopup();
                    return;
                }

                // Garde "popup silencieuse au démarrage" : tant que le caret n'a
                // pas bougé depuis l'ouverture, on ne fait rien d'autre que
                // d'enregistrer la position. Un seul mouvement (clic ou frappe)
                // suffit à lever la garde pour le reste de la session.
                int currentCaret;
                try { currentCaret = _app.Selection.Start; }
                catch { return; }
                if (!_userInteracted)
                {
                    if (_initialCaretPos < 0)
                    {
                        _initialCaretPos = currentCaret;
                        return; // 1er tick : on note, on attend
                    }
                    if (currentCaret == _initialCaretPos)
                    {
                        return; // pas encore d'interaction utilisateur
                    }
                    _userInteracted = true;
                    LogDiag($"user interaction detected (caret moved from {_initialCaretPos} to {currentCaret}) — popup armed");
                }

                // Cooldown post-commit : si on vient d'insérer un OMath, le
                // caret peut être resté à l'intérieur. On ne ré-ouvre PAS la
                // popup en mode édition immédiatement (sinon respam visuel).
                bool inPostCommitCooldown =
                    (DateTime.UtcNow - _lastCommitUtc).TotalMilliseconds < PostCommitCooldownMs;

                // Caret sur un OMath ? Deux cas :
                //  - OMath à nous (bookmark mcEq_...) → MODE ÉDITION : on recharge
                //    la source brute depuis le store, on repasse l'engine dessus
                //    et on affiche la popup avec les alternatives.
                //  - OMath étranger (ex. une équation déjà dans le doc) → hide
                //    popup (relancer l'algo sur du LaTeX rendu donnerait du bruit).
                var omAtCaret = FindOMathAtCaret();
                if (omAtCaret != null)
                {
                    if (inPostCommitCooldown)
                    {
                        HidePopup();
                        return;
                    }
                    // Si on a DÉJÀ géré cet OMath (popup affichée OU dismissée
                    // par l'utilisateur via Esc), on ne re-spawn pas. Le flag
                    // _editingOMathStart marque "OMath traité" — il ne sera
                    // remis à -1 que quand le caret QUITTE cet OMath.
                    int omStart = -1;
                    try { omStart = omAtCaret.Range.Start; } catch { }
                    if (_editingOMathStart == omStart) return;

                    var ok = TryEnterEditMode(omAtCaret);
                    // On marque l'OMath comme traité dès qu'on a tenté d'ouvrir,
                    // même en cas d'échec (pas d'OMath à nous → pas de re-tentative).
                    _editingOMathStart = omStart;
                    return;
                }
                // Sortie propre du mode édition quand on quitte l'OMath
                _editHandle = null;
                _editingOMathStart = -1;
                _editPopup?.HidePopup();

                ParagraphRead paragraph;
                int caretPos;
                try
                {
                    paragraph = _contextReader.ReadCurrentParagraph();
                    caretPos = _app.Selection.Start;
                }
                catch
                {
                    return;
                }
                string paragraphText = paragraph.Text;
                int caretInParagraph = paragraph.CaretOffset;
                int paragraphAbsStart = paragraph.ParagraphAbsStart;
                var omathRegions = paragraph.OMathRegions;

                // Skip si rien n'a changé depuis le dernier check
                if (paragraphText == _lastParagraph && caretPos == _lastCaretPos) return;
                _lastParagraph = paragraphText;
                _lastCaretPos = caretPos;

                if (string.IsNullOrWhiteSpace(paragraphText))
                {
                    HidePopupTransient();
                    return;
                }

                LogDiag($"tick len={paragraphText.Length} caret={caretInParagraph} omaths={omathRegions.Count} text=\"{Preview(paragraphText)}\"");

                // Inférence sur thread pool (~30-80 ms hors warm-up)
                _inferenceInFlight = true;
                Task.Run(() =>
                {
                    // Coupe le paragraphe APRÈS la dernière région OMath qui
                    // se termine avant le caret. Évite que le NER voit le
                    // contexte math résiduel et drag des stopwords ("et",
                    // "donc"...) dans le span MATH suivant.
                    // Cf. bug user 30-04 : `f(x) = 1/x² et g(x)=rac(x+1)`
                    // avec OMath rendu pour `f(x) = 1/x²` faisait que le NER
                    // classait `et g(x)=rac(x+1)` ENTIER comme MATH.
                    int nerOffset = 0;
                    foreach (var (s, e) in omathRegions)
                    {
                        if (e <= caretInParagraph && e > nerOffset) nerOffset = e;
                    }
                    string nerInput = nerOffset > 0
                        ? paragraphText.Substring(nerOffset)
                        : paragraphText;
                    LogDiag($"ner_input offset={nerOffset} len={nerInput.Length} omaths={omathRegions.Count} text=\"{nerInput.Replace("\r", "\\r").Replace("\n", "\\n")}\"");

                    IReadOnlyList<DetectedZone> zones;
                    try { zones = _ner.Detect(nerInput); }
                    catch (Exception ex) { LogDiag("ner_error: " + ex.Message); zones = Array.Empty<DetectedZone>(); }

                    // Remap des positions : les zones reviennent avec des
                    // positions dans `nerInput`, on les rebase sur le paragraphe
                    // entier en ajoutant `nerOffset`.
                    if (zones != null && zones.Count > 0 && nerOffset > 0)
                    {
                        var rebased = new List<DetectedZone>(zones.Count);
                        foreach (var z in zones)
                            rebased.Add(new DetectedZone(
                                z.Start + nerOffset, z.End + nerOffset, z.Text, z.Confidence));
                        zones = rebased;
                    }
                    if (zones != null)
                    {
                        for (int z = 0; z < zones.Count; z++)
                            LogDiag($"ner_zone[{z}]=[{zones[z].Start},{zones[z].End}] conf={zones[z].Confidence:F2} text=\"{zones[z].Text}\"");
                    }

                    // Filtre : on jette les zones NER qui chevauchent une région OMath.
                    // Ces zones sont déjà converties — les re-proposer serait redondant
                    // (et piégeux : on insèrerait un 2e OMath par-dessus).
                    var filteredZones = FilterOutOMathOverlap(zones, omathRegions);
                    LogDiag($"zones={zones.Count} → filtered={filteredZones.Count} (omath_overlap dropped={zones.Count - filteredZones.Count})");

                    // Retour sur le thread UI pour mettre à jour la popup
                    var capturedOmaths = omathRegions; // closure capture
                    _pollTimer?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { ApplyZones(filteredZones, caretInParagraph, paragraphAbsStart, capturedOmaths); }
                        finally { _inferenceInFlight = false; }
                    }));
                });
            }
            catch
            {
                _inferenceInFlight = false;
            }
        }

        private void ApplyZones(IReadOnlyList<DetectedZone> zones, int caretInParagraph, int paragraphAbsStart, IReadOnlyList<(int start, int end)> omathRegions)
        {
            if (zones == null || zones.Count == 0)
            {
                HidePopupTransient();
                return;
            }

            // On n'affiche que la zone la plus proche du curseur. Tolérance :
            // si le caret est juste après la zone NER avec uniquement du
            // whitespace entre, on étend la zone jusqu'au caret — mais
            // SEULEMENT si la zone le justifie (cf. ShouldExtendZoneForward).
            // Sinon (formule complète sans slot vacant, dernière partie pas
            // un opérateur en attente d'opérande), on ferme.
            var target = PickNearestZone(zones, caretInParagraph, out int dist);
            LogDiag($"pick caret={caretInParagraph} target={(target == null ? "null" : target.ToString())} dist={dist}");
            if (target == null) { HidePopupTransient(); return; }
            if (dist > 0)
            {
                if (!ShouldExtendZoneForward(target))
                {
                    LogDiag($"hide_reason=zone_complete_no_extend (target end='{target.Text}')");
                    HidePopupTransient();
                    return;
                }
                target = TryExtendForwardWhitespace(_lastParagraph, target, caretInParagraph);
                if (target == null || (caretInParagraph - target.End) > 0)
                {
                    LogDiag("hide_reason=caret_still_outside_after_forward_extend");
                    HidePopupTransient();
                    return;
                }
                LogDiag($"forward_extended target={target}");
            }

            // Le NER rate parfois des mots-clés math en début de zone (lim, sqrt, etc.)
            // On tente une extension arrière : si le mot immédiatement avant la zone est
            // un keyword math connu, on l'absorbe.
            target = ExtendZoneBackwardWithKeyword(_lastParagraph, target);
            LogDiag($"backward_extended target={target}");

            // Pipeline lattice via le ZoneResolver : applique les prefs
            // source-mutation accumulées (V→forall, etc.) avant le pipeline
            // pour que les ambig déjà résolues ne se re-déclenchent pas.
            ResolvedZone resolved;
            // DEBUG : dump chars hex pour détecter chars invisibles Word
            if (target.Text != null && target.Text.Length > 0)
            {
                var hex = new System.Text.StringBuilder();
                foreach (var ch in target.Text) hex.Append($"{(int)ch:X4} ");
                LogDiag($"engine zone hex=\"{hex.ToString().TrimEnd()}\" len={target.Text.Length}");
            }
            try { resolved = _resolver.Resolve(target.Text ?? ""); }
            catch (Exception ex)
            {
                LogDiag("engine_error: " + ex.Message);
                HidePopupTransient();
                return;
            }

            LogDiag($"engine zone=\"{target.Text}\" muted=\"{resolved.MutedSource}\" top=\"{resolved.TopLatex}\" ambig={(resolved.Spot == null ? "no" : $"{resolved.Spot.Alternatives.Count} alts")}");

            // Conversion offsets paragraphe → positions absolues document.
            int absStart = paragraphAbsStart + target.Start;
            int absEnd = paragraphAbsStart + target.End;

            if (string.IsNullOrEmpty(resolved.TopLatex))
            {
                HidePopupTransient();
                return;
            }

            // Anti-spam Esc : si l'utilisateur a déjà fermé la popup pour
            // CETTE zone exacte, on ne re-spawn pas. Le flag est reset dès
            // que la zone change (la condition ci-dessous tombe naturellement).
            if (absStart == _dismissedZoneStart && absEnd == _dismissedZoneEnd)
                return;
            // Nouvelle zone → on libère le flag dismissed
            _dismissedZoneStart = -1;
            _dismissedZoneEnd = -1;

            int rawLen = target.Text?.Length ?? 0;
            // _lastZoneSource = source brute telle que dans Word (pas mutée).
            // Les mutations sont gérées à la volée par le résolveur.
            _lastZoneSource = target.Text ?? "";
            ShowPopup(resolved, absStart, absEnd, rawLen, target.Text ?? "");

            // Initialise l'état d'extension itérative depuis la zone NER
            // courante. Permet à Ctrl+Espace suivants d'étendre cette zone
            // (cf. ADR 29-04 iterative-zone-expansion). Sans ce hook,
            // l'extension itérative ne marche que pour les popups venues
            // du manual trigger (TriggerManual), pas du polling NER.
            _iterativeParagraph = _lastParagraph ?? "";
            _iterativeParaAbsStart = paragraphAbsStart;
            _iterativeSpanStart = target.Start;
            _iterativeSpanEnd = target.End;
            _iterativeOMaths = omathRegions;
        }

        /// <summary>
        /// Si l'OMath au caret est à nous (bookmark mcEq_...), on ouvre la
        /// popup d'édition qui propose « Revenir à la saisie initiale ».
        /// Cf. brief docs/dev/briefs/2026-04-27-edit-mode-revert-to-source.md.
        /// Retourne true si la popup edit a été (ou était déjà) affichée pour
        /// cet OMath, false si l'OMath n'est pas à nous.
        /// </summary>
        private bool TryEnterEditMode(Word.OMath om)
        {
            var handleId = FindOurHandleForOMath(om);
            if (handleId == null) return false;

            // Cache la popup de suggestion si elle était ouverte — les deux
            // popups ne doivent pas cohabiter.
            HidePopup();

            _editHandle = new EquationHandle(handleId);

            if (_editPopup == null)
            {
                _editPopup = new EditModePopupWindow();
                _editPopup.RevertRequested += OnRevertRequested;
            }

            // Position : on prend la position du caret (déjà fiable via Win32
            // GetGUIThreadInfo) puis on décale en Y pour passer sous la boîte
            // OMath. Bord droit de la popup aligné avec la position du caret —
            // le caret est dans l'OMath, donc la popup vient se coller à
            // gauche du caret, ne dépasse pas la droite de la zone math.
            //
            // Tentative précédente via Range.Information(wdHorizontalPosition…)
            // sur le END de l'OMath retournait une coordonnée trop à droite
            // (capture user 2026-04-28) — probablement à cause du dropdown
            // handle Word ou du wrapping de la zone OMath. Caret position est
            // plus fiable.
            const double OMathExtraHeightDip = 18.0;
            var caretPos = GetCaretScreenPosition();
            _editPopup.ShowAt(caretPos.x, caretPos.y + OMathExtraHeightDip, alignRight: true);
            LogDiag($"edit mode: handle={handleId} popup at caret-rightaligned ({caretPos.x:F0},{caretPos.y + OMathExtraHeightDip:F0})");
            return true;
        }


        /// <summary>
        /// Action OUI de la popup edit : remplace l'OMath au caret par le
        /// texte source brut, supprime l'entrée du store, repositionne le caret
        /// en fin du texte inséré.
        /// </summary>
        private void OnRevertRequested()
        {
            var handle = _editHandle;
            if (handle == null) { LogDiag("revert: no _editHandle, abort"); return; }

            // Retrouver l'OMath au caret (peut avoir bougé entre l'ouverture
            // de la popup et le clic).
            var om = FindOMathAtCaret();
            if (om == null) { LogDiag("revert: no OMath at caret, abort"); return; }

            // Lire le source
            StoredEquation stored;
            try { stored = _store.RetrieveAsync(handle).GetAwaiter().GetResult(); }
            catch (Exception ex) { LogDiag("revert_retrieve_error: " + ex.Message); return; }
            if (stored == null || string.IsNullOrEmpty(stored.Source))
            {
                LogDiag($"revert: source introuvable pour handle {handle.Id}");
                return;
            }

            string source = stored.Source;
            int omStart, omEnd;
            try { omStart = om.Range.Start; omEnd = om.Range.End; }
            catch (Exception ex) { LogDiag("revert_range_error: " + ex.Message); return; }

            try
            {
                var doc = _app.ActiveDocument;

                // Étendre au bookmark mcEq_ si présent
                string bmName = BookmarkPrefix + handle.Id;
                if (doc.Bookmarks.Exists(bmName))
                {
                    var bm = doc.Bookmarks[bmName];
                    var bmRange = bm.Range;
                    omStart = Math.Min(omStart, bmRange.Start);
                    omEnd = Math.Max(omEnd, bmRange.End);
                    try { bm.Delete(); } catch { }
                }

                // Étendre au ContentControl wrapper si Word en a posé un
                // autour de l'OMath (cas display-mode notamment). Suppression
                // explicite du CC pour éviter de laisser un wrapper vide.
                try
                {
                    foreach (Word.ContentControl cc in doc.ContentControls)
                    {
                        var ccRange = cc.Range;
                        if (ccRange.Start <= omStart && ccRange.End >= omEnd)
                        {
                            omStart = Math.Min(omStart, ccRange.Start);
                            omEnd = Math.Max(omEnd, ccRange.End);
                            try { cc.Delete(true); } catch { } // delete avec contenu
                            break;
                        }
                    }
                }
                catch (Exception ex) { LogDiag("revert_cc_scan_error: " + ex.Message); }

                // Supprime explicitement l'OMath (sinon Word peut garder
                // l'enveloppe math autour du nouveau texte). Puis remplace
                // le range par le texte source brut.
                try { om.Range.Delete(); } catch { }

                var range = doc.Range(omStart, Math.Min(omEnd, doc.Content.End));
                // Le source brut peut contenir \n (séparateurs de lignes d'un
                // MultiLineBlock système ou align*, cf. brief 30-04
                // multiline-systems-equivalences). Au revert, on convertit
                // chaque \n en paragraph mark Word (\r) pour recréer la
                // structure multi-paragraphe d'origine. Length 1:1, donc le
                // calcul de caret ci-dessous reste valide.
                string revertText = source.Replace("\n", "\r");
                range.Text = revertText;

                // Caret en fin du texte inséré
                int newEnd = omStart + revertText.Length;
                try { _app.Selection.SetRange(newEnd, newEnd); } catch { }

                // Click sur la popup WPF a volé le focus à Word. Sans ça, le
                // tick polling qui suivra ne trouvera pas le caret via Win32
                // GetGUIThreadInfo et la popup de conversion s'affichera en
                // (200,200) = haut-gauche du document. On rebascule le focus.
                try { _app.Activate(); } catch { }
            }
            catch (Exception ex) { LogDiag("revert_replace_error: " + ex.Message); return; }

            // Cleanup store
            try { _store.RemoveAsync(handle).GetAwaiter().GetResult(); }
            catch (Exception ex) { LogDiag("revert_store_remove_error: " + ex.Message); }

            // Reset état édition
            _editHandle = null;
            _editingOMathStart = -1;
            _editPopup?.HidePopup();
            LogDiag($"revert: handle={handle.Id} OMath remplacé par source=\"{source}\"");
        }

        // Stopwords courts FR qui bornent la span du trigger manuel (Ctrl+Espace).
        // Idée : quand l'utilisateur force la popup, on prend le texte entre le
        // caret et le dernier "mot-outil" (ou délimiteur, ou OMath précédent).
        // Liste volontairement petite et ciblée — des mots qui introduisent ou
        // séparent des expressions math dans un cours de lycée français.
        private static readonly HashSet<string> ManualTriggerStopwords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "soit", "soient", "et", "ou", "donc", "alors", "avec", "si",
            "on", "car", "mais", "ainsi", "puis", "comme", "tout",
            "un", "une", "le", "la", "les", "des", "du", "de",
            "pour", "par", "sur", "dans", "au", "aux",
        };

        // Délimiteurs qui bornent la span du trigger manuel. Inclut les relations
        // `=`/`<`/`>` : quand l'utilisateur tape "g(x) = (1+x)/V(x+1)" et
        // Ctrl+Espace, il veut convertir le membre droit "(1+x)/V(x+1)", pas
        // la span entière qui commencerait par un `=` (engine produit alors du
        // bruit : "= (1+x)" dropping le reste, etc.).
        // `,` retiré : depuis ADR 29-04 (virgule comme opérateur Bin(",")),
        // la virgule fait partie d'expressions math légitimes (`Vx,y`,
        // `f(x,y)`, `forall x,y dans R`). Plus de coupure manuelle dessus.
        // `:` retiré : depuis ADR 29-04 function-definition, `:` est
        // l'opérateur de définition de fonction (`f:x->expr`). Plus de
        // coupure manuelle dessus.
        private static readonly char[] ManualTriggerDelimiters =
            { '.', ';', '!', '?', '=', '<', '>', '\n', '\r' };

        /// <summary>
        /// Trigger explicite (Ctrl+Espace) : bypass NER, calcule la span
        /// texte à partir du caret en remontant jusqu'au premier "séparateur"
        /// (délimiteur ponctuation, stopword mot-outil, fin d'OMath précédent
        /// ou début du paragraphe), envoie au pattern engine, affiche la popup.
        ///
        /// Utile quand le NER a rendu une partie muette (ex: "Soit f et g"
        /// après conversion de f → "g" n'est plus détecté contextuellement).
        /// L'utilisateur force la conversion de ce qu'il vient de taper.
        /// </summary>
        public void TriggerManual()
        {
            try
            {
                if (_app.Documents.Count == 0) return;

                // Si on est dans un OMath, laisser le flux édition habituel tourner.
                if (FindOMathAtCaret() != null) { CheckAndUpdate(); return; }

                // Extension itérative (ADR 29-04) : si la popup est ouverte
                // ET qu'on a un état d'extension actif, ce Ctrl+Espace étend
                // la zone d'un cran vers la gauche au lieu de re-détecter.
                if (_popup != null && _popup.IsVisible && _iterativeSpanStart >= 0)
                {
                    ExtendOneStop();
                    return;
                }

                var paragraph = _contextReader.ReadCurrentParagraph();
                int caretInParagraph = paragraph.CaretOffset;
                int paragraphAbsStart = paragraph.ParagraphAbsStart;
                string text = paragraph.Text ?? "";
                if (string.IsNullOrEmpty(text) || caretInParagraph <= 0) return;

                int spanStart = ComputeManualSpanStart(text, caretInParagraph, paragraph.OMathRegions);
                // Trim whitespace aux bords. On NE trim PAS les opérateurs
                // binaires (`+`, `-`…) parce que `+inf`, `-5`, `-inf` sont des
                // unaires légitimes en début de span.
                while (spanStart < caretInParagraph && char.IsWhiteSpace(text[spanStart])) spanStart++;
                int spanEnd = caretInParagraph;
                while (spanEnd > spanStart && char.IsWhiteSpace(text[spanEnd - 1])) spanEnd--;
                if (spanEnd <= spanStart) return;

                string span = text.Substring(spanStart, spanEnd - spanStart);
                LogDiag($"manual trigger span=[{spanStart},{spanEnd}] → \"{Preview(span)}\"");

                ResolvedZone resolved;
                try { resolved = _resolver.Resolve(span); }
                catch (Exception ex) { LogDiag("manual_engine_error: " + ex.Message); return; }

                int absStart = paragraphAbsStart + spanStart;
                int absEnd = paragraphAbsStart + spanEnd;

                _lastZoneSource = span;
                _editHandle = null;

                if (string.IsNullOrEmpty(resolved.TopLatex)) return;
                ShowPopup(resolved, absStart, absEnd, span.Length, "manuel: " + span);
                // Entre direct en mode nav : l'utilisateur a demandé explicitement
                // la conversion.
                _popup?.EnterNavMode();

                // Initialise l'état d'extension itérative : chaque Ctrl+Espace
                // suivant tant que la popup est ouverte étendra la zone d'un cran.
                _iterativeParagraph = text;
                _iterativeParaAbsStart = paragraphAbsStart;
                _iterativeSpanStart = spanStart;
                _iterativeSpanEnd = spanEnd;
                _iterativeOMaths = paragraph.OMathRegions;
            }
            catch (Exception ex)
            {
                LogDiag("manual_trigger_error: " + ex.Message);
            }
        }

        /// <summary>
        /// Remonte depuis le caret pour trouver le début de la span manuelle.
        /// Boundary = max de : début du paragraphe, fin du dernier OMath avant
        /// caret, position juste après le dernier délimiteur, position juste
        /// après le dernier stopword mot-outil.
        ///
        /// Détail important : <c>;</c> et <c>,</c> ne sont des délimiteurs QUE
        /// hors brackets/parens. À l'intérieur de <c>[...]</c> ou <c>(...)</c>
        /// ce sont des séparateurs d'intervalle ou d'arguments de fonction, pas
        /// des ruptures de phrase. Sans ce check, <c>[0;+inf[</c> serait coupé
        /// sur le <c>;</c> et la span ne capturerait que <c>+inf[</c>.
        /// </summary>
        /// <summary>
        /// Extension itérative (ADR 29-04) : étend la span vers la gauche
        /// d'un cran. Passe OUTRE la borne actuelle (délim/stopword qui
        /// bloquait le span précédent) et cherche la borne suivante en amont.
        /// Si la borne est un OMath, STOP FINAL : on n'étend pas au-delà.
        /// </summary>
        private void ExtendOneStop()
        {
            if (string.IsNullOrEmpty(_iterativeParagraph))
            {
                LogDiag("iterative extend: empty paragraph, no-op");
                return;
            }
            if (_iterativeSpanStart <= 0)
            {
                LogDiag($"iterative extend: at paragraph start (spanStart=0), no-op");
                return;
            }

            // Recule d'un cran au-delà de la borne courante : on saute le
            // whitespace puis le caractère qui bloquait. Sinon
            // ComputeManualSpanStart trouve la même borne et retourne la
            // même position → no-op.
            int boundary = _iterativeSpanStart - 1;
            while (boundary >= 0 && char.IsWhiteSpace(_iterativeParagraph[boundary])) boundary--;
            if (boundary < 0)
            {
                LogDiag($"iterative extend: at paragraph start, no-op");
                return;
            }

            // Si la borne est DANS un OMath, c'est un stop final : on n'étend
            // pas au-delà (l'OMath précédent est une formule autonome qui ne
            // doit pas être absorbée par l'extension texte).
            if (_iterativeOMaths != null)
            {
                foreach (var (s, e) in _iterativeOMaths)
                {
                    if (s <= boundary && boundary < e)
                    {
                        LogDiag($"iterative extend: blocked by OMath at [{s},{e}], no-op (stop final)");
                        return;
                    }
                }
            }

            // Cherche la borne suivante en amont, en partant de `boundary` (=
            // un cran avant l'ancienne borne, donc strictement plus à gauche).
            int newStart = ComputeManualSpanStart(_iterativeParagraph, boundary, _iterativeOMaths);
            // Trim whitespace en début de la nouvelle zone
            while (newStart < _iterativeSpanEnd && char.IsWhiteSpace(_iterativeParagraph[newStart])) newStart++;

            if (newStart >= _iterativeSpanStart)
            {
                // Vraiment pas d'extension possible.
                LogDiag($"iterative extend no-op: spanStart={_iterativeSpanStart} unchanged (newStart={newStart}, boundary={boundary})");
                return;
            }

            _iterativeSpanStart = newStart;
            string span = _iterativeParagraph.Substring(_iterativeSpanStart, _iterativeSpanEnd - _iterativeSpanStart);
            LogDiag($"iterative extend: span=[{_iterativeSpanStart},{_iterativeSpanEnd}] → \"{Preview(span)}\"");

            ResolvedZone resolved;
            try { resolved = _resolver.Resolve(span); }
            catch (Exception ex) { LogDiag("iterative_extend_error: " + ex.Message); return; }
            if (string.IsNullOrEmpty(resolved.TopLatex)) return;

            int absStart = _iterativeParaAbsStart + _iterativeSpanStart;
            int absEnd = _iterativeParaAbsStart + _iterativeSpanEnd;
            _lastZoneSource = span;
            _editHandle = null;
            ShowPopup(resolved, absStart, absEnd, span.Length, "iterative: " + span);
            _popup?.EnterNavMode();
        }

        /// <summary>Reset l'état d'extension itérative (HidePopup, déplacement caret, etc.).</summary>
        private void ResetIterativeExpansion()
        {
            if (_iterativeSpanStart < 0) return; // already reset, skip log
            _iterativeParagraph = null;
            _iterativeParaAbsStart = -1;
            _iterativeSpanStart = -1;
            _iterativeSpanEnd = -1;
            _iterativeOMaths = null;
        }

        private static int ComputeManualSpanStart(string text, int caret, IReadOnlyList<(int start, int end)> omathRegions)
        {
            int start = 0;

            // Après le dernier délimiteur (point, virgule, etc.) — walk backward
            // avec suivi de profondeur brackets/parens pour ignorer `;` et `,`
            // internes à une structure math.
            int bracketDepth = 0;
            int parenDepth = 0;
            for (int k = caret - 1; k >= 0; k--)
            {
                char c = text[k];
                if (c == ']') { bracketDepth++; continue; }
                if (c == '[') { if (bracketDepth > 0) bracketDepth--; continue; }
                if (c == ')') { parenDepth++; continue; }
                if (c == '(') { if (parenDepth > 0) parenDepth--; continue; }

                if (Array.IndexOf(ManualTriggerDelimiters, c) < 0) continue;
                // `;` et `,` : séparateurs math internes si on est dans [...] ou (...)
                if ((c == ';' || c == ',') && (bracketDepth > 0 || parenDepth > 0)) continue;
                start = Math.Max(start, k + 1);
                break;
            }

            // Après la fin du dernier OMath qui se termine avant le caret
            if (omathRegions != null)
            {
                foreach (var (s, e) in omathRegions)
                {
                    if (e <= caret) start = Math.Max(start, e);
                }
            }

            // Après le dernier stopword (mot entier)
            int i = caret - 1;
            while (i >= start)
            {
                // skip whitespace
                while (i >= start && char.IsWhiteSpace(text[i])) i--;
                if (i < start) break;
                // fin du mot est à i (inclus)
                int wordEnd = i + 1;
                while (i >= start && IsWordChar(text[i])) i--;
                int wordStart = i + 1;
                if (wordEnd <= wordStart) { i--; continue; }
                string w = text.Substring(wordStart, wordEnd - wordStart);
                if (ManualTriggerStopwords.Contains(w))
                {
                    start = wordEnd;
                    break;
                }
                // Sinon on continue à remonter
            }

            return start;
        }

        private static bool IsWordChar(char c) => char.IsLetter(c) || c == '\'' || c == '-';

        // Liste de mots-clés math que le NER rate parfois en début d'expression.
        // On les absorbe dans la zone détectée si ils précèdent immédiatement celle-ci.
        private static readonly HashSet<string> MathPrefixKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "lim", "limite", "lmt",
            "sqrt", "rac", "racine",
            "int", "integrale", "integ", "integral",
            "sum", "somme",
            "forall", "qq", "qqe",
            "exists", "existe",
            "vec", "vect", "vecteur",
        };

        /// <summary>
        /// Décide si une zone NER mérite d'être étendue à droite quand le caret
        /// est plus loin avec whitespace entre. Délégué au <see cref="ZoneResolver"/>
        /// via <see cref="ResolvedZone.IsIncomplete"/> : la zone est incomplète
        /// (donc à étendre) si le rendu contient un Hole non rempli OU si le
        /// dernier char non-whitespace de la source est un opérateur binaire.
        /// </summary>
        private bool ShouldExtendZoneForward(DetectedZone zone)
        {
            if (zone == null || string.IsNullOrEmpty(zone.Text)) return false;
            try { return _resolver.Resolve(zone.Text).IsIncomplete; }
            catch { return false; }
        }

        /// <summary>
        /// Si le caret est juste après la zone NER avec UNIQUEMENT du whitespace
        /// entre (l'utilisateur a tapé un espace pour étendre la formule), on
        /// pousse l'end de la zone jusqu'au caret. Sinon retourne la zone telle
        /// quelle. Évite la fermeture clignotante de la popup à chaque espace
        /// tapé pendant la saisie continue (somme[espace]k[espace]…).
        /// </summary>
        private static DetectedZone TryExtendForwardWhitespace(string paragraph, DetectedZone zone, int caret)
        {
            if (zone == null || string.IsNullOrEmpty(paragraph)) return zone;
            if (caret <= zone.End) return zone; // déjà dans/avant l'end
            int gap = caret - zone.End;
            if (gap > 5) return zone; // trop loin pour étendre
            for (int i = zone.End; i < caret && i < paragraph.Length; i++)
                if (!char.IsWhiteSpace(paragraph[i])) return zone; // non-whitespace → pas notre zone
            // Tout whitespace entre zone.End et caret → on étend
            int newEnd = Math.Min(caret, paragraph.Length);
            string newText = paragraph.Substring(zone.Start, newEnd - zone.Start);
            return new DetectedZone(zone.Start, newEnd, newText, zone.Confidence);
        }

        private static DetectedZone ExtendZoneBackwardWithKeyword(string paragraph, DetectedZone zone)
        {
            if (string.IsNullOrEmpty(paragraph) || zone == null) return zone;

            int i = zone.Start;
            // Skip whitespace juste avant la zone
            while (i > 0 && char.IsWhiteSpace(paragraph[i - 1])) i--;
            int wordEnd = i;
            // Remonte sur le mot alphabétique
            while (i > 0 && char.IsLetter(paragraph[i - 1])) i--;
            int wordStart = i;
            if (wordEnd <= wordStart) return zone;

            string prevWord = paragraph.Substring(wordStart, wordEnd - wordStart);
            if (!MathPrefixKeywords.Contains(prevWord)) return zone;

            // Extension : la zone inclut désormais le mot-clé
            int newEnd = zone.End;
            int newStart = wordStart;
            if (newStart >= 0 && newEnd <= paragraph.Length && newEnd > newStart)
            {
                string newText = paragraph.Substring(newStart, newEnd - newStart);
                return new DetectedZone(newStart, newEnd, newText, zone.Confidence);
            }
            return zone;
        }

        /// <summary>
        /// Jette les zones NER qui chevauchent une région OMath : ces zones sont
        /// déjà converties, pas besoin de les re-proposer.
        /// </summary>
        private static IReadOnlyList<DetectedZone> FilterOutOMathOverlap(
            IReadOnlyList<DetectedZone> zones, IReadOnlyList<(int start, int end)> regions)
        {
            if (zones == null || zones.Count == 0 || regions == null || regions.Count == 0)
                return zones ?? Array.Empty<DetectedZone>();
            var kept = new List<DetectedZone>(zones.Count);
            foreach (var z in zones)
            {
                bool overlaps = false;
                foreach (var (s, e) in regions)
                {
                    // Chevauchement strict : [z.Start, z.End) intersecte [s, e)
                    if (z.End > s && z.Start < e) { overlaps = true; break; }
                }
                if (!overlaps) kept.Add(z);
            }
            return kept;
        }

        private static DetectedZone PickNearestZone(IReadOnlyList<DetectedZone> zones, int caret, out int bestDist)
        {
            DetectedZone best = null;
            bestDist = int.MaxValue;
            foreach (var z in zones)
            {
                int dist;
                if (caret >= z.Start && caret <= z.End) dist = 0;       // curseur dedans ou collé au bord
                else if (caret < z.Start) dist = z.Start - caret;       // zone après le curseur
                else dist = caret - z.End;                              // zone avant le curseur
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = z;
                }
            }
            return best;
        }

        // ============================================================
        // Positionnement popup (inchangé du heuristique)
        // ============================================================

        private const double AvgCharWidthDip = 7.0;
        private const double PopupWidthDip = 280.0;

        /// <summary>
        /// Handler du click "Signaler une erreur" : construit le rapport avec le
        /// contexte courant (NER text, candidat sélectionné, log tail, versions),
        /// ouvre le <see cref="FeedbackDialog"/> modal, laisse le sender faire
        /// l'envoi. Non-bloquant vis-à-vis du polling popup (Word reprend la main
        /// après fermeture du dialog).
        /// </summary>
        /// <summary>
        /// Appelé quand l'utilisateur clique sur la formule finale dans la
        /// popup (équivalent d'un Enter sur la finale). Appelle la même
        /// logique de commit que CommitSelected.
        /// </summary>
        private void OnPopupCommitRequested()
        {
            try
            {
                if (_popup == null || !_popup.IsVisible) return;
                if (_lastZoneAbsStart < 0 || _lastZoneAbsEnd <= _lastZoneAbsStart) return;
                var latex = _popup.CurrentFinalLatex ?? "";
                if (string.IsNullOrWhiteSpace(latex)) return;
                CommitLatexAndOMath(latex, _lastZoneSource ?? "");
            }
            catch (Exception ex) { LogDiag("popup_click_commit_error: " + ex.Message); }
        }

        private void OnReportRequested()
        {
            // Séquence importante :
            //   1. CAPTURER d'abord le screen — popup de suggestion toujours
            //      visible : c'est CE que voit l'user au moment du bug, donc
            //      utile pour le debug (rendu visuel + position popup).
            //   2. PUIS cacher la popup pour ne pas qu'elle gêne le dialog
            //   3. PUIS ouvrir le dialog (qui apparaîtra APRÈS capture, donc
            //      ne pollue pas l'image)
            byte[] preScreenshot = null;
            try { preScreenshot = FeedbackBundle.CaptureScreenshotPng(); } catch { }
            try { HidePopup(); } catch { }

            try
            {
                var report = BuildFeedbackReport();
                if (preScreenshot != null && preScreenshot.Length > 0)
                    report.ScreenshotPngBase64 = Convert.ToBase64String(preScreenshot);
                var sender = Feedback.FeedbackSenderFactory.Create();
                var dialog = new FeedbackDialog(report, sender);
                dialog.ShowDialog();
            }
            catch (Exception ex) { LogDiag("feedback_dialog_error: " + ex.Message); }
        }

        /// <summary>
        /// Construit un <see cref="Feedback.FeedbackReport"/> pré-rempli à partir
        /// du <see cref="LastActionSnapshot"/> (saisie + popup + commit éventuel)
        /// + métadonnées env (version add-in, Word, OS, .NET).
        ///
        /// Si aucune action depuis le démarrage de Word (snapshot null), retourne
        /// un report vide avec juste les métadonnées — l'utilisateur devra remplir
        /// les 3 champs à la main dans la fenêtre.
        ///
        /// Public pour que <see cref="ThisAddIn"/> y accède depuis le ribbon.
        /// </summary>
        public Feedback.FeedbackReport BuildFeedbackReport()
        {
            // Word.Application.Version = "16.0" (peu utile pour le triage),
            // Word.Application.Build = "16.0.18526.20144" (build complet =
            // précieux pour distinguer les bugs spécifiques à un build OMath).
            // On préfère Build et on tombe sur Version en fallback.
            string wordVersion = "?";
            try { wordVersion = _app?.Build ?? _app?.Version ?? "?"; } catch { }

            string version = "?";
            try { version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?"; } catch { }

            var snap = _lastAction;
            string proposed = snap?.ProposedLatex ?? "";
            string committed = snap?.CommittedLatex ?? "";

            // Si l'user n'a PAS encore committé (CommittedLatex vide) mais
            // qu'on a une proposition, on simule la conversion LaTeX →
            // UnicodeMath comme si Enter avait été pressé. Cas typique :
            // l'user voit un mauvais rendu dans la popup et clique
            // "Signaler" sans valider — on doit quand même renseigner ce
            // que Word RECEVRAIT pour que le rapport soit utile.
            if (string.IsNullOrEmpty(committed) && !string.IsNullOrEmpty(proposed))
            {
                try { committed = MathCursor.Core.LatexToUnicodeMath.Convert(proposed); }
                catch { /* best-effort, on laisse vide si la conversion plante */ }
            }

            return new Feedback.FeedbackReport
            {
                Version = version,
                Timestamp = DateTimeOffset.UtcNow,
                UserId = Feedback.UserIdStore.GetOrCreate(),
                SessionId = _sessionId,
                NerText = snap?.SourceText ?? (_lastZoneSource ?? ""),
                RecognizedFormula = proposed,
                CommittedLatex = committed,
                ParagraphContext = snap?.ParagraphContext ?? "",
                WordVersion = wordVersion,
                OsVersion = Environment.OSVersion.ToString(),
                DotNetVersion = Environment.Version.ToString(),
                // LogTail / ScreenshotPngBase64 remplis par la fenêtre selon
                // les toggles (pas ici).
            };
        }

        private void ShowPopup(ResolvedZone resolved, int absStart, int absEnd, int rawZoneLength, string debugText = "")
        {
            if (_popup == null)
            {
                _popup = new SuggestionPopupWindow();
                _popup.ReportRequested += OnReportRequested;
                _popup.SourceMutationRequested += OnSourceMutationRequested;
                _popup.CommitRequested += OnPopupCommitRequested;
            }

            // Repositionnement : seulement si nouvelle zone, sinon on garde la
            // position actuelle (clic dans la popup → Word perd focus, GetCaretPos
            // rate et renverrait fallback 200,200).
            bool shouldReposition =
                !_popup.IsVisible
                || absStart != _lastZoneAbsStart || absEnd != _lastZoneAbsEnd;

            double popupX, popupY;
            if (shouldReposition)
            {
                var pos = GetCaretScreenPosition();
                double zoneWidth = Math.Max(0, rawZoneLength) * AvgCharWidthDip;
                double offset = Math.Min(zoneWidth, PopupWidthDip);
                popupX = pos.x - offset;
                if (popupX < 0) popupX = 0;
                popupY = pos.y;
            }
            else
            {
                popupX = _popup.Left;
                popupY = _popup.Top;
            }

            _lastZoneAbsStart = absStart;
            _lastZoneAbsEnd = absEnd;

            // Snapshot pour la fenêtre "Signaler une erreur" : on capture la
            // saisie source + ce que MathCursor propose. Le CommittedLatex sera
            // rempli plus tard (avant InsertOMathAt) si l'user commit.
            try
            {
                _lastAction = new LastActionSnapshot
                {
                    At = DateTime.UtcNow,
                    SourceText = _lastZoneSource ?? string.Empty,
                    ProposedLatex = resolved?.TopLatex ?? string.Empty,
                    CommittedLatex = null,
                    ParagraphContext = ReadParagraphContextForReport(),
                };
            }
            catch { /* le snapshot est best-effort, jamais bloquant pour la popup */ }

            var alts = resolved.Spot?.Alternatives
                ?? (IReadOnlyList<MathCursor.Core.Lattice.AmbiguityAlternative>)Array.Empty<MathCursor.Core.Lattice.AmbiguityAlternative>();
            string ruleId = resolved.Spot?.RuleId ?? "";
            int spotStart = resolved.SpotStart ?? -1;
            int spotEnd = resolved.SpotEnd ?? -1;
            _popup.Show(resolved.TopLatex, ruleId, alts, spotStart, spotEnd,
                resolved.AllMatches, popupX, popupY, debugText);
        }

        /// <summary>
        /// Appelé quand l'utilisateur résout une alt avec
        /// <see cref="MathCursor.Core.Lattice.SourceMutation"/> (ex: V→forall).
        /// On délègue tout au <see cref="ZoneResolver"/> : il mémorise la
        /// préférence et la prochaine résolution applique la mutation.
        ///
        /// Invariant clé : le ContentControl Word garde le source brut tapé
        /// par l'utilisateur (`V x R`) jusqu'au commit Enter final qui crée
        /// l'OMath. Les mutations sont une couche mémoire dans le résolveur,
        /// pas une réécriture du document.
        /// </summary>
        private void OnSourceMutationRequested(string ruleId, int altIdx,
            MathCursor.Core.Lattice.SourceMutation mutation)
        {
            try
            {
                if (mutation == null || string.IsNullOrEmpty(ruleId)) return;
                _resolver.AddPreference(ruleId, altIdx);

                var src = _lastZoneSource ?? string.Empty;
                var resolved = _resolver.Resolve(src);
                LogDiag($"pref applied rule=\"{ruleId}\" altIdx={altIdx} src=\"{src}\" → muted=\"{resolved.MutedSource}\" incomplete={resolved.IsIncomplete}");

                // Auto-commit retiré (29-04). Avec la décomposition modulaire
                // de forall (Const " \forall " seul), la mutation V→forall sur
                // `V` produit `\forall ` qui a IsIncomplete=false alors que
                // sémantiquement il manque var et ensemble. L'auto-commit
                // "volait" la frappe de l'utilisateur. Désormais l'utilisateur
                // commit toujours via flèche bas + Enter, comportement prévisible.
                ShowPopup(resolved, _lastZoneAbsStart, _lastZoneAbsEnd, src.Length, debugText: resolved.MutedSource);
            }
            catch (Exception ex)
            {
                LogDiag("source_mutation_error: " + ex.Message);
            }
        }

        /// <summary>
        /// Commit du candidat sélectionné :
        ///  - mode normal → insère un nouvel OMath, crée bookmark mcEq_ID, persiste
        ///    la source brute dans la store.
        ///  - mode édition (_editHandle != null) → remplace l'OMath existant et
        ///    conserve le même handle/bookmark (la source brute ne change pas).
        /// Retourne true si le commit a été fait (Enter consommé), false sinon.
        /// </summary>
        public bool CommitSelected()
        {
            // Mode édition : Enter passe à Word (édition math native). Le
            // revert se fait par click souris sur la popup edit, pas Enter.
            if (_popup == null || !_popup.IsVisible || !_popup.IsNavMode) return false;
            if (_lastZoneAbsStart < 0 || _lastZoneAbsEnd <= _lastZoneAbsStart) return false;

            // Si l'utilisateur a Enter sur une alternative (focus alts), on
            // résout localement et on garde la popup ouverte. Le commit Word
            // se fera au prochain Enter (focus passe automatiquement sur final).
            if (!_popup.IsFocusOnFinal)
            {
                if (_popup.ResolveCurrentAltIfFocused()) return true;
            }

            // Sinon : commit la formule finale (intègre les éventuelles
            // résolutions d'alternatives faites avant).
            var latex = _popup.CurrentFinalLatex ?? "";
            if (string.IsNullOrWhiteSpace(latex)) return false;

            return CommitLatexAndOMath(latex, _lastZoneSource ?? "");
        }

        /// <summary>
        /// Insère un OMath au niveau de la zone courante avec le LaTeX donné,
        /// crée le bookmark et persiste la source dans le store, reset l'état
        /// et cache la popup. Utilisé par <see cref="CommitSelected"/> (Enter
        /// final) ET par <see cref="OnSourceMutationRequested"/> en mode
        /// auto-commit (résolution d'une alt source-mutation qui ferme la
        /// formule, ex: V x R → \forall x \in R, plus rien à attendre).
        /// </summary>
        private bool CommitLatexAndOMath(string latex, string source)
        {
            if (string.IsNullOrWhiteSpace(latex)) return false;

            // Suspend le repaint Word pour TOUTE la durée du commit (insertion,
            // BuildUp, bookmark, store, finalize cross-merge). Sans ça
            // l'utilisateur voit les états intermédiaires : OMath qui apparaît,
            // ¶ qui se crée, caret qui saute. ScreenUpdating=false → Word
            // batch les mutations, repaint atomique au moment du restore.
            // Cf. user 04-05 « micro saut du caret ».
            bool prevScreenUpdating = true;
            try { prevScreenUpdating = _app.ScreenUpdating; _app.ScreenUpdating = false; } catch { }
            try
            {
                return CommitLatexAndOMathCore(latex, source);
            }
            finally
            {
                try { _app.ScreenUpdating = prevScreenUpdating; } catch { }
            }
        }

        /// <summary>
        /// Corps du commit (séparé de l'enveloppe ScreenUpdating). Cf.
        /// <see cref="CommitLatexAndOMath"/> pour le wrapper.
        /// </summary>
        private bool CommitLatexAndOMathCore(string latex, string source)
        {
            var editing = _editHandle;

            // Merge avec OMaths adjacents (ADR 29-04). Pas en mode édition
            // (le mode revert remplace l'OMath en cours, pas de fusion).
            // ORDRE :
            //  1. Intra-paragraphe (TryMergeWithAdjacentOMaths) — gagne toujours
            //     quand applicable (cf. brief 30-04 §3.1 précédence).
            //  2. Cross-paragraphe (TryFindCrossMergeAbove) — Phase 1 align*
            //     uniquement, déclenché si ligne courante = marqueur align ET
            //     ¶ précédent termine par OMath à nous.
            bool wasCrossParagraphMerge = false;
            if (editing == null)
            {
                var merged = TryMergeWithAdjacentOMaths(_lastZoneAbsStart, _lastZoneAbsEnd, source);
                if (merged == null)
                {
                    // Pas d'intra-merge → tenter cross-paragraphe (brief 30-04)
                    merged = TryFindCrossMergeAbove(_lastZoneAbsStart, _lastZoneAbsEnd, source);
                    if (merged != null) wasCrossParagraphMerge = true;
                }
                if (merged != null)
                {
                    _lastZoneAbsStart = merged.AbsStart;
                    _lastZoneAbsEnd = merged.AbsEnd;
                    source = merged.MergedSource;
                    // Recalcule le LaTeX sur le source mergé via le pipeline
                    try
                    {
                        var resolved = _resolver.Resolve(source);
                        if (!string.IsNullOrEmpty(resolved.TopLatex)) latex = resolved.TopLatex;
                    }
                    catch (Exception ex) { LogDiag("merge_resolve_error: " + ex.Message); }
                    // Supprime les anciens handles du store ET les bookmarks
                    // Word `mcEq_<handleId>` (sinon FindOurHandleForOMath
                    // retrouve l'ancien handle fantôme au prochain merge tentative
                    // → store retourne null/empty → log "stored null or empty source").
                    foreach (var h in merged.RemovedHandles)
                    {
                        try { _store.RemoveAsync(new EquationHandle(h)).GetAwaiter().GetResult(); }
                        catch (Exception ex) { LogDiag($"merge_remove_error handle={h}: {ex.Message}"); }
                        try { DeleteBookmarkByHandle(h); }
                        catch (Exception ex) { LogDiag($"merge_bookmark_delete_error handle={h}: {ex.Message}"); }
                    }

                    // Supprime explicitement les OMaths qui chevauchent le range
                    // mergé : Word refuse d'écraser un OMath via Range.Text =
                    // "..." (lève "The range cannot be deleted"). Doit être fait
                    // AVANT InsertOMathAt. On itère en sens descendant pour que
                    // les suppressions n'invalident pas les positions précédentes.
                    int rangeShrink = DeleteOMathsInRange(_lastZoneAbsStart, _lastZoneAbsEnd);
                    _lastZoneAbsEnd -= rangeShrink;

                    LogDiag($"merge: {merged.RemovedHandles.Count} OMath(s) absorbés range=[{_lastZoneAbsStart},{_lastZoneAbsEnd}] (shrunk by {rangeShrink}) mergedSource=\"{source}\" latex=\"{latex}\"");
                }
            }

            // Snapshot : on enregistre le LaTeX qui va être committé pour que
            // la fenêtre "Signaler une erreur" puisse le pré-remplir.
            try
            {
                if (_lastAction == null)
                {
                    _lastAction = new LastActionSnapshot
                    {
                        At = DateTime.UtcNow,
                        SourceText = source ?? string.Empty,
                        ParagraphContext = ReadParagraphContextForReport(),
                    };
                }
                _lastAction.CommittedLatex = latex ?? string.Empty;
                _lastAction.At = DateTime.UtcNow;
            }
            catch { /* best-effort */ }

            try
            {
                var (newStart, newEnd) = InsertOMathAt(_lastZoneAbsStart, _lastZoneAbsEnd, latex);
                bool insertionSucceeded = newEnd > newStart;
                if (!insertionSucceeded)
                {
                    LogDiag($"commit ABORTED latex=\"{latex}\" — OMath build failed, rollback effectué dans InsertOMathAt");
                }
                else if (editing != null)
                {
                    LogDiag($"edit commit handle={editing.Id} latex=\"{latex}\"");
                }
                else
                {
                    var handle = new EquationHandle(NewHandleId());
                    CreateBookmarkForRange(handle.Id, newStart, newEnd);
                    try
                    {
                        _store.StoreAsync(handle, source, new EquationMetadata
                        {
                            SourceLanguage = "fr",
                            CreatedAt = DateTimeOffset.UtcNow,
                        }).GetAwaiter().GetResult();
                    }
                    catch (Exception ex) { LogDiag("store_save_error: " + ex.Message); }
                    LogDiag($"insert commit handle={handle.Id} range=[{newStart},{newEnd}] latex=\"{latex}\" source=\"{source}\"");
                }

                // Phase 4 du pipeline cross-merge (cf. ADR 04-05) : finalise
                // le layout (strip ¶ vide / align / append ¶ après / caret).
                // Encapsulé dans une méthode dédiée pour découpler les étapes.
                if (insertionSucceeded && wasCrossParagraphMerge)
                {
                    var doc = _app.ActiveDocument;
                    if (doc != null) FinalizeCrossMergeLayout(doc, ref newStart, ref newEnd);
                }
            }
            catch (Exception ex)
            {
                LogDiag("commit_error: " + ex.Message);
            }

            // Reset état
            _lastZoneAbsStart = -1;
            _lastZoneAbsEnd = -1;
            _lastZoneSource = "";
            _editHandle = null;
            _editingOMathStart = -1;
            _lastCommitUtc = DateTime.UtcNow;
            HidePopup();
            return true;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Pipeline cross-merge : Phase 4 (finalisation layout)
        //  Cf. ADR 2026-05-04-Meta-cross-merge-pipeline-refactor.md
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Phase 4 du pipeline cross-merge : finalise le layout après l'insertion
        /// brute de l'OMath multi-ligne. Orchestre 4 sous-étapes séquentielles :
        /// <list type="number">
        /// <item>Strip du <c>¶</c> vide en tête (résidu du paragraphe remplacé
        /// par BuildUp Word sur <c>█(...)</c>).</item>
        /// <item>Application de l'alignement OOXML sur le paragraphe OMath
        /// (<c>m:oMathParaPr/m:jc</c>).</item>
        /// <item>Création d'un nouveau paragraphe vide APRÈS l'OMath
        /// (<see cref="Word.Range.InsertParagraphAfter"/>, API native).</item>
        /// <item>Positionnement du caret dans ce nouveau paragraphe, hors
        /// zone math.</item>
        /// </list>
        /// <para>
        /// Note : <c>ScreenUpdating=false</c> est géré par le wrapper
        /// <see cref="CommitLatexAndOMath"/> qui couvre tout le commit (pas
        /// que la phase 4) — sans ça l'utilisateur voyait les états
        /// intermédiaires de l'insertion (cf. user 04-05).
        /// </para>
        /// <paramref name="newStart"/> et <paramref name="newEnd"/> sont mis
        /// à jour si le strip décale les positions, ce qui permet au caller
        /// de continuer à les utiliser.
        /// </summary>
        private void FinalizeCrossMergeLayout(Word.Document doc, ref int newStart, ref int newEnd)
        {
            try
            {
                StripLeadingResidualEmptyParagraph(doc, ref newStart, ref newEnd);
                EnforceOMathParagraphAlignment(doc, newStart);
                int caretPos = AppendEmptyParagraphAfterOMath(doc, newStart);
                if (caretPos >= 0) SetCaretAtPosition(caretPos);
            }
            catch (Exception ex) { LogDiag("xparMerge_finalize_error: " + ex.Message); }
        }

        /// <summary>
        /// Phase 4.1 : supprime le <c>¶</c> vide qui peut subsister juste avant
        /// l'OMath après cross-merge. Word's BuildUp sur <c>█(...)</c> crée
        /// l'OMathPara dans son propre paragraphe et laisse parfois un <c>¶</c>
        /// orphelin du paragraphe remplacé. On vérifie que le paragraphe
        /// candidat est bien vide ET qu'il ne contient PAS d'OMath (un OMath
        /// inline a <c>Text=""</c> mais ne doit pas être supprimé).
        /// <paramref name="newStart"/> et <paramref name="newEnd"/> sont
        /// décalés du nombre de chars supprimés.
        /// </summary>
        private void StripLeadingResidualEmptyParagraph(Word.Document doc, ref int newStart, ref int newEnd)
        {
            if (newStart <= doc.Content.Start) return;
            try
            {
                var prevRange = doc.Range(newStart - 1, newStart - 1).Paragraphs[1].Range;
                bool hasOMath = false;
                try { hasOMath = prevRange.OMaths != null && prevRange.OMaths.Count > 0; } catch { }
                if (hasOMath) return;
                string prevText = prevRange.Text ?? "";
                if (prevText.Replace("\r", "").Replace("\n", "").Trim().Length > 0) return;
                int delLen = prevRange.End - prevRange.Start;
                prevRange.Delete();
                newStart -= delLen;
                newEnd -= delLen;
            }
            catch (Exception ex) { LogDiag("xparMerge_strip_lead_para_error: " + ex.Message); }
        }

        /// <summary>
        /// Phase 4.2 (réutilisable) : applique l'alignement du paragraphe Word
        /// sur l'OMath qui le contient. Délègue à
        /// <see cref="SyncOMathJustificationToParagraph"/>. Appelable depuis
        /// d'autres flows (mode édition, single-eq) sans dupliquer le wrapping
        /// try/catch.
        /// </summary>
        private void EnforceOMathParagraphAlignment(Word.Document doc, int pos)
        {
            try { SyncOMathJustificationToParagraph(doc, pos, pos); }
            catch (Exception ex) { LogDiag("xparMerge_enforce_align_error: " + ex.Message); }
        }

        /// <summary>
        /// Phase 4.3 (réutilisable) : insère un paragraphe vide APRÈS celui
        /// qui contient l'OMath couvrant <paramref name="posInOMath"/>.
        /// Utilise <see cref="Word.Range.InsertParagraphAfter"/> (API Word
        /// native, gère correctement le dernier <c>¶</c> du document
        /// contrairement à un <c>Range.Text = "\r"</c> manuel qui peut être
        /// normalisé/bouffé à la frontière fin de doc).
        /// Retourne la position de début du nouveau paragraphe (où placer
        /// le caret), ou <c>-1</c> si aucun OMath ne couvre la position.
        /// </summary>
        private int AppendEmptyParagraphAfterOMath(Word.Document doc, int posInOMath)
        {
            try
            {
                foreach (Word.OMath om in doc.OMaths)
                {
                    if (om.Range.Start > posInOMath || om.Range.End <= posInOMath) continue;
                    var omPara = om.Range.Paragraphs[1];
                    omPara.Range.InsertParagraphAfter();
                    return omPara.Range.End;
                }
            }
            catch (Exception ex) { LogDiag("xparMerge_append_para_error: " + ex.Message); }
            return -1;
        }

        /// <summary>
        /// Phase 4.4 (réutilisable) : positionne le caret à la position donnée.
        /// Utilisé après <see cref="AppendEmptyParagraphAfterOMath"/> pour
        /// déposer le caret dans le nouveau paragraphe vide. La position est
        /// supposée être hors zone math (le paragraphe vient juste d'être
        /// créé indépendamment de l'OMath par <c>InsertParagraphAfter</c>).
        /// </summary>
        private void SetCaretAtPosition(int caretPos)
        {
            try { _app.Selection.SetRange(caretPos, caretPos); }
            catch (Exception ex) { LogDiag("xparMerge_setcaret_error: " + ex.Message); }
        }

        // ─────────────────────────────────────────────────────────────────
        //  Alignement OMath ↔ paragraphe (réutilisable)
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Aligne l'OMath couvrant <paramref name="pos"/> sur l'alignement du
        /// paragraphe Word qui le contient.
        /// <para>
        /// Word centre par défaut les équations display via
        /// <c>OMathPara.Justification = wdOMathJcCenterGroup</c>, ce qui ne
        /// respecte pas le choix utilisateur. On lit l'alignement du paragraphe
        /// (<c>Left/Center/Right/Justify</c>), on le map vers
        /// <see cref="Word.WdOMathJc"/>, puis :
        /// </para>
        /// <list type="number">
        /// <item>Typed setter sur <see cref="Word.OMath.Justification"/>
        /// (couvre les OMath inline qui ne sont pas wrappés dans un OMathPara
        /// display — l'inline suit naturellement l'alignement paragraphe, le
        /// setter est ceinture-bretelles).</item>
        /// <item>Patch direct du WordOpenXML pour OMathPara : la PIA Office15
        /// n'expose pas la collection <c>OMathParagraphs</c> ni en typé ni en
        /// IDispatch sur ce Word (vérifié <c>DISP_E_UNKNOWNNAME</c> dans tous
        /// les logs). Le patch injecte
        /// <c>&lt;m:oMathParaPr&gt;&lt;m:jc m:val="..."/&gt;&lt;/m:oMathParaPr&gt;</c>
        /// après <c>&lt;m:oMathPara&gt;</c> — exactement ce que le bouton
        /// « Aligner à gauche » du ribbon ajoute (vérifié sur align.docx).</item>
        /// </list>
        /// Ne modifie JAMAIS le paragraphe texte lui-même : on respecte le
        /// choix utilisateur.
        /// </summary>
        private void SyncOMathJustificationToParagraph(Word.Document doc, int pos, int spanEnd)
        {
            try
            {
                int omathJc = MapParagraphAlignToOMathJc(ReadParagraphAlignment(doc, pos));

                // 1) Typed OMath.Justification setter
                foreach (Word.OMath om in doc.OMaths)
                {
                    var r = om.Range;
                    if (r.Start > pos || r.End <= pos) continue;
                    try { om.Justification = (Word.WdOMathJc)omathJc; } catch { }
                    break;
                }

                // 2) Patch OOXML pour OMathPara (seul path qui marche sur cette PIA/Word)
                PatchOMathParaJustificationViaXml(doc, pos, omathJc);
            }
            catch (Exception ex) { LogDiag("align_sync_error: " + ex.Message); }
        }

        /// <summary>
        /// Lit l'alignement du paragraphe Word à la position donnée. Retourne
        /// <c>0</c> (Left) par défaut si la lecture échoue. Utilise reflection
        /// (InvokeMember) car certaines PIA exposent <c>ParagraphFormat.Alignment</c>
        /// uniquement en late-binding.
        /// </summary>
        private static int ReadParagraphAlignment(Word.Document doc, int pos)
        {
            try
            {
                var format = doc.Range(pos, pos).Paragraphs[1].Format;
                return (int)format.GetType().InvokeMember(
                    "Alignment", System.Reflection.BindingFlags.GetProperty,
                    null, format, null);
            }
            catch { return 0; }
        }

        /// <summary>
        /// Map WdParagraphAlignment → WdOMathJc.
        /// WdParagraphAlignment : Left=0, Center=1, Right=2, Justify=3 (et variantes 4-9 rares).
        /// WdOMathJc             : CenterGroup=1, Center=2, Left=3, Right=4, Inline=7.
        /// Justify → Left (l'équation tient sur une ligne, justify dégénère en left).
        /// </summary>
        private static int MapParagraphAlignToOMathJc(int paragraphAlign)
        {
            switch (paragraphAlign)
            {
                case 1: return 2; // Center
                case 2: return 4; // Right
                default: return 3; // Left (couvre Left, Justify, et les variantes rares)
            }
        }

        /// <summary>
        /// Patch OOXML : injecte/remplace &lt;m:oMathParaPr&gt;&lt;m:jc m:val="..."/&gt;&lt;/m:oMathParaPr&gt;
        /// après &lt;m:oMathPara&gt; dans le paragraphe contenant pos. C'est exactement
        /// ce que le bouton « Aligner à gauche » du ribbon Word ajoute (vérifié
        /// sur docx test : centré = pas de m:oMathParaPr / aligné gauche =
        /// m:oMathParaPr présent avec m:jc). Utilisé quand OMathParagraphs n'est
        /// exposé ni par la PIA ni par IDispatch.
        /// </summary>
        private void PatchOMathParaJustificationViaXml(Word.Document doc, int pos, int omathJc)
        {
            string targetVal = OMathJcToOoxmlVal(omathJc);
            if (targetVal == null) return;
            try
            {
                var probeRange = doc.Range(pos, pos);
                var paras = probeRange.Paragraphs;
                if (paras == null || paras.Count == 0) return;
                var paraRange = paras[1].Range;
                string xml = paraRange.WordOpenXML;
                if (string.IsNullOrEmpty(xml)) return;
                if (xml.IndexOf("<m:oMathPara", StringComparison.Ordinal) < 0) return;
                bool changed;
                string patched = PatchOMathParaJc(xml, targetVal, out changed);
                // Réinsertion forcée même si contenu identique : le set typé
                // OMath.Justification met à jour le XML mais ne déclenche pas
                // de re-layout. InsertXML re-process le paragraphe et force le
                // repaint (équivalent du clic ribbon).
                paraRange.InsertXML(patched);
            }
            catch (Exception ex) { LogDiag("align_sync_xml_error: " + ex.Message); }
        }

        private static string OMathJcToOoxmlVal(int jc)
        {
            switch (jc)
            {
                case 1: return "centerGroup";
                case 2: return "center";
                case 3: return "left";
                case 4: return "right";
                default: return null;
            }
        }

        // Patch les attributs de justification OMathPara dans l'OOXML d'un
        // paragraphe. Trois cas couverts (cas 4 = cas réel observé sur docx user) :
        //   1. <m:oMathParaPr>...<m:jc m:val="X"/>...</m:oMathParaPr> → remplace X
        //   2. <m:oMathParaPr/> auto-fermant → remplace par bloc complet
        //   3. <m:oMathParaPr> ouvert sans m:jc → injecte m:jc en tête
        //   4. <m:oMathPara> sans m:oMathParaPr (cas par défaut Word) → injecte tout
        private static readonly Regex _rxJcVal = new Regex(
            @"(<m:oMathParaPr[^>]*>(?:(?!</m:oMathParaPr>).)*?<m:jc\s+m:val="")[^""]*("")",
            RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex _rxParaPrSelfClosing = new Regex(
            @"<m:oMathParaPr\s*/>",
            RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex _rxParaPrNoJc = new Regex(
            @"<m:oMathParaPr\s*>(?!\s*<m:jc)",
            RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex _rxParaNoParaPr = new Regex(
            @"<m:oMathPara\s*>(?!\s*<m:oMathParaPr)",
            RegexOptions.Singleline | RegexOptions.Compiled);

        internal static string PatchOMathParaJc(string xml, string targetVal, out bool changed)
        {
            changed = false;
            if (string.IsNullOrEmpty(xml) || string.IsNullOrEmpty(targetVal)) return xml;

            // Cas 1 : m:jc existe déjà — remplace le val (s'il diffère)
            if (_rxJcVal.IsMatch(xml))
            {
                bool needsChange = false;
                string updated = _rxJcVal.Replace(xml, m =>
                {
                    string current = m.Value;
                    int valStart = current.IndexOf("m:val=\"", StringComparison.Ordinal) + 7;
                    int valEnd = current.IndexOf('"', valStart);
                    string currentVal = current.Substring(valStart, valEnd - valStart);
                    if (currentVal != targetVal) needsChange = true;
                    return m.Groups[1].Value + targetVal + m.Groups[2].Value;
                });
                changed = needsChange;
                return needsChange ? updated : xml;
            }

            string injection = "<m:oMathParaPr><m:jc m:val=\"" + targetVal + "\"/></m:oMathParaPr>";

            // Cas 2 : <m:oMathParaPr/> auto-fermant
            if (_rxParaPrSelfClosing.IsMatch(xml))
            {
                changed = true;
                return _rxParaPrSelfClosing.Replace(xml, injection, 1);
            }

            // Cas 3 : <m:oMathParaPr> ouvert sans m:jc — injecte m:jc juste après le tag
            if (_rxParaPrNoJc.IsMatch(xml))
            {
                changed = true;
                return _rxParaPrNoJc.Replace(xml, "<m:oMathParaPr><m:jc m:val=\"" + targetVal + "\"/>", 1);
            }

            // Cas 4 : <m:oMathPara> sans m:oMathParaPr du tout (default Word)
            if (_rxParaNoParaPr.IsMatch(xml))
            {
                changed = true;
                return _rxParaNoParaPr.Replace(xml, "<m:oMathPara>" + injection, 1);
            }

            return xml;
        }

        private static bool IsWhitespaceCharAt(Word.Document doc, int pos)
        {
            try
            {
                var t = doc.Range(pos, pos + 1).Text ?? "";
                return t.Length > 0 && char.IsWhiteSpace(t[0]);
            }
            catch { return false; }
        }

        // Strict : un seul caractère espace UNIQUEMENT. Pour la fusion d'OMaths :
        // on accepte 0 ou 1 espace entre eux. Pas de tab, pas d'espaces multiples,
        // pas de newline. Spec utilisateur 29-04 (révision du brief original).
        private static bool IsSingleSpaceAt(Word.Document doc, int pos)
        {
            try
            {
                var t = doc.Range(pos, pos + 1).Text ?? "";
                return t.Length > 0 && t[0] == ' ';
            }
            catch { return false; }
        }

        /// <summary>
        /// Résultat d'une tentative de merge : nouvelles positions absolues
        /// englobant les OMaths fusionnés, source mergé, handles à supprimer
        /// du store. Null si pas de fusion possible.
        /// </summary>
        private sealed class MergeResult
        {
            public int AbsStart { get; set; }
            public int AbsEnd { get; set; }
            public string MergedSource { get; set; }
            public List<string> RemovedHandles { get; set; }
        }

        /// <summary>
        /// Cherche des OMaths adjacents à la zone à insérer (avant et/ou après,
        /// avec uniquement espaces/tabs entre). Pour chaque OMath trouvé qui a
        /// un handle MathCursor connu, fusionne son source dans le source mergé.
        /// OMaths sans handle (insertion native Word ou collés depuis ailleurs)
        /// ne sont pas fusionnés — fallback comportement actuel (OMaths séparés).
        /// </summary>
        private MergeResult TryMergeWithAdjacentOMaths(int absStart, int absEnd, string middleSource)
        {
            try
            {
                var doc = _app.ActiveDocument;
                if (doc == null) { LogDiag("merge: skip (no active document)"); return null; }

                LogDiag($"merge: try absStart={absStart} absEnd={absEnd} middle=\"{Preview(middleSource)}\"");

                Word.OMath leftOMath = null;
                string leftSource = null;
                string leftHandle = null;
                Word.OMath rightOMath = null;
                string rightSource = null;
                string rightHandle = null;

                // GAUCHE : on accepte 0 ou 1 espace simple entre absStart et
                // l'OMath gauche. Spec utilisateur : direct adjacence ou un
                // espace, sinon pas de merge (pas de tab, pas d'espaces multiples).
                int leftScan = absStart - 1;
                bool leftHadSpace = false;
                if (leftScan >= 0 && IsSingleSpaceAt(doc, leftScan))
                {
                    leftScan--;
                    leftHadSpace = true;
                }
                LogDiag($"merge left: scan={leftScan} hadSpace={leftHadSpace} (looking for OMath ending at {leftScan + 1})");
                // À leftScan il doit y avoir un OMath qui finit à leftScan+1.
                if (leftScan >= 0)
                {
                    int omathCount = 0;
                    foreach (Word.OMath om in doc.OMaths)
                    {
                        omathCount++;
                        var omEnd = om.Range.End;
                        var omStart = om.Range.Start;
                        if (omEnd == leftScan + 1)
                        {
                            LogDiag($"merge left: candidate OMath range=[{omStart},{omEnd}]");
                            var h = FindOurHandleForOMath(om);
                            LogDiag($"merge left: handle={(h ?? "null")}");
                            if (h != null)
                            {
                                try
                                {
                                    var stored = _store.RetrieveAsync(new EquationHandle(h)).GetAwaiter().GetResult();
                                    if (stored != null && !string.IsNullOrEmpty(stored.Source))
                                    {
                                        leftOMath = om;
                                        leftHandle = h;
                                        leftSource = stored.Source;
                                        LogDiag($"merge left: source=\"{Preview(stored.Source)}\"");
                                    }
                                    else { LogDiag("merge left: stored null or empty source"); }
                                }
                                catch (Exception ex) { LogDiag($"merge_retrieve_left_error: {ex.Message}"); }
                            }
                            break;
                        }
                    }
                    LogDiag($"merge left: scanned {omathCount} OMaths total, leftOMath={(leftOMath != null ? "found" : "null")}");
                }
                else { LogDiag("merge left: leftScan < 0, skip"); }

                // DROITE : 0 ou 1 espace entre absEnd et l'OMath droit (idem gauche).
                int rightScan = absEnd;
                int docEnd = doc.Content.End;
                bool rightHadSpace = false;
                if (rightScan < docEnd && IsSingleSpaceAt(doc, rightScan))
                {
                    rightScan++;
                    rightHadSpace = true;
                }
                LogDiag($"merge right: scan={rightScan} hadSpace={rightHadSpace} docEnd={docEnd} (looking for OMath starting at {rightScan})");
                if (rightScan < docEnd)
                {
                    foreach (Word.OMath om in doc.OMaths)
                    {
                        if (om.Range.Start == rightScan)
                        {
                            var omEnd = om.Range.End;
                            LogDiag($"merge right: candidate OMath range=[{rightScan},{omEnd}]");
                            var h = FindOurHandleForOMath(om);
                            LogDiag($"merge right: handle={(h ?? "null")}");
                            if (h != null)
                            {
                                try
                                {
                                    var stored = _store.RetrieveAsync(new EquationHandle(h)).GetAwaiter().GetResult();
                                    if (stored != null && !string.IsNullOrEmpty(stored.Source))
                                    {
                                        rightOMath = om;
                                        rightHandle = h;
                                        rightSource = stored.Source;
                                        LogDiag($"merge right: source=\"{Preview(stored.Source)}\"");
                                    }
                                    else { LogDiag("merge right: stored null or empty source"); }
                                }
                                catch (Exception ex) { LogDiag($"merge_retrieve_right_error: {ex.Message}"); }
                            }
                            break;
                        }
                    }
                }

                if (leftOMath == null && rightOMath == null)
                {
                    LogDiag("merge: no adjacent OMath found, skip merge");
                    return null;
                }

                // Construit le source mergé avec un espace simple comme jointure
                // (les espaces tapés par l'utilisateur sont collapsés à un seul).
                var sb = new System.Text.StringBuilder();
                if (leftSource != null) { sb.Append(leftSource); sb.Append(' '); }
                sb.Append(middleSource ?? string.Empty);
                if (rightSource != null) { sb.Append(' '); sb.Append(rightSource); }

                int newStart = leftOMath != null ? leftOMath.Range.Start : absStart;
                int newEnd = rightOMath != null ? rightOMath.Range.End : absEnd;

                var removed = new List<string>();
                if (leftHandle != null) removed.Add(leftHandle);
                if (rightHandle != null) removed.Add(rightHandle);

                return new MergeResult
                {
                    AbsStart = newStart,
                    AbsEnd = newEnd,
                    MergedSource = sb.ToString(),
                    RemovedHandles = removed,
                };
            }
            catch (Exception ex)
            {
                LogDiag("try_merge_error: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Marqueurs en début de ligne courante qui déclenchent le cross-paragraphe
        /// merge align* (cf. brief 30-04 multiline-systems-equivalences §3.2).
        /// `<=>`, `=>`, `<=` (et leurs variantes ASCII multi-char) + `=` solo.
        /// `{` (cases) est Phase 2, pas en V1.
        /// </summary>
        private static readonly string[] AlignMarkers = { "<==>", "<=>", "==>", "=>", "<==", "<=", "=" };

        /// <summary>
        /// Phase 2 du pipeline cross-merge : détecte si la zone courante
        /// (ligne en cours de commit) doit fusionner avec un OMath situé sur
        /// le paragraphe immédiatement au-dessus, formant un bloc align*
        /// multi-ligne (cf. brief 30-04 multiline-systems §3.2).
        ///
        /// <para>Conditions cumulées :</para>
        /// <list type="number">
        /// <item>La source courante commence par un marqueur align (<c>=</c>,
        /// <c>&lt;=&gt;</c>, <c>=&gt;</c>, <c>&lt;=</c> et variantes).</item>
        /// <item>Le paragraphe immédiatement au-dessus contient un OMath à
        /// nous (bookmark <c>mcEq_*</c>).</item>
        /// <item>Pas de paragraphe vide entre.</item>
        /// <item>Pas de texte significatif entre la fin de l'OMath précédent
        /// et le <c>¶</c> break, ni entre le <c>¶</c> break et le début de
        /// la zone courante (que du whitespace).</item>
        /// </list>
        ///
        /// <para>Si OK, retourne un <see cref="MergeResult"/> dont le range
        /// englobe <c>[prev_OMath.Start, current_zone_end]</c> et le source
        /// mergé est <c>prev_source\ncurrent_source</c>. Le pipeline core
        /// (lattice engine) détectera le <c>\n</c> comme LineBreak et
        /// produira un LaTeX <c>\begin{align*}...\end{align*}</c>.</para>
        ///
        /// <para>Au moment de l'insertion (Phase 3 = <see cref="InsertOMathAt"/>),
        /// le <c>Range.Text="..."</c> remplace tout y compris le paragraph
        /// break entre les 2 <c>¶</c> → les 2 paragraphes sont collapsés en 1
        /// contenant l'OMath multi-ligne. Phase 4 finalise (cf.
        /// <see cref="FinalizeCrossMergeLayout"/>).</para>
        /// </summary>
        private MergeResult TryFindCrossMergeAbove(int absStart, int absEnd, string currentSource)
        {
            try
            {
                if (string.IsNullOrEmpty(currentSource)) return null;
                // Condition 1 : source courante commence par marqueur align ?
                string trimmed = currentSource.TrimStart();
                string matchedMarker = null;
                foreach (var m in AlignMarkers)
                {
                    if (trimmed.StartsWith(m, StringComparison.Ordinal))
                    {
                        // Le `=` doit être suivi d'un caractère qui n'est pas `=`
                        // (sinon `==` qui est un cas bizarre). `<=`/`<==` etc.
                        // sont déjà filtrés par l'ordre de AlignMarkers (plus
                        // longs en premier).
                        matchedMarker = m;
                        break;
                    }
                }
                if (matchedMarker == null) { LogDiag("xparMerge: no align marker at start"); return null; }
                LogDiag($"xparMerge: found marker `{matchedMarker}` in current source");

                var doc = _app.ActiveDocument;
                if (doc == null) { LogDiag("xparMerge: no doc"); return null; }

                // Trouver le paragraphe courant (celui qui contient absStart)
                var currentRange = doc.Range(absStart, absStart);
                Word.Paragraph currentPara = currentRange.Paragraphs[1];
                int currentParaStart = currentPara.Range.Start;
                if (currentParaStart >= absStart) { /* OK, the zone is in this paragraph */ }

                // Condition 4a : entre currentParaStart et absStart, que du whitespace ?
                if (absStart > currentParaStart)
                {
                    string between = doc.Range(currentParaStart, absStart).Text ?? "";
                    if (!string.IsNullOrEmpty(between) && between.Trim().Length > 0)
                    {
                        LogDiag($"xparMerge: text before zone in current ¶ = \"{Preview(between)}\", abort");
                        return null;
                    }
                }

                // Trouver le paragraphe précédent
                if (currentParaStart <= 0) { LogDiag("xparMerge: at doc start, no previous ¶"); return null; }
                int prevParaEnd = currentParaStart;          // = position du ¶ mark de fin du précédent + 1
                Word.Paragraph prevPara;
                try { prevPara = doc.Range(prevParaEnd - 1, prevParaEnd - 1).Paragraphs[1]; }
                catch { LogDiag("xparMerge: cannot resolve previous ¶"); return null; }
                int prevParaStart = prevPara.Range.Start;
                int prevParaContentEnd = prevPara.Range.End - 1; // exclut le ¶ mark
                if (prevParaContentEnd <= prevParaStart) { LogDiag("xparMerge: previous ¶ empty, barrier"); return null; }

                // Condition 3 : barrière paragraphe vide. Si prevPara est vide ou
                // que sa "fin de contenu" === ¶ mark, c'est une barrière.
                string prevText = doc.Range(prevParaStart, prevParaContentEnd).Text ?? "";
                if (string.IsNullOrWhiteSpace(prevText)) { LogDiag("xparMerge: previous ¶ whitespace-only, barrier"); return null; }

                // Condition 2 : trouver l'OMath à nous qui termine le paragraphe précédent
                Word.OMath prevOMath = null;
                string prevHandle = null;
                string prevSource = null;
                foreach (Word.OMath om in doc.OMaths)
                {
                    var rng = om.Range;
                    // OMath dans le ¶ précédent
                    if (rng.Start < prevParaStart || rng.End > prevParaContentEnd) continue;
                    // On veut celui qui termine le ¶ (= rien après lui sauf whitespace)
                    if (rng.End < prevParaContentEnd)
                    {
                        // Vérifier que ce qui suit jusqu'au ¶ mark est whitespace only
                        string afterOMath = doc.Range(rng.End, prevParaContentEnd).Text ?? "";
                        if (afterOMath.Trim().Length > 0) continue; // pas le dernier OMath utile
                    }
                    var h = FindOurHandleForOMath(om);
                    if (h == null) continue;
                    try
                    {
                        var stored = _store.RetrieveAsync(new EquationHandle(h)).GetAwaiter().GetResult();
                        if (stored != null && !string.IsNullOrEmpty(stored.Source))
                        {
                            prevOMath = om;
                            prevHandle = h;
                            prevSource = stored.Source;
                        }
                    }
                    catch (Exception ex) { LogDiag($"xparMerge: retrieve_error: {ex.Message}"); }
                }
                if (prevOMath == null) { LogDiag("xparMerge: no OMath at end of previous ¶"); return null; }

                LogDiag($"xparMerge: prev OMath range=[{prevOMath.Range.Start},{prevOMath.Range.End}] source=\"{Preview(prevSource)}\"");

                // Construire le source mergé : prev + \n + current.
                // Le \n source sera tokenisé comme LineBreak par le Lexer et
                // le Parser détectera le pattern MultiLineBlock align*.
                string mergedSource = prevSource + "\n" + currentSource;

                // Range englobant : [prev OMath start, current zone end].
                // Tout ce qui est entre (¶ mark inclus) sera remplacé par
                // l'OMath multi-ligne lors de InsertOMathAt.
                int newAbsStart = prevOMath.Range.Start;
                int newAbsEnd = absEnd;

                return new MergeResult
                {
                    AbsStart = newAbsStart,
                    AbsEnd = newAbsEnd,
                    MergedSource = mergedSource,
                    RemovedHandles = new List<string> { prevHandle },
                };
            }
            catch (Exception ex)
            {
                LogDiag("xparMerge_error: " + ex.Message);
                return null;
            }
        }

        private static string NewHandleId()
        {
            // 16 premiers hex du Guid — assez unique pour notre usage, compatible
            // avec les restrictions Word sur les noms de bookmark (alphanum + _).
            return Guid.NewGuid().ToString("N").Substring(0, 16);
        }

        /// <summary>
        /// Crée un bookmark "mcEq_{id}" couvrant [absStart, absEnd]. Si un bookmark
        /// de même nom existe déjà on l'écrase (cas improbable avec guid).
        /// </summary>
        /// <summary>
        /// Supprime tous les OMaths du document qui sont entièrement contenus
        /// dans [absStart, absEnd). Word.Range.Text = "..." refuse d'écraser
        /// un OMath et lève "The range cannot be deleted" — il faut les
        /// supprimer explicitement avant. Itère en ordre descendant pour que
        /// les suppressions n'invalident pas les positions des OMaths
        /// précédents. Retourne le nombre TOTAL de chars supprimés (= shrink
        /// du range global).
        /// </summary>
        private int DeleteOMathsInRange(int absStart, int absEnd)
        {
            int totalShrink = 0;
            try
            {
                var doc = _app.ActiveDocument;
                if (doc == null) return 0;
                var inRange = new List<Word.OMath>();
                foreach (Word.OMath om in doc.OMaths)
                {
                    var omStart = om.Range.Start;
                    var omEnd = om.Range.End;
                    if (omStart >= absStart && omEnd <= absEnd) inRange.Add(om);
                }
                inRange.Sort((a, b) => b.Range.Start.CompareTo(a.Range.Start));
                foreach (var om in inRange)
                {
                    try
                    {
                        int delLen = om.Range.End - om.Range.Start;
                        om.Range.Delete();
                        totalShrink += delLen;
                        LogDiag($"merge_pre_delete_omath: deleted len={delLen} totalShrink={totalShrink}");
                    }
                    catch (Exception ex) { LogDiag("merge_pre_delete_omath_error: " + ex.Message); }
                }
            }
            catch (Exception ex) { LogDiag("merge_pre_delete_scan_error: " + ex.Message); }
            return totalShrink;
        }

        /// <summary>
        /// Supprime le bookmark Word `mcEq_<handleId>` s'il existe. Utilisé
        /// quand un handle est retiré du store (ex: merge OMath qui fusionne
        /// l'OMath dans un nouveau bloc) — sans ce nettoyage, le bookmark
        /// fantôme reste dans le doc et `FindOurHandleForOMath` retrouve
        /// l'ancien handle, créant des merge corrompus au prochain trigger.
        /// </summary>
        private void DeleteBookmarkByHandle(string handleId)
        {
            var doc = _app.ActiveDocument;
            if (doc == null) return;
            string name = BookmarkPrefix + handleId;
            if (doc.Bookmarks.Exists(name)) doc.Bookmarks[name].Delete();
            LogDiag($"bookmark deleted: {name}");
        }

        private void CreateBookmarkForRange(string handleId, int absStart, int absEnd)
        {
            try
            {
                var doc = _app.ActiveDocument;
                string name = BookmarkPrefix + handleId;
                var range = doc.Range(absStart, absEnd);
                if (doc.Bookmarks.Exists(name)) doc.Bookmarks[name].Delete();
                doc.Bookmarks.Add(name, range);
            }
            catch (Exception ex) { LogDiag("bookmark_create_error: " + ex.Message); }
        }

        /// <summary>
        /// Remplace le range [absStart, absEnd) du document par un OMath construit
        /// à partir du LaTeX fourni. Word's BuildUp ne parse pas le LaTeX nativement,
        /// on convertit donc d'abord en UnicodeMath (le format natif qu'il comprend).
        /// Renvoie (newStart, newEnd) = bornes réelles de l'OMath inséré pour qu'on
        /// puisse accrocher un bookmark dessus.
        /// </summary>
        private (int newStart, int newEnd) InsertOMathAt(int absStart, int absEnd, string latex)
        {
            var doc = _app.ActiveDocument;
            if (doc == null) return (absStart, absEnd);
            int docStart = doc.Content.Start;
            int docEnd = doc.Content.End;
            if (absStart < docStart) absStart = docStart;
            if (absEnd > docEnd) absEnd = docEnd;
            if (absEnd <= absStart) return (absStart, absEnd);

            // Trim whitespaces aux bords de la zone détectée : le NER inclut
            // parfois un espace avant/après dans la zone, et on ne veut PAS le
            // remplacer. Sinon on colle l'OMath au mot précédent ("Soit V x" →
            // NER zone "  V x" → remplacement engloutit l'espace → "Soit∀ x").
            while (absStart < absEnd && IsWhitespaceCharAt(doc, absStart)) absStart++;
            while (absEnd > absStart && IsWhitespaceCharAt(doc, absEnd - 1)) absEnd--;
            if (absEnd <= absStart) return (absStart, absEnd);

            // Conversion LaTeX → UnicodeMath : Word's OMaths.BuildUp parse
            // l'UnicodeMath (\frac{a}{b} → (a)/(b), \sqrt{x} → √(x), etc.).
            string unicodeMath = LatexToUnicodeMath.Convert(latex);
            LogDiag($"latex→umath \"{latex}\" → \"{unicodeMath}\"");

            // SAUVEGARDE du texte original avant remplacement, pour rollback si
            // l'OMath n'est pas vraiment créé par BuildUp. Règle dure : on ne
            // doit JAMAIS laisser dans Word du texte technique (UnicodeMath ou
            // LaTeX brut) si la conversion en équation a échoué.
            string originalText;
            try { originalText = doc.Range(absStart, absEnd).Text ?? ""; }
            catch { originalText = ""; }

            // Espace trailing : seulement si le caractère suivant n'est pas déjà
            // un whitespace (sinon on se retrouve avec des doubles espaces).
            bool nextIsWs = absEnd < docEnd && IsWhitespaceCharAt(doc, absEnd);
            string insertText = nextIsWs ? unicodeMath : unicodeMath + " ";

            var replaceRange = doc.Range(absStart, absEnd);
            replaceRange.Text = insertText;

            int insertedLen = unicodeMath.Length;
            var mathRange = doc.Range(absStart, absStart + insertedLen);
            bool buildUpThrew = false;
            try
            {
                mathRange.OMaths.Add(mathRange);
                mathRange.OMaths.BuildUp();
            }
            catch (Exception ex)
            {
                LogDiag("omath_add_error: " + ex.Message);
                buildUpThrew = true;
            }

            // VÉRIFICATION : un OMath couvre-t-il vraiment notre plage ? Si non,
            // BuildUp a échoué silencieusement (il n'a pas su parser l'UnicodeMath)
            // et on a laissé le texte technique dans le doc — INADMISSIBLE.
            // Rollback vers le texte original (ce que l'utilisateur avait tapé).
            int newStart = absStart;
            int newEnd = absStart + insertedLen;
            bool omathCreated = false;
            try
            {
                foreach (Word.OMath om in doc.OMaths)
                {
                    var rng = om.Range;
                    if (rng.Start <= absStart && rng.End > absStart)
                    {
                        newStart = rng.Start;
                        newEnd = rng.End;
                        omathCreated = true;
                        break;
                    }
                }
            }
            catch { }

            if (!omathCreated)
            {
                LogDiag($"omath NOT created (buildUpThrew={buildUpThrew}) — rollback texte technique \"{insertText}\" → original \"{originalText}\"");
                try
                {
                    var fallbackRange = doc.Range(absStart, absStart + insertText.Length);
                    fallbackRange.Text = originalText;
                    // Repositionne le caret à la fin de la zone restaurée
                    int restoredEnd = absStart + originalText.Length;
                    try { _app.Selection.SetRange(restoredEnd, restoredEnd); } catch { }
                }
                catch (Exception ex) { LogDiag("rollback_error: " + ex.Message); }
                // On signale au caller que rien n'a été inséré : zone vide.
                return (absStart, absStart);
            }

            // On aligne l'OMath sur l'alignement du paragraphe texte — par défaut
            // Word centre les équations (wdOMathJcCenterGroup), ce qui ne respecte
            // pas le choix utilisateur. On touche uniquement OMath.Justification et
            // OMathPara.Justification, jamais le paragraphe texte.
            SyncOMathJustificationToParagraph(doc, absStart, absStart + insertedLen);

            // Positionne le curseur juste après l'OMath, puis vérifie qu'on n'est
            // PAS resté dans l'éditeur math (Word interprète parfois "pile après"
            // comme "encore dedans", surtout en display-mode). Nudge jusqu'à 3 fois
            // pour sortir proprement sur une zone de texte libre.
            int afterPos = Math.Min(newEnd + 1, doc.Content.End);
            try { _app.Selection.SetRange(afterPos, afterPos); } catch { }
            NudgeCursorOutOfMath(doc, maxAttempts: 3);
            return (newStart, newEnd);
        }

        /// <summary>
        /// Force la sortie de l'éditeur OMath après une insertion. Word a tendance
        /// à garder le caret "en mode math" quand il est positionné pile à la fin
        /// d'une équation — il faut plusieurs leviers pour sortir proprement :
        ///  1. SetRange(omEnd + 1) : suffit parfois pour inline simple.
        ///  2. Selection.EndKey(wdLine) : pousse jusqu'à la fin de ligne, ce qui
        ///     sort systématiquement d'un OMath display-mode (la ligne suivante
        ///     est en texte libre).
        ///  3. Répétition jusqu'à maxAttempts (caret toujours dans un OMath → on
        ///     re-tente en augmentant la position).
        /// </summary>
        private void NudgeCursorOutOfMath(Word.Document doc, int maxAttempts)
        {
            for (int i = 0; i < maxAttempts; i++)
            {
                try
                {
                    var sel = _app.Selection;
                    if (sel.OMaths == null || sel.OMaths.Count == 0) return;

                    // Niveau 1 : SetRange juste après la fin de l'OMath courant
                    int omEnd = sel.OMaths[1].Range.End;
                    int target = Math.Min(omEnd + 1, doc.Content.End);
                    if (target > sel.Start) _app.Selection.SetRange(target, target);

                    // Niveau 2 : si toujours dans un OMath, EndKey(wdLine) pour
                    // sortir jusqu'à la fin de la ligne courante (late-bind :
                    // wdLine = 5, wdMove = 0 dans toutes les versions Word).
                    if (_app.Selection.OMaths != null && _app.Selection.OMaths.Count > 0)
                    {
                        try
                        {
                            _app.Selection.GetType().InvokeMember(
                                "EndKey",
                                System.Reflection.BindingFlags.InvokeMethod,
                                null, _app.Selection,
                                new object[] { 5, 0 });
                        }
                        catch { }
                    }
                }
                catch { return; }
            }
        }

        private (double x, double y) GetCaretScreenPosition()
        {
            // GetGUIThreadInfo renvoie atomiquement hwndCaret (fenêtre qui
            // possède le caret) et rcCaret (rect du caret dans ce référentiel).
            // On convertit ensuite avec hwndCaret, pas GetFocus() : dès qu'un
            // OMath existe dans le doc, Word multiplie les sous-fenêtres
            // (éditeur math, pane texte) et les deux HWND peuvent diverger,
            // ce qui décalait la popup.
            try
            {
                var gti = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf(typeof(GUITHREADINFO)) };
                if (!GetGUIThreadInfo(0, ref gti) || gti.hwndCaret == IntPtr.Zero)
                {
                    return (200, 200);
                }
                var pt = new POINT { X = gti.rcCaret.Left, Y = gti.rcCaret.Bottom };
                ClientToScreen(gti.hwndCaret, ref pt);
                double scale = GetDpiScale();
                return (pt.X / scale, pt.Y / scale + 4);
            }
            catch
            {
                return (200, 200);
            }
        }

        private static double GetDpiScale()
        {
            try
            {
                using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                {
                    return g.DpiX / 96.0;
                }
            }
            catch { return 1.0; }
        }

        private static string Preview(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
            return s.Length > 120 ? s.Substring(0, 120) + "…" : s;
        }

        private static void LogDiag(string message)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MathCursor", "logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "mathcursor.log"),
                    $"{DateTime.UtcNow:o} ner {message}{Environment.NewLine}");
            }
            catch { }
        }

        // --- Win32 ---
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct GUITHREADINFO
        {
            public uint cbSize;
            public uint flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public RECT rcCaret;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
    }
}
