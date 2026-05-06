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
using MathCursor.Host.Merging;
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
        private readonly LastActionTracker _lastActionTracker;

        // Sidecars de résolutions par handle d'OMath (Phase 1.5 ADR 06-05).
        // Mémoire seulement (pas persisté store côté Phase 3). Permet au
        // cross-merge de retrouver les choix vec/paren/etc. faits sur les
        // OMaths absorbés du dessus, et de les fusionner pour produire un
        // LaTeX align* qui les préserve.
        private readonly EquationHandleRegistry _handleRegistry;

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

        // Zone reverted d'un OMath multi-ligne (cf. ADR 04-05 multiline-edit-cascade).
        // Set par OnRevertRequested quand source contient `\n`. Lu par
        // TryFindCrossMergeAbove pour activer le Mode 2 : cascade absorbe
        // tous les paragraphes de la zone (y compris ligne 1 sans marker).
        // Reset sur : commit succès, caret hors zone, edit-mode annulé.
        // -1 = inactif.
        private int _revertedMultiLineZoneStart = -1;
        private int _revertedMultiLineZoneEnd = -1;

        // Mode liste invisible (cf. ADR 05-05 multiline-list-mode). Activé
        // après un cross-merge multi-ligne réussi. Quand l'user appuie sur
        // Enter sur une nouvelle ligne sans marker, on préfixe silencieusement
        // sa source par le marker actif avant de la passer au pipeline cross-merge.
        // Logique pure dans ListModeStateMachine, testée séparément.
        // ListModeController encapsule la state machine ListModeStateMachine
        // + l'ancre paragraphe (anchorParaStart). Cf. ADR 06-05 Phase 4c L4.
        private readonly ListModeController _listMode = new ListModeController();

        // Flag : la dernière InsertOMathAt a-t-elle utilisé le pattern XML
        // transplant ? Si oui, l'alignement (m:jc) a déjà été pré-patché dans
        // le XML capturé avant l'unique InsertXML — pas besoin d'un 2e
        // InsertXML via PatchOMathParaJustificationViaXml en finalize, qui
        // causerait une fusion avec l'OMath voisin (cf. bug user 04-05).
        private bool _lastInsertUsedXmlTransplant;

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
            // CANARY LOG (Phase 4 + bug fixes 06-05) : log distinctif au
            // démarrage pour confirmer que la DLL chargée est la version
            // courante. Si tu vois ce log dans mathcursor.log, mes derniers
            // fix (splice position-aware, semicolon `;`, suppression
            // consumed[i] dans flip) sont actifs.
            LogDiag("[CANARY 2026-05-06 v3] SuggestionService ctor — splice+semicolon+flip-consumed-removed");

            _app = app ?? throw new ArgumentNullException(nameof(app));
            _ner = ner ?? throw new ArgumentNullException(nameof(ner));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _resolver = new ZoneResolver(_engine);
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _contextReader = new WordContextReader(_app);
            _lastActionTracker = new LastActionTracker(ReadParagraphContextForReport);
            _handleRegistry = new EquationHandleRegistry(
                createBookmark: CreateBookmarkForRange,
                deleteBookmark: DeleteBookmarkByHandle,
                popupSidecar: () => _popup?.CurrentSidecar
                                    ?? MathCursor.Core.Resolution.ResolutionSidecar.Empty);

            // Pipeline de mergers (cf. ADR 2026-05-06-Meta-zone-merger-pipeline) :
            // remplace l'empilement de `if (merged == null)` qui vivait dans
            // OnPopupCommitRequested. Ordre = priorité (intra avant cross,
            // reverted avant cases avant marker). Chaque merger est self-guarding :
            // il retourne null si non-applicable au commit courant.
            _mergerPipeline = new MergerPipeline(new IZoneMerger[]
            {
                new IntraOMathsMerger(TryMergeWithAdjacentOMaths),
                new RevertedMultiLineMerger((s, e, src) =>
                {
                    var doc = _app.ActiveDocument;
                    return doc == null ? null : TryAbsorbRevertedMultiLineZone(doc, s, e, src);
                }),
                new CasesChainCascadeMerger((s, e, src) =>
                {
                    var doc = _app.ActiveDocument;
                    return doc == null ? null : TryCascadeAbsorbCasesChain(doc, s, e, src);
                }),
                new MarkerChainCascadeMerger((s, e, src) =>
                {
                    var doc = _app.ActiveDocument;
                    return doc == null ? null : TryCascadeAbsorbMarkerChain(doc, s, e, src);
                }),
            }, log: LogDiag);

            // Pipeline du commit (cf. ADR 2026-05-06-Meta-l4-pipeline-and-session,
            // Phase 3a). Phase 3a livre seulement les 2 stages réels (Merger,
            // Resolver) ; les 5 autres stages (Renderer/Inserter/Store/Layout/
            // Caret/Snapshot) sont posés en code (Phase 2.5) mais non branchés
            // ici — ils s'intégreront en Phase 3b/4 avec l'extraction effective
            // de la logique métier.
            _commitPipeline = new MathCursor.Host.Pipeline.CommitPipeline(
                new MathCursor.Host.Pipeline.ICommitStage[]
                {
                    new MathCursor.Host.Pipeline.Stages.MergerStage(
                        _mergerPipeline, ExtractMarkerFromMergedSource),
                    new MathCursor.Host.Pipeline.Stages.ResolverStage(_resolver),
                    new MathCursor.Host.Pipeline.Stages.SnapshotStage(_lastActionTracker),
                    new MathCursor.Host.Pipeline.Stages.InserterStage(InserterImpl),
                    new MathCursor.Host.Pipeline.Stages.StoreStage(_store, _handleRegistry, LogDiag),
                    new MathCursor.Host.Pipeline.Stages.LayoutStage(LayoutImpl),
                },
                log: LogDiag);
        }

        private readonly MergerPipeline _mergerPipeline;
        private readonly MathCursor.Host.Pipeline.CommitPipeline _commitPipeline;

        /// <summary>
        /// Retourne le snapshot de la dernière action (popup + commit éventuel)
        /// pour pré-remplir la fenêtre "Signaler une erreur". Renvoie null si
        /// aucune action depuis le démarrage de Word.
        /// </summary>
        public LastActionSnapshot GetLastAction() => _lastActionTracker.Current;

        /// <summary>
        /// Wrapper rétro-compat vers <c>_handleRegistry.GetSidecar</c>. Cf.
        /// ADR 2026-05-06 sidecar-and-layers + Phase 4b L4 extraction.
        /// </summary>
        internal MathCursor.Core.Resolution.ResolutionSidecar GetSidecarForHandle(string handleId)
            => _handleRegistry.GetSidecar(handleId);

        /// <summary>
        /// Wrapper rétro-compat vers <c>_handleRegistry.Stash</c>. Cf. ADR
        /// 2026-05-06 sidecar-and-layers + Phase 4b L4 extraction.
        /// </summary>
        private void StashSidecarForHandle(
            string handleId,
            MathCursor.Core.Resolution.ResolutionSidecar overrideSidecar = null)
            => _handleRegistry.Stash(handleId, overrideSidecar);

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
                // Mode 2 cascade (ADR 04-05) : invalide la zone reverted si
                // l'utilisateur a quitté la zone (clic ailleurs, scroll).
                try { InvalidateRevertedMultiLineZoneIfCaretLeft(sel?.Start ?? -1); } catch { }
                // List-mode (ADR 05-05) : si le caret quitte le ¶ d'ancrage,
                // désactive le mode liste invisible. Tant qu'on tape dans le
                // même ¶, le start ne change pas → le mode reste actif.
                try { InvalidateListModeIfCaretLeftAnchor(sel); } catch { }
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

            // Phase 3 sidecar : si l'équation stockée a un sidecar persisté,
            // on le ré-injecte dans la mémoire pour que le cross-merge ou
            // l'edit OMath re-l'utilise. Sinon (commit pré-Phase 3 ou 100%
            // default) → mémoire reste vierge, default sera appliqué.
            if (!string.IsNullOrEmpty(stored.Metadata?.SidecarJson))
            {
                var sc = MathCursor.Core.Resolution.SidecarSerializer.Deserialize(
                    stored.Metadata!.SidecarJson);
                _handleRegistry.Restore(handle.Id, sc);
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
                // l'enveloppe math autour du nouveau texte).
                try { om.Range.Delete(); } catch { }

                // ⚠ Après Delete(), positions du doc shiftées : omEnd est stale.
                // Utiliser [omStart, omEnd] comme range pour Text = ... ferait
                // ÉCRASER le contenu qui suivait l'OMath. → On insère via range
                // collapsé à omStart : pure insertion, pas de remplacement.
                // Le source brut peut contenir \n (séparateurs de lignes d'un
                // MultiLineBlock align*, cf. brief 30-04). On convertit chaque
                // \n en paragraph mark Word (\r) pour recréer la structure
                // multi-paragraphe d'origine.
                string revertText = source.Replace("\n", "\r");
                doc.Range(omStart, omStart).Text = revertText;

                // Caret en fin du texte inséré
                int newEnd = omStart + revertText.Length;
                try { _app.Selection.SetRange(newEnd, newEnd); } catch { }

                // Mode 2 cascade (ADR 04-05) : si source était multi-ligne, on
                // mémorise la zone pour que TryFindCrossMergeAbove absorbe
                // TOUS les paragraphes de la zone au prochain commit, y
                // compris la première ligne qui n'a pas de marker.
                if (source.IndexOf('\n') >= 0)
                {
                    _revertedMultiLineZoneStart = omStart;
                    _revertedMultiLineZoneEnd = newEnd;
                    LogDiag($"revert: multi-ligne zone tracked [{omStart},{newEnd}]");
                }
                else
                {
                    _revertedMultiLineZoneStart = -1;
                    _revertedMultiLineZoneEnd = -1;
                }

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

            var snap = _lastActionTracker.Current;
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
            _lastActionTracker.RecordPopupOpen(_lastZoneSource, resolved?.TopLatex);

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
        /// <summary>
        /// Mode visible (ADR 05-05 visible) : si le list-mode est actif, traite
        /// l'Enter selon l'action calculée par <see cref="ListModeStateMachine"/> :
        /// <list type="bullet">
        /// <item><b>ExitListMode</b> (ligne vide ou marker-only) → strip le
        /// marker visible si présent, consume Enter (caret reste sur ¶ vide).
        /// Comportement Word bullet list.</item>
        /// <item><b>ValidateAsIs</b> (marker + contenu) → trigger conversion,
        /// cross-merge absorbe dans le bloc, re-injecte marker sur le ¶ suivant.</item>
        /// <item><b>PrefixWithActiveMarker</b> (contenu sans marker = user a
        /// backspacé le marker) → exit silencieux, on laisse Enter passer.</item>
        /// <item><b>Passthrough</b> → no-op.</item>
        /// </list>
        /// Retourne true si l'Enter a été consommé.
        /// </summary>
        public bool TryHandleListModeEnter()
        {
            if (_listMode.ActiveMarker == null) return false;
            try
            {
                var paragraph = _contextReader.ReadCurrentParagraph();
                string lineText = paragraph.Text ?? string.Empty;
                // Strip ¶-mark trailing chars (\r, \v, \n) — la state machine
                // veut le contenu pur, pas les marqueurs de fin de ¶ Word.
                string lineForDecision = lineText.TrimEnd('\r', '\n', '\v');

                var action = _listMode.OnEnterPressed(lineForDecision);
                LogDiag($"list_mode_enter: line=\"{Preview(lineForDecision)}\" → {action}");

                switch (action)
                {
                    case EnterAction.Passthrough:
                        return false;

                    case EnterAction.ExitListMode:
                        // Strip le marker visible auto-injecté (si présent)
                        // pour que le ¶ devienne vraiment vide. Ensuite consume
                        // l'Enter : caret reste sur le ¶ désormais vide.
                        StripListModeMarkerFromCurrentLine();
                        _listMode.Reset();
                        return true;

                    case EnterAction.ValidateAsIs:
                        return CommitCurrentLineForListMode();

                    case EnterAction.PrefixWithActiveMarker:
                        // User a backspacé notre injection puis tapé du contenu.
                        // Exit silencieux : on laisse Enter créer un ¶ normal.
                        _listMode.Reset();
                        return false;
                }
            }
            catch (Exception ex)
            {
                LogDiag("list_mode_enter_error: " + ex.Message);
            }
            return false;
        }

        /// <summary>
        /// Supprime le marker auto-injecté du ¶ courant (= remplace le contenu
        /// du ¶ par chaîne vide). Appelé sur ExitListMode quand l'user fait
        /// Enter sur une ligne marker-only.
        /// </summary>
        private void StripListModeMarkerFromCurrentLine()
        {
            try
            {
                var doc = _app.ActiveDocument;
                if (doc == null) return;
                var sel = _app.Selection;
                if (sel == null) return;
                var paraRange = sel.Paragraphs[1].Range;
                int contentStart = paraRange.Start;
                int contentEnd = Math.Max(contentStart, paraRange.End - 1);
                if (contentEnd <= contentStart) return;
                var stripRange = doc.Range(contentStart, contentEnd);
                stripRange.Text = string.Empty;
                doc.Range(contentStart, contentStart).Select();
                LogDiag($"list_mode: stripped marker from ¶[{contentStart},{contentEnd}]");
            }
            catch (Exception ex) { LogDiag("list_mode_strip_error: " + ex.Message); }
        }

        /// <summary>
        /// Pour le list-mode : place le caret en fin de ¶ courant, déclenche
        /// la conversion manuelle (TriggerManual remontera depuis la fin de
        /// ligne jusqu'au début car aucun délim/OMath sur cette ligne fraîche),
        /// puis commit immédiatement le candidat sélectionné. La cascade
        /// cross-merge (Mode 1 marker chain) absorbera la ligne dans le bloc
        /// multi-ligne au-dessus, puis re-injectera un marker sur le ¶ suivant.
        /// </summary>
        private bool CommitCurrentLineForListMode()
        {
            try
            {
                var doc = _app.ActiveDocument;
                if (doc == null) return false;
                var sel = _app.Selection;
                if (sel == null) return false;

                // Place le caret en fin du contenu du ¶ (avant le \r mark).
                var paraRange = sel.Paragraphs[1].Range;
                int contentEnd = Math.Max(paraRange.Start, paraRange.End - 1);
                doc.Range(contentEnd, contentEnd).Select();

                TriggerManual();
                if (IsPopupVisible)
                {
                    return CommitSelected();
                }
                LogDiag("list_mode_enter: TriggerManual ne montre pas de popup, abort");
                return false;
            }
            catch (Exception ex)
            {
                LogDiag("list_mode_commit_error: " + ex.Message);
                return false;
            }
        }

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
            // Pipeline du commit (Phase 3b — ADR 2026-05-06-Meta-l4-pipeline-and-session).
            // 6 stages composés : Merger → Resolver → Snapshot → Inserter →
            // Store → Layout. Les délégués pointent vers des méthodes privées
            // de SuggestionService qui contiennent la logique métier (Inserter
            // = OOXML/transplant, Store = bookmark+sidecar, Layout = align +
            // list-mode). À nettoyer en Phase 4 quand la logique sera vraiment
            // extraite dans les classes des stages.
            //
            // En mode édition, MergerStage skip. ResolverStage skip si
            // !WasMerged && Sidecar.IsEmpty. InserterStage peut signaler
            // IsAborted (rollback Word) → les stages suivants pass-through.
            //
            // Pre-load sidecar en edit mode (fix canary 4) : sinon le revert
            // d'un OMath multi-ligne avec vec perd ses désambiguïsations.
            // Merge stored + popup pour que anciens pins ET nouveaux choix
            // popup soient combinés (last-write-wins via ZoneResolver pour
            // les pins divergents sur le même span).
            var initialSidecar = MathCursor.Core.Resolution.ResolutionSidecar.Empty;
            if (_editHandle != null)
            {
                initialSidecar = MathCursor.Core.Resolution.SidecarMerger.Merge(
                    new[]
                    {
                        GetSidecarForHandle(_editHandle.Id),
                        _popup?.CurrentSidecar ?? MathCursor.Core.Resolution.ResolutionSidecar.Empty,
                    },
                    new[] { 0, 0 });
            }
            var ctx = new MathCursor.Host.Pipeline.CommitContext(
                absStart: _lastZoneAbsStart,
                absEnd: _lastZoneAbsEnd,
                source: source,
                latex: latex,
                sidecar: initialSidecar,
                editingHandle: _editHandle);
            try
            {
                ctx = _commitPipeline.Run(ctx);
            }
            catch (Exception ex) { LogDiag("commit_pipeline_error: " + ex.Message); }

            // Reset état
            _lastZoneAbsStart = -1;
            _lastZoneAbsEnd = -1;
            _lastZoneSource = "";
            _editHandle = null;
            _editingOMathStart = -1;
            _revertedMultiLineZoneStart = -1;
            _revertedMultiLineZoneEnd = -1;
            _lastCommitUtc = DateTime.UtcNow;
            HidePopup();
            return true;
        }

        // ─── Implémentations des stages du pipeline (Phase 3b) ──────────
        // Ces méthodes sont des délégués pour les InserterStage / StoreStage /
        // LayoutStage / SnapshotStage du CommitPipeline. La logique métier
        // (OOXML, bookmarks, list-mode) reste dans SuggestionService pour
        // limiter le diff de cette phase. Phase 4 fera la vraie extraction
        // dans les classes des stages.

        /// <summary>InserterStage : cleanup post-merge (handles absorbés +
        /// DeleteOMathsInRange) puis insertion OMath via <c>InsertOMathAt</c>.
        /// Si l'insertion échoue, retourne <c>ctx.WithAbort()</c> pour que les
        /// stages suivants pass-through. Sinon, mémorise les nouvelles bornes
        /// + ReplaceStart pour LayoutStage.</summary>
        private MathCursor.Host.Pipeline.CommitContext InserterImpl(
            MathCursor.Host.Pipeline.CommitContext ctx)
        {
            // Cleanup post-merge : doit être fait AVANT InsertOMathAt
            // (sinon Word refuse d'écraser un OMath via Range.Text).
            if (ctx.RemovedHandles != null && ctx.RemovedHandles.Count > 0)
            {
                foreach (var h in ctx.RemovedHandles)
                {
                    try { _store.RemoveAsync(new EquationHandle(h)).GetAwaiter().GetResult(); }
                    catch (Exception ex) { LogDiag($"merge_remove_error handle={h}: {ex.Message}"); }
                    _handleRegistry.Forget(h);
                }

                int rangeShrink = DeleteOMathsInRange(ctx.AbsStart, ctx.AbsEnd);
                ctx = ctx.WithBounds(ctx.AbsStart, ctx.AbsEnd - rangeShrink);

                LogDiag($"merge: {ctx.RemovedHandles.Count} OMath(s) absorbés range=[{ctx.AbsStart},{ctx.AbsEnd}] (shrunk by {rangeShrink}) mergedSource=\"{ctx.Source}\" latex=\"{ctx.Latex}\"");
            }

            int replaceStart = ctx.AbsStart;
            var (newStart, newEnd) = InsertOMathAt(ctx.AbsStart, ctx.AbsEnd, ctx.Latex);
            if (newEnd <= newStart)
            {
                LogDiag($"commit ABORTED latex=\"{ctx.Latex}\" — OMath build failed, rollback effectué dans InsertOMathAt");
                return ctx.WithAbort();
            }
            return ctx.WithInsertedBounds(newStart, newEnd, replaceStart);
        }

        /// <summary>LayoutStage : finalise le layout post-insert. Cas :
        /// (1) cross-merge → FinalizeCrossMergeLayout + InjectListModeMarker,
        /// (2) cases single-line (`{ x=1`) → AppendEmptyParagraph + caret +
        /// inject `{`, (3) sinon → reset list-mode.</summary>
        private MathCursor.Host.Pipeline.CommitContext LayoutImpl(
            MathCursor.Host.Pipeline.CommitContext ctx)
        {
            int newStart = ctx.AbsStart;
            int newEnd = ctx.AbsEnd;
            bool finalizedAnchorIsOursAndEmpty = false;

            if (ctx.WasCrossParagraphMerge)
            {
                var doc = _app.ActiveDocument;
                if (doc != null)
                    FinalizeCrossMergeLayout(doc, ctx.ReplaceStart, ref newStart, ref newEnd, out finalizedAnchorIsOursAndEmpty);
            }

            if (ctx.WasCrossParagraphMerge && ctx.CrossMergeMarker != null)
            {
                _listMode.OnCrossMergeSucceeded(ctx.CrossMergeMarker);
                InjectListModeMarker(ctx.CrossMergeMarker, finalizedAnchorIsOursAndEmpty);
            }
            else if (!ctx.WasCrossParagraphMerge && IsCasesLatex(ctx.Latex))
            {
                // Phase 2 cases (ADR 05-05) : single-line cases activé dès
                // la 1re conversion `{ x=1`.
                var doc2 = _app.ActiveDocument;
                if (doc2 != null)
                {
                    bool didCreateAnchorPara;
                    int caretPos = AppendEmptyParagraphAfterOMath(doc2, newStart, out didCreateAnchorPara);
                    if (caretPos >= 0) SetCaretAtPosition(caretPos);
                    _listMode.OnCrossMergeSucceeded("{");
                    InjectListModeMarker("{", didCreateAnchorPara);
                    LogDiag("list_mode_cases: activated on single-line conversion");
                }
            }
            else
            {
                _listMode.Reset();
            }

            return ctx.WithBounds(newStart, newEnd);
        }

        /// <summary>
        /// Extrait le marker dominant d'un merged source (= chaîne de lignes
        /// jointes par <c>\n</c> issue d'un cross-merge align* ou cases). Le
        /// marker dominant est le premier marker rencontré en parcourant les
        /// lignes du haut vers le bas. Reconnaît les markers align (Phase 1)
        /// ET le marker cases <c>{</c> (Phase 2, ADR 05-05). Retourne null si
        /// aucune ligne ne commence par un marker connu.
        /// </summary>
        private static string ExtractMarkerFromMergedSource(string mergedSource)
        {
            if (string.IsNullOrEmpty(mergedSource)) return null;
            foreach (var line in mergedSource.Split('\n'))
            {
                if (StartsWithAlignMarker(line, out string m)) return m;
                if (CasesCascadeMerger.StartsWithCasesMarker(line)) return "{";
            }
            return null;
        }

        /// <summary>
        /// True si le LaTeX émis commence par <c>\begin{cases}</c> (single-line
        /// ou multi-line cases). Utilisé pour activer le list-mode cases sur
        /// une conversion single-line non-cross-merge (Phase 2 ADR 05-05) :
        /// l'user tape <c>{ x=1</c> Ctrl+Espace, le pipeline produit un cases,
        /// on injecte <c>{ </c> sur le ¶ suivant pour permettre extension.
        /// </summary>
        private static bool IsCasesLatex(string latex)
            => !string.IsNullOrEmpty(latex)
               && latex.TrimStart().StartsWith(@"\begin{cases}", System.StringComparison.Ordinal);

        /// <summary>
        /// Mode visible (ADR 05-05 visible) : injecte le marker en texte plain
        /// au début du ¶ d'ancrage post cross-merge, puis place le caret
        /// juste après l'espace de séparation.
        /// <para>
        /// Si le ¶ d'ancrage a été créé fraîchement par
        /// <see cref="AppendEmptyParagraphAfterOMath"/> (= OMath était dernier
        /// ¶ du doc), on injecte directement sans <c>\r</c>. Sinon (¶ user
        /// pré-existant : séparateur, contenu, etc.), on insère AVEC <c>\r</c>
        /// pour créer un ¶ neuf au marker tout en préservant le ¶ user.
        /// </para>
        /// <para>
        /// Plan calculé par <see cref="ListModeMarkerInjector.Plan"/> (testé
        /// séparément). Définit l'ancre via <see cref="ListModeController.SetAnchor"/>
        /// pour la détection caret-leave côté <see cref="OnSelectionChange"/>.
        /// </para>
        /// </summary>
        private void InjectListModeMarker(string marker, bool hostParaIsOursAndEmpty)
        {
            try
            {
                var doc = _app.ActiveDocument;
                if (doc == null) { _listMode.ClearAnchor(); return; }
                var sel = _app.Selection;
                if (sel == null) { _listMode.ClearAnchor(); return; }

                int paraStart = sel.Paragraphs[1].Range.Start;
                var plan = ListModeMarkerInjector.Plan(marker, hostParaIsOursAndEmpty);

                var insertRange = doc.Range(paraStart, paraStart);
                insertRange.Text = plan.TextToInsert;

                int caretAfter = paraStart + plan.CaretOffset;
                doc.Range(caretAfter, caretAfter).Select();

                _listMode.SetAnchor(paraStart);
                LogDiag($"list_mode: injected \"{plan.TextToInsert.Replace("\r", "\\r")}\" at ¶[{paraStart}], caret=[{caretAfter}], marker=\"{marker}\", createsNewPara={plan.CreatesNewParagraph}");
            }
            catch (Exception ex)
            {
                LogDiag("list_mode_inject_error: " + ex.Message);
                _listMode.ClearAnchor();
            }
        }

        /// <summary>
        /// Vérifie si la zone reverted multi-ligne est encore active et que
        /// le caret est dedans. Si non, invalide la zone (caret hors zone =
        /// abandon de l'édition cascade). Appelée à chaque tick de selection.
        /// </summary>
        private void InvalidateRevertedMultiLineZoneIfCaretLeft(int caretPos)
        {
            if (_revertedMultiLineZoneStart < 0) return;
            // Tolérance d'un char à la fin (caret juste après la zone reste OK)
            if (caretPos < _revertedMultiLineZoneStart || caretPos > _revertedMultiLineZoneEnd + 1)
            {
                LogDiag($"revert_zone: caret={caretPos} hors zone [{_revertedMultiLineZoneStart},{_revertedMultiLineZoneEnd}], invalidée");
                _revertedMultiLineZoneStart = -1;
                _revertedMultiLineZoneEnd = -1;
            }
        }

        /// <summary>
        /// Si le caret a quitté le ¶ d'ancrage du list-mode (ex. clic ailleurs,
        /// flèches haut/bas vers une autre ligne), désactive le mode liste.
        /// Tant qu'on tape dans le même ¶, <c>Range.Start</c> du paragraphe
        /// ne change pas → le mode reste actif. Cf. ADR 05-05.
        /// </summary>
        private void InvalidateListModeIfCaretLeftAnchor(Word.Selection sel)
        {
            try
            {
                int currentParaStart = sel?.Paragraphs?[1]?.Range.Start ?? -1;
                if (_listMode.ShouldInvalidate(currentParaStart))
                {
                    LogDiag($"list_mode: caret ¶[{currentParaStart}] hors anchor ¶[{_listMode.AnchorParaStart}], désactivé");
                    _listMode.OnSelectionMoved();
                }
            }
            catch (Exception ex)
            {
                LogDiag("list_mode_invalidate_error: " + ex.Message);
                _listMode.OnSelectionMoved();
            }
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
        /// <para>
        /// <paramref name="replaceStart"/> = position de début du range remplacé
        /// par <see cref="InsertOMathAt"/>. Sert de borne basse pour le strip
        /// du <c>¶</c> résiduel : on ne strip que si le <c>¶</c> candidat
        /// était DANS le range remplacé (= vraiment un résidu de notre
        /// insertion), pas si c'est un séparateur visuel utilisateur préservé
        /// au-dessus (cf. bug user 04-05 sur revert d'un 2nd multi-ligne).
        /// </para>
        /// <paramref name="newStart"/> et <paramref name="newEnd"/> sont mis
        /// à jour si le strip décale les positions, ce qui permet au caller
        /// de continuer à les utiliser.
        /// </summary>
        private void FinalizeCrossMergeLayout(Word.Document doc, int replaceStart, ref int newStart, ref int newEnd, out bool didCreateAnchorPara)
        {
            didCreateAnchorPara = false;
            try
            {
                StripLeadingResidualEmptyParagraph(doc, replaceStart, ref newStart, ref newEnd);
                // Skip alignment si le transplant XML l'a déjà pré-patché (cf.
                // bug user 04-05 : 2e InsertXML ici causait fusion avec voisin).
                if (!_lastInsertUsedXmlTransplant)
                {
                    EnforceOMathParagraphAlignment(doc, newStart);
                }
                int caretPos = AppendEmptyParagraphAfterOMath(doc, newStart, out didCreateAnchorPara);
                if (caretPos >= 0) SetCaretAtPosition(caretPos);
            }
            catch (Exception ex) { LogDiag("xparMerge_finalize_error: " + ex.Message); }
        }

        /// <summary>
        /// Phase 4.1 : supprime le <c>¶</c> vide qui peut subsister juste avant
        /// l'OMath après cross-merge. Word's BuildUp sur <c>█(...)</c> crée
        /// l'OMathPara dans son propre paragraphe et laisse parfois un <c>¶</c>
        /// orphelin du paragraphe remplacé.
        /// <para>
        /// On strip UNIQUEMENT si le <c>¶</c> candidat est DANS le range qu'on
        /// a remplacé (= <paramref name="replaceStart"/> ou plus tard) — c'est
        /// alors vraiment un résidu de notre insertion. Sinon c'est un
        /// séparateur visuel utilisateur (ex. ligne vide entre 2 multi-lignes
        /// distincts) qu'il faut préserver. Cf. bug user 04-05.
        /// </para>
        /// On vérifie aussi que le paragraphe candidat est bien vide ET qu'il
        /// ne contient PAS d'OMath (un OMath inline a <c>Text=""</c> mais ne
        /// doit pas être supprimé).
        /// </summary>
        private void StripLeadingResidualEmptyParagraph(Word.Document doc, int replaceStart, ref int newStart, ref int newEnd)
        {
            if (newStart <= doc.Content.Start) return;
            try
            {
                var prevRange = doc.Range(newStart - 1, newStart - 1).Paragraphs[1].Range;
                // Garde-fou anti-faux-positif : ne pas stripper un ¶ utilisateur
                // qui était hors du range remplacé. Le résidu BuildUp est
                // toujours À CHEVAL ou DANS le range remplacé.
                if (prevRange.Start < replaceStart)
                {
                    LogDiag($"xparMerge_strip: ¶ at [{prevRange.Start},{prevRange.End}] hors range remplacé (start={replaceStart}), preserved");
                    return;
                }
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
        /// Phase 4.3 (réutilisable) : positionne le caret juste APRÈS le
        /// paragraphe OMath (= début du paragraphe suivant, vide ou pas).
        /// On ne crée un nouveau <c>¶</c> QUE si l'OMath est le dernier
        /// paragraphe du document (= rien après pour accueillir le caret).
        /// <para>
        /// Cf. user 05-05 : « si paragraphe d'après a du contenu, laisser le
        /// caret juste après l'OMath ; ne créer un ¶ que s'il n'y a rien en
        /// dessous ». Évite de polluer le doc avec des ¶ vides parasites.
        /// </para>
        /// Retourne la position où placer le caret, ou <c>-1</c> si aucun
        /// OMath ne couvre la position.
        /// </summary>
        private int AppendEmptyParagraphAfterOMath(Word.Document doc, int posInOMath, out bool didCreateNewPara)
        {
            didCreateNewPara = false;
            try
            {
                foreach (Word.OMath om in doc.OMaths)
                {
                    if (om.Range.Start > posInOMath || om.Range.End <= posInOMath) continue;
                    var omPara = om.Range.Paragraphs[1];
                    int afterOMathPara = omPara.Range.End;

                    // Si l'OMath est le dernier paragraphe du doc → créer un
                    // ¶ vide pour que le caret ait un endroit où atterrir.
                    // Sinon : rien à faire, position caret = début du paragraphe
                    // suivant (qui existe déjà, vide ou avec contenu).
                    if (afterOMathPara >= doc.Content.End)
                    {
                        omPara.Range.InsertParagraphAfter();
                        didCreateNewPara = true;
                        LogDiag("append_para: OMath était last para, ¶ vide créé pour caret");
                    }
                    return afterOMathPara;
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
                bool changed;
                // Une seule fonction couvre les 2 cas (wrap si inline, patch
                // si déjà oMathPara). Sans ça, post-BuildUp Word peut ne pas
                // avoir encore promu l'OMath en oMathPara → l'ancien check
                // skip silencieusement → centré (bug user 06-05).
                string patched = OMathParaJcPatcher.EnsureDisplayWithJc(xml, targetVal, out changed);
                if (!changed) return;
                // Réinsertion forcée : le set typé OMath.Justification met à
                // jour le XML mais ne déclenche pas de re-layout. InsertXML
                // re-process le paragraphe et force le repaint.
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

        // Patch m:jc sur OMathPara — délégué à OMathParaJcPatcher (helper pur testable).
        internal static string PatchOMathParaJc(string xml, string targetVal, out bool changed)
            => OMathParaJcPatcher.Patch(xml, targetVal, out changed);

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

                // Sidecar fusionné — logique extraite dans IntraMergeSidecarBuilder
                // pour testabilité hors Word (bug 06-05 même-ligne, ADR 06-05).
                var mergedSc = IntraMergeSidecarBuilder.Build(
                    leftSource, leftHandle != null ? GetSidecarForHandle(leftHandle) : null,
                    middleSource, _popup?.CurrentSidecar,
                    rightSource, rightHandle != null ? GetSidecarForHandle(rightHandle) : null);
                LogDiag($"merge sidecar: pins={mergedSc.SpanPins.Count} ruleVotes={mergedSc.ZoneVotes.Count}");

                return new MergeResult
                {
                    AbsStart = newStart,
                    AbsEnd = newEnd,
                    MergedSource = sb.ToString(),
                    RemovedHandles = removed,
                    MergedSidecar = mergedSc,
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
        /// (ligne en cours de commit) doit fusionner avec ce qui est au-dessus
        /// pour former un bloc align* multi-ligne. Deux modes :
        /// <list type="bullet">
        /// <item><b>Mode 2 (revert)</b> : si l'utilisateur vient de revert un
        /// OMath multi-ligne (cf. <see cref="_revertedMultiLineZoneStart"/>),
        /// on absorbe TOUS les paragraphes de la zone reverted, y compris la
        /// 1re ligne sans marker. Cf. ADR 04-05 multiline-edit-cascade.</item>
        /// <item><b>Mode 1 (default)</b> : cascade montante conservatrice.
        /// La source courante doit commencer par un marker align
        /// (<c>=</c>/<c>&lt;=&gt;</c>/<c>=&gt;</c>/<c>&lt;=</c>). On absorbe
        /// les paragraphes au-dessus tant qu'ils ont aussi un marker en tête,
        /// et on s'arrête sur un OMath à nous (absorbé) ou un paragraphe sans
        /// marker (non absorbé). Cf. brief 30-04 §3.2 + ADR 04-05.</item>
        /// </list>
        ///
        /// <para>Si match, retourne un <see cref="MergeResult"/> dont le range
        /// englobe <c>[chainStart, currentZoneEnd]</c> et le source mergé est
        /// <c>line1\nline2\n...\ncurrentSource</c>. Le pipeline core (lattice
        /// engine) détectera les <c>\n</c> comme LineBreaks et produira un
        /// LaTeX <c>\begin{align*}...\end{align*}</c>.</para>
        /// </summary>
        private MergeResult TryFindCrossMergeAbove(int absStart, int absEnd, string currentSource)
        {
            try
            {
                if (string.IsNullOrEmpty(currentSource)) return null;
                var doc = _app.ActiveDocument;
                if (doc == null) return null;

                // Mode 2 prioritaire : édition d'un multi-ligne reverted.
                var mode2 = TryAbsorbRevertedMultiLineZone(doc, absStart, absEnd, currentSource);
                if (mode2 != null) return mode2;

                // Mode 1 : dispatch selon marker du current source.
                // - Cases (Phase 2 ADR 05-05) : ligne courante commence par `{ `
                // - Align (Phase 1) : marker align (`<=>`, `=>`, `<=`, `=`)
                // Pas de mix : chaque cascade reconnaît exclusivement son marker.
                if (CasesCascadeMerger.StartsWithCasesMarker(currentSource))
                    return TryCascadeAbsorbCasesChain(doc, absStart, absEnd, currentSource);
                return TryCascadeAbsorbMarkerChain(doc, absStart, absEnd, currentSource);
            }
            catch (Exception ex)
            {
                LogDiag("xparMerge_error: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Mode 2 du cross-merge (cf. ADR 04-05 multiline-edit-cascade) :
        /// si l'user a fait « Revenir à la saisie » sur un OMath multi-ligne
        /// et que le commit courant est dans la zone reverted, on absorbe
        /// TOUS les paragraphes de la zone (y compris la 1re ligne sans
        /// marker). Le source mergé = concat des textes paragraphe par
        /// paragraphe, séparés par <c>\n</c>.
        /// </summary>
        private MergeResult TryAbsorbRevertedMultiLineZone(Word.Document doc, int absStart, int absEnd, string currentSource)
        {
            if (_revertedMultiLineZoneStart < 0) return null;
            if (absStart < _revertedMultiLineZoneStart || absStart > _revertedMultiLineZoneEnd + 1) return null;

            try
            {
                var zoneRange = doc.Range(_revertedMultiLineZoneStart, Math.Min(_revertedMultiLineZoneEnd, doc.Content.End));
                var paras = zoneRange.Paragraphs;
                if (paras == null || paras.Count == 0) return null;

                var paragraphTexts = new List<string>();
                var paragraphStarts = new List<int>();
                int chainStart = int.MaxValue;
                int chainEnd = int.MinValue;
                foreach (Word.Paragraph p in paras)
                {
                    var r = p.Range;
                    if (r.Start < chainStart) chainStart = r.Start;
                    if (r.End - 1 > chainEnd) chainEnd = r.End - 1; // exclut ¶ mark
                    int contentEnd = Math.Max(r.Start, r.End - 1);
                    string txt = doc.Range(r.Start, contentEnd).Text ?? "";
                    paragraphTexts.Add(txt);
                    paragraphStarts.Add(r.Start);
                }
                if (paragraphTexts.Count < 2) return null;

                // Replace la ligne où le user a committé (= identifiée par
                // absStart vs paragraphStarts) avec currentSource. Cf. bug user
                // 05-05 : commit sur ligne 1 d'un revert 3-lignes ne doit PAS
                // remplacer la dernière ligne (ancien comportement hardcodé).
                // Logique extraite et testée dans RevertedZoneMerger.
                string mergedSource = RevertedZoneMerger.BuildMergedSource(
                    paragraphTexts, paragraphStarts, absStart, currentSource);
                // ⚠ newAbsEnd = chainEnd (PAS chainEnd + 1) : on ne consomme PAS
                // le ¶ qui termine la dernière ligne du zone reverted. Sinon
                // BuildUp Word fusionne l'OMath avec le paragraphe suivant et
                // mange le ¶ vide qu'on avait au-dessus du Block B (cf. bug
                // user 04-05 : « ligne à la fin du paragraphe supprimée »).
                int newAbsEnd = Math.Max(chainEnd, absEnd);
                LogDiag($"xparMerge_mode2: revert zone absorbed {paragraphTexts.Count} paragraphs, range=[{chainStart},{newAbsEnd}]");

                return new MergeResult
                {
                    AbsStart = chainStart,
                    AbsEnd = newAbsEnd,
                    MergedSource = mergedSource,
                    RemovedHandles = new List<string>(),
                };
            }
            catch (Exception ex)
            {
                LogDiag("xparMerge_mode2_error: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Mode 1 du cross-merge : cascade montante conservatrice.
        /// La source courante doit commencer par un marker align. On itère
        /// vers le haut paragraphe par paragraphe :
        /// <list type="bullet">
        /// <item>Paragraphe vide → barrier, stop sans absorber.</item>
        /// <item>Paragraphe contient un OMath à nous en fin → ABSORBÉ comme
        /// sommet de la cascade, on stoppe.</item>
        /// <item>Paragraphe texte commence par marker align → ABSORBÉ, on
        /// continue plus haut.</item>
        /// <item>Paragraphe texte sans marker → stop sans absorber.</item>
        /// </list>
        /// </summary>
        private MergeResult TryCascadeAbsorbMarkerChain(Word.Document doc, int absStart, int absEnd, string currentSource)
        {
            if (!StartsWithAlignMarker(currentSource, out string matchedMarker)) return null;
            LogDiag($"xparMerge_mode1: found marker `{matchedMarker}` in current source");

            // Trouver le paragraphe courant
            var currentPara = doc.Range(absStart, absStart).Paragraphs[1];
            int currentParaStart = currentPara.Range.Start;

            // Vérif : entre currentParaStart et absStart, que du whitespace ?
            if (absStart > currentParaStart)
            {
                string between = doc.Range(currentParaStart, absStart).Text ?? "";
                if (!string.IsNullOrEmpty(between) && between.Trim().Length > 0)
                {
                    LogDiag($"xparMerge_mode1: text before zone in current ¶, abort");
                    return null;
                }
            }

            // Cascade montante. On accumule les lignes en ordre TOP→BOTTOM
            // (le source mergé doit être ligne1\nligne2\n...\ncurrent).
            var chainLines = new List<string> { currentSource };
            var removedHandles = new List<string>();
            int chainStart = currentParaStart;
            int cursor = currentParaStart;

            while (cursor > 0)
            {
                Word.Paragraph prev;
                try { prev = doc.Range(cursor - 1, cursor - 1).Paragraphs[1]; }
                catch { break; }
                int prevStart = prev.Range.Start;
                int prevContentEnd = prev.Range.End - 1; // exclut ¶ mark
                if (prevContentEnd <= prevStart) break; // ¶ vide = barrier

                string prevText = doc.Range(prevStart, prevContentEnd).Text ?? "";
                if (string.IsNullOrWhiteSpace(prevText)) break;

                // Tente OMath à nous en fin de ¶ → sommet de la cascade
                var omathTop = FindOwnedOMathAtEndOfParagraph(doc, prevStart, prevContentEnd);
                if (omathTop.HasValue)
                {
                    chainLines.Insert(0, omathTop.Value.source);
                    removedHandles.Add(omathTop.Value.handle);
                    chainStart = omathTop.Value.omStart;
                    LogDiag($"xparMerge_mode1: absorbed OMath top range=[{omathTop.Value.omStart},{prevContentEnd}] source=\"{Preview(omathTop.Value.source)}\"");
                    break;
                }

                // Tente texte avec marker en tête → continue cascade
                if (StartsWithAlignMarker(prevText, out _))
                {
                    chainLines.Insert(0, prevText);
                    chainStart = prevStart;
                    cursor = prevStart;
                    LogDiag($"xparMerge_mode1: cascaded text ¶ [{prevStart},{prevContentEnd}] = \"{Preview(prevText)}\"");
                    continue;
                }

                // Texte sans marker = stop sans absorber
                break;
            }

            if (chainLines.Count < 2) return null;

            string mergedSource = string.Join("\n", chainLines);

            // Fusion des sidecars (ADR 06-05 Phase 1.6) : pour chaque chainLine
            // top→bottom, on récupère le sidecar mémorisé si la ligne est un
            // OMath absorbé, vide sinon. Les offsets sont décalés selon la
            // position cumulative dans la mergedSource.
            // chainLines a 1 entrée pour currentSource (bottom) + N pour les
            // absorbées (top→middle), donc la dernière chainLine = currentSource
            // → son sidecar = popup.CurrentSidecar.
            var sidecarParts = new List<MathCursor.Core.Resolution.ResolutionSidecar>();
            var offsetShifts = new List<int>();
            int cumulativeShift = 0;
            int absorbedHandleIdx = 0;
            for (int li = 0; li < chainLines.Count; li++)
            {
                MathCursor.Core.Resolution.ResolutionSidecar partSc;
                bool isLastLine = (li == chainLines.Count - 1);
                if (isLastLine)
                {
                    partSc = _popup?.CurrentSidecar
                        ?? MathCursor.Core.Resolution.ResolutionSidecar.Empty;
                }
                else if (absorbedHandleIdx < removedHandles.Count)
                {
                    partSc = GetSidecarForHandle(removedHandles[absorbedHandleIdx++]);
                }
                else
                {
                    partSc = MathCursor.Core.Resolution.ResolutionSidecar.Empty;
                }
                sidecarParts.Add(partSc);
                offsetShifts.Add(cumulativeShift);
                cumulativeShift += chainLines[li].Length + 1; // +1 pour le \n
            }
            var mergedSidecar = MathCursor.Core.Resolution.SidecarMerger.Merge(
                sidecarParts, offsetShifts);

            return new MergeResult
            {
                AbsStart = chainStart,
                AbsEnd = absEnd,
                MergedSource = mergedSource,
                RemovedHandles = removedHandles,
                MergedSidecar = mergedSidecar,
            };
        }

        /// <summary>
        /// Cascade cases (Phase 2, ADR 05-05) : la source courante commence
        /// par <c>{ </c>. Itère vers le haut paragraphe par paragraphe :
        /// <list type="bullet">
        /// <item>¶ vide → barrier, stop</item>
        /// <item>OMath à nous en fin de ¶ avec source qui commence aussi par
        /// <c>{ </c> → absorbé comme sommet de cascade, stop</item>
        /// <item>¶ texte commence par <c>{ </c> → absorbé, continue</item>
        /// <item>Sinon (texte sans <c>{ </c>, marker align...) → stop sans
        /// absorber. Pas de mix avec align.</item>
        /// </list>
        /// La logique de merge effective est déléguée à <see cref="CasesCascadeMerger"/>
        /// (helper pur testé séparément).
        /// </summary>
        private MergeResult TryCascadeAbsorbCasesChain(Word.Document doc, int absStart, int absEnd, string currentSource)
        {
            if (!CasesCascadeMerger.StartsWithCasesMarker(currentSource)) return null;
            LogDiag($"xparMerge_cases: found cases marker `{{ ` in current source");

            // Trouver le paragraphe courant
            var currentPara = doc.Range(absStart, absStart).Paragraphs[1];
            int currentParaStart = currentPara.Range.Start;

            // Vérif : entre currentParaStart et absStart, que du whitespace ?
            if (absStart > currentParaStart)
            {
                string between = doc.Range(currentParaStart, absStart).Text ?? "";
                if (!string.IsNullOrEmpty(between) && between.Trim().Length > 0)
                {
                    LogDiag("xparMerge_cases: text before zone in current ¶, abort");
                    return null;
                }
            }

            // Cascade montante. paragraphsAbove en ordre TOP→BOTTOM.
            var paragraphsAbove = new List<string>();
            var removedHandles = new List<string>();
            int chainStart = currentParaStart;
            int cursor = currentParaStart;

            while (cursor > 0)
            {
                Word.Paragraph prev;
                try { prev = doc.Range(cursor - 1, cursor - 1).Paragraphs[1]; }
                catch { break; }
                int prevStart = prev.Range.Start;
                int prevContentEnd = prev.Range.End - 1; // exclut ¶ mark
                if (prevContentEnd <= prevStart) break; // ¶ vide = barrier

                // OMath à nous en fin de ¶ → potentiel sommet de cascade.
                // On absorbe SEULEMENT si sa source est un cases (commence par `{ `).
                var omathTop = FindOwnedOMathAtEndOfParagraph(doc, prevStart, prevContentEnd);
                if (omathTop.HasValue)
                {
                    if (CasesCascadeMerger.StartsWithCasesMarker(omathTop.Value.source))
                    {
                        paragraphsAbove.Insert(0, omathTop.Value.source);
                        removedHandles.Add(omathTop.Value.handle);
                        chainStart = omathTop.Value.omStart;
                        LogDiag($"xparMerge_cases: absorbed OMath top range=[{omathTop.Value.omStart},{prevContentEnd}] source=\"{Preview(omathTop.Value.source)}\"");
                    }
                    else
                    {
                        LogDiag($"xparMerge_cases: OMath above is not cases, stop");
                    }
                    break;
                }

                string prevText = doc.Range(prevStart, prevContentEnd).Text ?? "";
                if (string.IsNullOrWhiteSpace(prevText)) break;

                if (CasesCascadeMerger.StartsWithCasesMarker(prevText))
                {
                    paragraphsAbove.Insert(0, prevText);
                    chainStart = prevStart;
                    cursor = prevStart;
                    LogDiag($"xparMerge_cases: cascaded text ¶ [{prevStart},{prevContentEnd}] = \"{Preview(prevText)}\"");
                    continue;
                }

                // Texte non-cases (ou marker align) → stop sans mix
                break;
            }

            // Délègue le merge final au helper pur (testé séparément).
            var cascade = CasesCascadeMerger.BuildCascade(paragraphsAbove, currentSource);
            if (cascade == null) return null;

            // Fusion des sidecars (parallèle au pattern TryCascadeAbsorbMarkerChain
            // — bug user 06-05 « système {…} multi-ligne fait sauter les vec »).
            // Reconstruit chainLines depuis paragraphsAbove + currentSource avec
            // les mêmes bornes que CasesCascadeMerger.BuildCascade (= AbsorbedCount
            // dernières lignes de paragraphsAbove + currentSource).
            var chainLines = new List<string>();
            int startIdx = paragraphsAbove.Count - cascade.AbsorbedCount;
            for (int i = startIdx; i < paragraphsAbove.Count; i++)
                chainLines.Add(paragraphsAbove[i]);
            chainLines.Add(currentSource);

            var sidecarParts = new List<MathCursor.Core.Resolution.ResolutionSidecar>();
            var offsetShifts = new List<int>();
            int cumulativeShift = 0;
            int absorbedHandleIdx = 0;
            for (int li = 0; li < chainLines.Count; li++)
            {
                MathCursor.Core.Resolution.ResolutionSidecar partSc;
                bool isLastLine = (li == chainLines.Count - 1);
                if (isLastLine)
                {
                    partSc = _popup?.CurrentSidecar
                        ?? MathCursor.Core.Resolution.ResolutionSidecar.Empty;
                }
                else if (absorbedHandleIdx < removedHandles.Count)
                {
                    partSc = GetSidecarForHandle(removedHandles[absorbedHandleIdx++]);
                }
                else
                {
                    partSc = MathCursor.Core.Resolution.ResolutionSidecar.Empty;
                }
                sidecarParts.Add(partSc);
                offsetShifts.Add(cumulativeShift);
                cumulativeShift += chainLines[li].Length + 1; // +1 pour le \n
            }
            var mergedSidecar = MathCursor.Core.Resolution.SidecarMerger.Merge(
                sidecarParts, offsetShifts);

            return new MergeResult
            {
                AbsStart = chainStart,
                AbsEnd = absEnd,
                MergedSource = cascade.MergedSource,
                RemovedHandles = removedHandles,
                MergedSidecar = mergedSidecar,
            };
        }

        /// <summary>
        /// Vérifie si la chaîne (après TrimStart) commence par un des markers
        /// align. Retourne le marker matché via le out param.
        /// </summary>
        private static bool StartsWithAlignMarker(string s, out string matchedMarker)
        {
            matchedMarker = null;
            if (string.IsNullOrEmpty(s)) return false;
            string trimmed = s.TrimStart();
            foreach (var m in AlignMarkers)
            {
                if (trimmed.StartsWith(m, StringComparison.Ordinal))
                {
                    matchedMarker = m;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Cherche un OMath à nous (= avec un bookmark <c>mcEq_*</c>) qui
        /// termine le paragraphe défini par <paramref name="paraStart"/> et
        /// <paramref name="paraContentEnd"/> (= position du dernier char avant
        /// le ¶ mark). Retourne <c>(omStart, source, handle)</c> si trouvé,
        /// sinon null.
        /// </summary>
        private (int omStart, string source, string handle)? FindOwnedOMathAtEndOfParagraph(Word.Document doc, int paraStart, int paraContentEnd)
        {
            try
            {
                foreach (Word.OMath om in doc.OMaths)
                {
                    var rng = om.Range;
                    if (rng.Start < paraStart || rng.End > paraContentEnd) continue;
                    // Doit terminer le ¶ : ce qui suit l'OMath jusqu'au ¶ doit être whitespace
                    if (rng.End < paraContentEnd)
                    {
                        string after = doc.Range(rng.End, paraContentEnd).Text ?? "";
                        if (after.Trim().Length > 0) continue;
                    }
                    string h = FindOurHandleForOMath(om);
                    if (h == null) continue;
                    try
                    {
                        var stored = _store.RetrieveAsync(new EquationHandle(h)).GetAwaiter().GetResult();
                        if (stored != null && !string.IsNullOrEmpty(stored.Source))
                        {
                            return (rng.Start, stored.Source, h);
                        }
                    }
                    catch (Exception ex) { LogDiag($"xparMerge_owned_omath_retrieve_error: {ex.Message}"); }
                }
            }
            catch (Exception ex) { LogDiag("xparMerge_owned_omath_scan_error: " + ex.Message); }
            return null;
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
        /// Extrait le premier élément <c>&lt;w:p ... &gt;...&lt;/w:p&gt;</c>
        /// d'un XML WordOpenXML package. Utilisé pour récupérer juste le
        /// paragraphe (sans pkg:package wrapper) à splicer dans un autre
        /// fullDocXml.
        /// </summary>
        private static string ExtractFirstWPElement(string xml)
        {
            if (string.IsNullOrEmpty(xml)) return null;
            var m = System.Text.RegularExpressions.Regex.Match(
                xml,
                @"<w:p[\s>](?:(?!</w:p>).)*?</w:p>",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            return m.Success ? m.Value : null;
        }

        /// <summary>
        /// Remplace <paramref name="targetCount"/> paragraphes consécutifs à
        /// l'index <paramref name="targetIdx0"/> (0-based) dans le full doc XML
        /// par un seul nouveau paragraphe <paramref name="newParaWp"/>.
        /// Manipulation pur structurelle (regex sur <c>&lt;w:p&gt;</c>),
        /// pas d'API Word. Cf. test offline xmltest_modified.docx 04-05 :
        /// produit un doc structurellement clean (pas de fusion possible).
        /// </summary>
        private static string ReplaceParagraphsInDocXml(string fullDocXml, int targetIdx0, int targetCount, string newParaWp)
        {
            if (string.IsNullOrEmpty(fullDocXml) || string.IsNullOrEmpty(newParaWp)) return null;
            if (targetIdx0 < 0 || targetCount < 1) return null;
            var paraRegex = new System.Text.RegularExpressions.Regex(
                @"<w:p[\s>](?:(?!</w:p>).)*?</w:p>",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            var matches = paraRegex.Matches(fullDocXml);
            if (targetIdx0 + targetCount > matches.Count) return null;
            var firstMatch = matches[targetIdx0];
            var lastMatch = matches[targetIdx0 + targetCount - 1];
            var sb = new System.Text.StringBuilder(fullDocXml.Length);
            sb.Append(fullDocXml, 0, firstMatch.Index);
            sb.Append(newParaWp);
            sb.Append(fullDocXml, lastMatch.Index + lastMatch.Length, fullDocXml.Length - (lastMatch.Index + lastMatch.Length));
            return sb.ToString();
        }

        /// <summary>
        /// Pattern build-isolated → transplant XML (cf. ADR 04-05). Construit
        /// l'OMath dans une zone temporaire isolée à la fin du document, en
        /// l'entourant de <paramref name="textBefore"/> et
        /// <paramref name="textAfter"/> (vides pour multi-ligne display, =
        /// contenu du paragraphe cible avant/après le math zone pour inline).
        /// Capture le <b>full WordOpenXML package</b> du paragraphe résultant
        /// (avec <c>&lt;pkg:package&gt;</c> wrapper + namespaces, format
        /// requis par <c>Range.InsertXML</c>) puis nettoie la zone temporaire.
        /// <para>
        /// Le XML capturé peut ensuite être inséré via <c>Range.InsertXML</c>
        /// pour remplacer le paragraphe cible — sans BuildUp, donc sans
        /// risque d'absorption d'OMaths voisins.
        /// </para>
        /// </summary>
        private string BuildOMathXmlIsolated(Word.Document doc, string textBefore, string latex, string textAfter)
        {
            string unicodeMath;
            try { unicodeMath = LatexToUnicodeMath.Convert(latex); }
            catch (Exception ex) { LogDiag("iso_l2um_error: " + ex.Message); return null; }
            if (string.IsNullOrEmpty(unicodeMath)) return null;
            textBefore ??= "";
            textAfter ??= "";

            int origContentEnd = doc.Content.End;
            int insertPos = origContentEnd - 1; // avant le ¶ final du doc
            // Layout temporaire :
            //   [insertPos] \r [textBefore] [unicodeMath] [textAfter] \r [final ¶]
            int paraStart = origContentEnd;
            int unicodeStart = paraStart + textBefore.Length;
            int unicodeEnd = unicodeStart + unicodeMath.Length;
            string capturedXml = null;

            try
            {
                // 1. Insert "\r" + textBefore + unicodeMath + textAfter + "\r"
                string toInsert = "\r" + textBefore + unicodeMath + textAfter + "\r";
                doc.Range(insertPos, insertPos).Text = toInsert;

                // 2. BuildUp UNIQUEMENT sur la portion unicodeMath (pas
                //    textBefore/After — c'est du texte normal qu'on préserve).
                var mathRange = doc.Range(unicodeStart, unicodeEnd);
                mathRange.OMaths.Add(mathRange);
                mathRange.OMaths.BuildUp();

                // 3. Find the new OMath (couvre unicodeStart)
                Word.OMath newOMath = null;
                foreach (Word.OMath om in doc.OMaths)
                {
                    var rng = om.Range;
                    if (rng.Start <= unicodeStart && rng.End > unicodeStart)
                    {
                        newOMath = om;
                        break;
                    }
                }
                if (newOMath == null) { LogDiag("iso_build: OMath not found after BuildUp"); return null; }

                Word.Paragraph omPara = null;
                try { omPara = newOMath.Range.Paragraphs[1]; } catch { }
                if (omPara == null) return null;

                // 4. Capture FULL WordOpenXML package du paragraphe (= avec
                //    pkg:package wrapper + namespaces). InsertXML demande ce
                //    format complet, sinon « Impossible d'insérer le code XML ».
                capturedXml = omPara.Range.WordOpenXML;
                if (string.IsNullOrEmpty(capturedXml))
                {
                    LogDiag("iso_build: empty WordOpenXML capture");
                    capturedXml = null;
                }
                else
                {
                    // Diag : check si la BuildUp en zone temp a accidentellement
                    // pulled in un autre OMath du doc (cas absorption).
                    int omathCount = System.Text.RegularExpressions.Regex.Matches(capturedXml, "<m:oMath\\b(?!Pa)").Count;
                    int eqArrCount = System.Text.RegularExpressions.Regex.Matches(capturedXml, "<m:eqArr\\b").Count;
                    LogDiag($"iso_capture: xml_len={capturedXml.Length} omathCount={omathCount} eqArrCount={eqArrCount}");
                }
            }
            catch (Exception ex) { LogDiag("iso_build_error: " + ex.Message); }
            finally
            {
                // 5. Cleanup : supprime tout ce qu'on a ajouté à la fin du doc.
                //    diff = ce que le doc a gagné en chars = exactement ce qu'on
                //    a inséré (post-BuildUp). On le supprime depuis insertPos.
                try
                {
                    int currentEnd = doc.Content.End;
                    int diff = currentEnd - origContentEnd;
                    if (diff > 0) doc.Range(insertPos, insertPos + diff).Delete();
                }
                catch (Exception ex) { LogDiag("iso_cleanup_error: " + ex.Message); }
            }

            return capturedXml;
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

            // SAUVEGARDE du texte original avant remplacement, pour rollback si
            // l'insertion échoue. Règle dure : on ne doit JAMAIS laisser dans
            // Word du texte technique (UnicodeMath ou LaTeX brut) si la conversion
            // en équation a échoué.
            string originalText;
            try { originalText = doc.Range(absStart, absEnd).Text ?? ""; }
            catch { originalText = ""; }

            // === PATTERN UNIFIÉ XML TRANSPLANT (cf. ADR 04-05) ===
            // Construit l'OMath en zone isolée fin de doc avec textBefore +
            // unicodeMath + textAfter, capture le full paragraph WordOpenXML,
            // remplace le paragraphe cible via InsertXML. Pour multi-ligne
            // display (latex contient \begin{...}), on ne préserve pas de
            // surround : la cible est remplacée intégralement par la zone
            // math. Pour inline single-eq, on préserve le texte du paragraphe
            // avant absStart et après absEnd. Aucun BuildUp à la cible →
            // aucune absorption possible des OMaths voisins.
            //
            // Fallback : .doc legacy (CompatibilityMode < 14) → ancien pattern
            // API in-place.
            bool isDocxOoxml = false;
            try { isDocxOoxml = doc.CompatibilityMode >= 14; } catch { }
            bool isDisplayMath = latex.IndexOf("\\begin{align", StringComparison.Ordinal) >= 0
                              || latex.IndexOf("\\begin{cases", StringComparison.Ordinal) >= 0;

            int newStart = absStart;
            int newEnd = absStart;
            bool omathCreated = false;
            bool usedXmlTransplant = false;

            if (isDocxOoxml)
            {
                try
                {
                    // 1. Identifier les paragraphes cibles. Probe à absStart+1 / absEnd-1
                    //    pour être strictement DANS les paragraphes cibles.
                    int safeProbeStart = Math.Min(absStart + 1, doc.Content.End - 1);
                    int safeProbeEnd = Math.Max(absStart, Math.Min(absEnd - 1, doc.Content.End - 1));
                    if (safeProbeStart > safeProbeEnd) safeProbeStart = safeProbeEnd;
                    var firstPara = doc.Range(safeProbeStart, safeProbeStart).Paragraphs[1];
                    var lastPara = doc.Range(safeProbeEnd, safeProbeEnd).Paragraphs[1];
                    int firstParaStart = firstPara.Range.Start;
                    int lastParaStart = lastPara.Range.Start;

                    // Identifier les indices 0-based des paragraphes cibles dans
                    // l'ordre du document (correspond aux <w:p> dans WordOpenXML).
                    int firstTargetIdx0 = -1;
                    int targetCount = 0;
                    int totalParas = doc.Paragraphs.Count;
                    for (int i = 1; i <= totalParas; i++)
                    {
                        var p = doc.Paragraphs[i];
                        if (p.Range.Start >= firstParaStart && p.Range.Start <= lastParaStart)
                        {
                            if (firstTargetIdx0 < 0) firstTargetIdx0 = i - 1; // 0-based
                            targetCount++;
                        }
                    }
                    LogDiag($"insert_transplant: target idx0={firstTargetIdx0} count={targetCount} (totalParas={totalParas})");

                    // 2. textBefore/textAfter pour inline single-eq
                    string textBefore = "";
                    string textAfter = "";
                    if (!isDisplayMath && targetCount == 1)
                    {
                        try
                        {
                            int paraStart = firstPara.Range.Start;
                            int paraContentEnd = firstPara.Range.End - 1;
                            if (absStart > paraStart) textBefore = doc.Range(paraStart, absStart).Text ?? "";
                            if (absEnd < paraContentEnd) textAfter = doc.Range(absEnd, paraContentEnd).Text ?? "";
                        }
                        catch { }
                    }

                    // 3. Build l'OMath en zone isolée + force m:jc=left.
                    //    Si captured XML a déjà <m:oMathPara> (ex. cases/align
                    //    multi-ligne), patch m:jc=left dedans. Si captured est
                    //    inline pur (ex. single-line `Y=2X+1`), on enrobe
                    //    pré-emptivement avec <m:oMathPara><m:oMathParaPr>
                    //    <m:jc=left> — sinon Word auto-promote standalone-in-¶
                    //    en display sans m:jc → centré par défaut (cf. bug user
                    //    05-05 « formules une ligne s'auto-centrent »).
                    string capturedXml = BuildOMathXmlIsolated(doc, textBefore, latex, textAfter);
                    if (!string.IsNullOrEmpty(capturedXml))
                    {
                        try
                        {
                            capturedXml = OMathParaJcPatcher.EnsureDisplayWithLeftJc(capturedXml, out _);
                        }
                        catch (Exception ex) { LogDiag("insert_transplant_ensure_error: " + ex.Message); }

                        // 4. SINGLE ROUND-TRIP XML : valider sur Python que la
                        //    manipulation pur XML est sound (cf. test
                        //    xmltest_modified.docx 04-05). Plutôt que paragraph
                        //    par paragraph (qui cause fusion), on lit le full
                        //    doc XML, on remplace les paragraphes cibles in-memory,
                        //    on réécrit en un seul doc.Content.InsertXML.
                        try
                        {
                            string fullDocXml = doc.Content.WordOpenXML;
                            string newParaWp = ExtractFirstWPElement(capturedXml);
                            if (string.IsNullOrEmpty(newParaWp))
                            {
                                LogDiag("insert_transplant: failed to extract <w:p> from captured");
                            }
                            else
                            {
                                string modifiedDocXml = ReplaceParagraphsInDocXml(
                                    fullDocXml, firstTargetIdx0, targetCount, newParaWp);
                                if (string.IsNullOrEmpty(modifiedDocXml))
                                {
                                    LogDiag("insert_transplant: failed to splice doc XML");
                                }
                                else
                                {
                                    doc.Content.InsertXML(modifiedDocXml);
                                    usedXmlTransplant = true;
                                    LogDiag($"insert_transplant: full-doc InsertXML ok, len={modifiedDocXml.Length}");
                                }
                            }
                        }
                        catch (Exception ex) { LogDiag("insert_transplant_fulldoc_error: " + ex.Message); }

                        // 5. Find OMath inséré : identifier par PARAGRAPHE cible
                        //    (pas par position fuzzy). Le transplant a remplacé
                        //    les paragraphes [firstTargetIdx0 .. firstTargetIdx0+targetCount)
                        //    par UN seul nouveau paragraphe. On cherche l'OMath
                        //    dedans. Sinon, l'ancienne tolérance `>= firstParaStart - 5`
                        //    matchait à tort un OMath en fin de ¶ précédent (cf. bug
                        //    user 05-05 « soit f » caret monte/descend après insertion).
                        if (usedXmlTransplant)
                        {
                            int omathCountAfter = 0;
                            try { omathCountAfter = doc.OMaths.Count; } catch { }
                            try
                            {
                                // doc.Paragraphs est 1-based ; targetIdx0 est 0-based.
                                // Le nouveau ¶ unique est à l'index 1-based targetIdx0+1.
                                int newParaIdx = firstTargetIdx0 + 1;
                                if (newParaIdx >= 1 && newParaIdx <= doc.Paragraphs.Count)
                                {
                                    var newPara = doc.Paragraphs[newParaIdx];
                                    foreach (Word.OMath om in newPara.Range.OMaths)
                                    {
                                        var rng = om.Range;
                                        int eqArrCount = 0;
                                        try
                                        {
                                            string omXml = om.Range.WordOpenXML ?? "";
                                            eqArrCount = System.Text.RegularExpressions.Regex.Matches(omXml, "<m:eqArr>").Count;
                                        }
                                        catch { }
                                        LogDiag($"insert_transplant: OMaths.Count={omathCountAfter} matched [{rng.Start},{rng.End}] in ¶[{newParaIdx}] eqArrs={eqArrCount}");
                                        newStart = rng.Start;
                                        newEnd = rng.End;
                                        omathCreated = true;
                                        break;
                                    }
                                }
                            }
                            catch (Exception ex) { LogDiag("insert_transplant_locate_error: " + ex.Message); }
                            if (!omathCreated) LogDiag($"insert: transplant ok but OMath not found in ¶[{firstTargetIdx0 + 1}]");
                        }
                    }
                    else { LogDiag("insert: build-isolated returned null"); }
                }
                catch (Exception ex) { LogDiag("insert_xml_transplant_error: " + ex.Message); }
            }

            // Fallback API in-place (legacy .doc OU si transplant XML a échoué)
            if (!omathCreated)
            {
                string unicodeMath = LatexToUnicodeMath.Convert(latex);
                LogDiag($"insert: fallback API in-place. latex→umath \"{latex}\" → \"{unicodeMath}\"");
                bool nextIsWs = absEnd < docEnd && IsWhitespaceCharAt(doc, absEnd);
                string insertText = nextIsWs ? unicodeMath : unicodeMath + " ";
                try { doc.Range(absStart, absEnd).Text = insertText; } catch (Exception ex) { LogDiag("insert_replace_error: " + ex.Message); }
                int insertedLen = unicodeMath.Length;
                var mathRange = doc.Range(absStart, absStart + insertedLen);
                try
                {
                    mathRange.OMaths.Add(mathRange);
                    mathRange.OMaths.BuildUp();
                }
                catch (Exception ex) { LogDiag("omath_add_error: " + ex.Message); }
                newEnd = absStart + insertedLen;
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
                // L'invariant "OMath aligné selon le ¶ parent" est garanti
                // plus bas par SyncOMathJustificationToParagraph (appelé
                // après ce bloc, via le path !usedXmlTransplant). Pas de
                // patch ad-hoc ici : depuis l'option B (ADR 06-05),
                // `PatchOMathParaJustificationViaXml` couvre wrap + patch
                // donc fonctionne même si Word n'a pas encore promu le
                // standalone-in-¶ en oMathPara.
                if (!omathCreated)
                {
                    // Rollback : restore le texte original
                    LogDiag($"omath NOT created — rollback to original=\"{originalText}\"");
                    try
                    {
                        var fallbackRange = doc.Range(absStart, Math.Min(absStart + insertText.Length, doc.Content.End));
                        fallbackRange.Text = originalText;
                        int restoredEnd = absStart + originalText.Length;
                        try { _app.Selection.SetRange(restoredEnd, restoredEnd); } catch { }
                    }
                    catch (Exception ex) { LogDiag("rollback_error: " + ex.Message); }
                    return (absStart, absStart);
                }
            }

            // On aligne l'OMath sur l'alignement du paragraphe texte. Skipped
            // si on a utilisé le transplant XML car le m:jc a déjà été pré-patché
            // dans le XML capturé avant l'unique InsertXML (cf. bug user 04-05 :
            // un 2e InsertXML via PatchOMathParaJustificationViaXml ICI causait
            // une fusion avec l'OMath voisin).
            _lastInsertUsedXmlTransplant = usedXmlTransplant;
            if (!usedXmlTransplant)
            {
                SyncOMathJustificationToParagraph(doc, newStart, newEnd);
            }

            // Positionne le curseur juste après l'OMath, puis vérifie qu'on n'est
            // PAS resté dans l'éditeur math (Word interprète parfois "pile après"
            // comme "encore dedans", surtout en display-mode). Nudge jusqu'à 3 fois
            // pour sortir proprement sur une zone de texte libre.
            int afterPos = ComputeAfterOMathCaret(doc, newEnd);
            try { _app.Selection.SetRange(afterPos, afterPos); } catch { }
            NudgeCursorOutOfMath(doc, maxAttempts: 3);
            return (newStart, newEnd);
        }

        /// <summary>
        /// Position où placer le caret juste après un OMath, sans déborder dans
        /// le ¶ suivant. Bug user 05-05 « soit f » Ctrl+Espace → cursor descend :
        /// quand l'OMath est en fin de ¶, <c>omEnd + 1</c> tombe sur le ¶ mark
        /// (= start du ¶ suivant). On clamp à <c>paraContentEnd</c> (= juste
        /// avant le ¶ mark). La logique pure est dans <see cref="CaretPositionCalculator"/>.
        /// </summary>
        private int ComputeAfterOMathCaret(Word.Document doc, int omEnd)
        {
            int paraContentEnd;
            try
            {
                var paraRange = doc.Range(omEnd, omEnd).Paragraphs[1].Range;
                paraContentEnd = Math.Max(paraRange.Start, paraRange.End - 1);
            }
            catch (Exception ex)
            {
                LogDiag("compute_after_omath_para_error: " + ex.Message);
                paraContentEnd = omEnd; // fallback : pas de débordement
            }
            return CaretPositionCalculator.ClampAfterOMathToParagraph(omEnd, paraContentEnd, doc.Content.End);
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

                    // Niveau 1 : SetRange juste après la fin de l'OMath courant.
                    // Clamp au ¶ courant (= juste avant le ¶ mark) sinon on
                    // déborde dans le ¶ suivant quand l'OMath est en fin de
                    // ligne (cf. bug user 05-05).
                    int omEnd = sel.OMaths[1].Range.End;
                    int target = ComputeAfterOMathCaret(doc, omEnd);
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
