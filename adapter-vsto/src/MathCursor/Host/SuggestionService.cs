using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Threading;
using MathCursor.Core;
using MathCursor.Core.Lattice;
using MathCursor.Core.Symbols;
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

        private void OnSelectionChange(Word.Selection sel) => CheckAndUpdate();

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
                    IReadOnlyList<DetectedZone> zones;
                    try { zones = _ner.Detect(paragraphText); }
                    catch (Exception ex) { LogDiag("ner_error: " + ex.Message); zones = Array.Empty<DetectedZone>(); }

                    // Filtre : on jette les zones NER qui chevauchent une région OMath.
                    // Ces zones sont déjà converties — les re-proposer serait redondant
                    // (et piégeux : on insèrerait un 2e OMath par-dessus).
                    var filteredZones = FilterOutOMathOverlap(zones, omathRegions);
                    LogDiag($"zones={zones.Count} → filtered={filteredZones.Count} (omath_overlap dropped={zones.Count - filteredZones.Count})");

                    // Retour sur le thread UI pour mettre à jour la popup
                    _pollTimer?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { ApplyZones(filteredZones, caretInParagraph, paragraphAbsStart); }
                        finally { _inferenceInFlight = false; }
                    }));
                });
            }
            catch
            {
                _inferenceInFlight = false;
            }
        }

        private void ApplyZones(IReadOnlyList<DetectedZone> zones, int caretInParagraph, int paragraphAbsStart)
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
                range.Text = source;

                // Caret en fin du texte inséré
                int newEnd = omStart + source.Length;
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
        private static readonly char[] ManualTriggerDelimiters =
            { '.', ',', ';', ':', '!', '?', '=', '<', '>', '\n', '\r' };

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
        private void OnReportRequested()
        {
            try
            {
                var report = BuildFeedbackReport();
                var sender = Feedback.FeedbackSenderFactory.Create();
                var dialog = new FeedbackDialog(report, sender);
                // ShowDialog = modal vis-à-vis de Word (focus bloqué tant qu'ouvert),
                // choix acté dans decisions.md.
                dialog.ShowDialog();
            }
            catch (Exception ex) { LogDiag("feedback_dialog_error: " + ex.Message); }
        }

        /// <summary>
        /// Construit un <see cref="Feedback.FeedbackReport"/> pré-rempli à partir
        /// de l'état courant : source NER / span manuel, formule sélectionnée,
        /// version add-in, version Word, OS, et tail du log.
        /// </summary>
        private Feedback.FeedbackReport BuildFeedbackReport()
        {
            string recognized = "";
            try { recognized = _popup?.CurrentFinalLatex ?? ""; }
            catch { }

            string wordVersion = "?";
            try { wordVersion = _app?.Version ?? "?"; } catch { }

            string version = "?";
            try { version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?"; } catch { }

            return new Feedback.FeedbackReport
            {
                Version = version,
                Timestamp = DateTimeOffset.UtcNow,
                UserId = Feedback.UserIdStore.GetOrCreate(),
                SessionId = _sessionId,
                NerText = _lastZoneSource ?? "",
                RecognizedFormula = recognized,
                LogTail = ReadLogTail(),
                WordVersion = wordVersion,
                OsVersion = Environment.OSVersion.ToString(),
            };
        }

        private static string ReadLogTail()
        {
            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MathCursor", "logs", "mathcursor.log");
                if (!File.Exists(path)) return "";
                const int maxBytes = 16 * 1024; // 16 KB
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    byte[] buf;
                    if (fs.Length <= maxBytes)
                    {
                        buf = new byte[fs.Length];
                        fs.Read(buf, 0, buf.Length);
                    }
                    else
                    {
                        fs.Seek(-maxBytes, SeekOrigin.End);
                        buf = new byte[maxBytes];
                        fs.Read(buf, 0, buf.Length);
                    }
                    return System.Text.Encoding.UTF8.GetString(buf);
                }
            }
            catch { return ""; }
        }

        private void ShowPopup(ResolvedZone resolved, int absStart, int absEnd, int rawZoneLength, string debugText = "")
        {
            if (_popup == null)
            {
                _popup = new SuggestionPopupWindow();
                _popup.ReportRequested += OnReportRequested;
                _popup.SourceMutationRequested += OnSourceMutationRequested;
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

                // Auto-commit : si la mutation rend la formule complète
                // (pas de Hole, pas d'opérateur final), on insère l'OMath
                // direct sans attendre flèche bas + Enter. Cas type : `V x R`
                // → `\forall x \in R` n'a rien d'autre à recevoir, l'utilisateur
                // a fini sa désambig. Pour les alts identity ou sub LaTeX
                // (vec AB), on ne passe jamais par ici (mutation null), donc
                // le flow popup → final → Enter reste pour ces cas.
                if (!resolved.IsIncomplete)
                {
                    LogDiag($"auto-commit on alt resolution latex=\"{resolved.TopLatex}\"");
                    CommitLatexAndOMath(resolved.TopLatex, src);
                    return;
                }

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
            var editing = _editHandle;
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

        /// <summary>
        /// Aligne l'OMath sur l'alignement du paragraphe texte qui le contient.
        /// Word centre par défaut les équations via OMath.Justification et
        /// OMathPara.Justification (wdOMathJcCenterGroup). On récupère l'alignement
        /// paragraphe (Left/Center/Right/Justify), on le mappe vers la valeur
        /// WdOMathJc correspondante, et on l'applique :
        ///   1. à l'OMath lui-même (r.Start &lt;= pos &lt; r.End)
        ///   2. à l'OMathPara parent (via doc.Content.OMathParagraphs) si présent
        /// Ne TOUCHE PAS au paragraphe texte : on respecte le choix utilisateur.
        /// </summary>
        private void SyncOMathJustificationToParagraph(Word.Document doc, int pos, int spanEnd)
        {
            try
            {
                // 1) Lit l'alignment du paragraphe contenant la position
                int paraAlign = 0; // wdAlignParagraphLeft par défaut
                try
                {
                    var para = doc.Range(pos, pos).Paragraphs[1];
                    paraAlign = (int)para.Format.GetType().InvokeMember(
                        "Alignment",
                        System.Reflection.BindingFlags.GetProperty,
                        null, para.Format, null);
                }
                catch (Exception ex) { LogDiag("para_align_read_error: " + ex.Message); }

                int omathJc = MapParagraphAlignToOMathJc(paraAlign);
                LogDiag($"align_sync paraAlign={paraAlign} → omathJc={omathJc}");

                // 2) Applique sur l'OMath couvrant pos
                try
                {
                    foreach (Word.OMath om in doc.OMaths)
                    {
                        var r = om.Range;
                        if (r.Start > pos || r.End <= pos) continue;
                        try
                        {
                            om.GetType().InvokeMember(
                                "Justification",
                                System.Reflection.BindingFlags.SetProperty,
                                null, om, new object[] { omathJc });
                        }
                        // API non dispo dans certaines PIA Word — silencieux,
                        // l'OMath garde son alignement par défaut. Pas un bug
                        // côté nous.
                        catch { }
                        break;
                    }
                }
                catch (Exception ex) { LogDiag("omath_scan_error: " + ex.Message); }

                // 3) Applique sur l'OMathPara parent (seulement si display-mode).
                //    OMathParagraphs vit sur Range, pas sur Document (DISP_E_UNKNOWNNAME
                //    direct sur Document). Itération indexée Count+Item : l'IEnumerable
                //    des collections COM Word rate parfois les derniers éléments ajoutés.
                try
                {
                    var contentRange = doc.Content;
                    if (contentRange == null) return;
                    object omathParas = contentRange.GetType().InvokeMember(
                        "OMathParagraphs",
                        System.Reflection.BindingFlags.GetProperty,
                        null, contentRange, null);
                    if (omathParas == null) return;

                    var parasType = omathParas.GetType();
                    int count = (int)parasType.InvokeMember(
                        "Count",
                        System.Reflection.BindingFlags.GetProperty,
                        null, omathParas, null);
                    for (int i = 1; i <= count; i++)
                    {
                        try
                        {
                            object omp = parasType.InvokeMember(
                                "Item",
                                System.Reflection.BindingFlags.InvokeMethod | System.Reflection.BindingFlags.GetProperty,
                                null, omathParas, new object[] { i });
                            if (omp == null) continue;
                            var ompType = omp.GetType();
                            object range = ompType.InvokeMember(
                                "Range",
                                System.Reflection.BindingFlags.GetProperty,
                                null, omp, null);
                            if (range == null) continue;
                            var rangeType = range.GetType();
                            int rStart = (int)rangeType.InvokeMember("Start", System.Reflection.BindingFlags.GetProperty, null, range, null);
                            int rEnd = (int)rangeType.InvokeMember("End", System.Reflection.BindingFlags.GetProperty, null, range, null);
                            if (rEnd < pos || rStart > spanEnd) continue;

                            ompType.InvokeMember(
                                "Justification",
                                System.Reflection.BindingFlags.SetProperty,
                                null, omp, new object[] { omathJc });
                        }
                        catch { } // API absente sur cette PIA — silencieux
                    }
                }
                catch { } // OMathParagraphs pas exposé sur cette PIA — silencieux
            }
            catch (Exception ex) { LogDiag("align_sync_error: " + ex.Message); }
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

        private static bool IsWhitespaceCharAt(Word.Document doc, int pos)
        {
            try
            {
                var t = doc.Range(pos, pos + 1).Text ?? "";
                return t.Length > 0 && char.IsWhiteSpace(t[0]);
            }
            catch { return false; }
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
