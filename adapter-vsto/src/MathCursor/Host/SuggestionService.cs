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
        // BookmarkPrefix gardé pour compat. Préférer Bookmarks.EquationBookmarkRegistry.Prefix.
        private const string BookmarkPrefix = Bookmarks.EquationBookmarkRegistry.Prefix;

        private readonly Word.Application _app;
        private readonly WordContextReader _contextReader;
        private readonly MathNerDetector _ner;
        private readonly Engine _engine;
        private readonly ZoneResolver _resolver;

        // Contexte global de session pour le ranking contextuel multi-zoom.
        // Cf. brief 2026-05-07. Initialisé dans le constructeur, alimenté
        // au fil des commits popup et resets au changement de ¶.
        private readonly MathCursor.Core.Resolution.GlobalContext _globalCtx;

        // Position du début du ¶ d'ancrage du caret au dernier tracking.
        // -1 = pas encore tracké. Au changement (caret a quitté le ¶),
        // l'historique paragraphe du _globalCtx est reset.
        private int _lastTrackedParaStart = -1;
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

        // Cache LaTeX → <m:oMath> XML. Couche 2/3 perf (ADR 2026-05-12).
        // Extrait en classe dédiée par P2.4 (ADR refactor pure-merger).
        private readonly OMathXmlCache _omathXmlCache = new OMathXmlCache(capacity: 32);

        // Pré-fetch du paraXml courant. Couche 3/3 perf (ADR 2026-05-12).
        // Extrait en classe dédiée par P2.5 (refactor archi).
        private readonly ParaXmlPrefetcher _paraXmlPrefetcher;

        // Ghost doc pour BuildUp en isolation. P2.6 du refactor archi —
        // zéro mutation du doc actif user.
        private readonly OMathStagingService _omathStaging;

        // Registre des bookmarks mcEq_* (P2.8 refactor archi).
        private readonly Bookmarks.EquationBookmarkRegistry _bookmarks;

        // Layout finalizer post-commit (P2.13 refactor archi).
        private readonly Layout.PostCommitLayoutFinalizer _layoutFinalizer;

        // Stratégies d'insertion (P2.7 refactor archi). Enchaînées dans
        // l'ordre fast_path → splice → atomic. Première qui Success gagne.
        private readonly Inserters.PureFastPathInserter _fastPathInserter;
        private readonly Inserters.InlineSpliceInserter _spliceInserter;
        private readonly Inserters.AtomicRangeInserter _atomicInserter;

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

        // Mode édition d'une OMath existante — P2.10 refactor archi.
        // L'état (_editHandle, _editingOMathStart, _editPopup) + le flow
        // complet (Sync polling, TryEnter, OnRevertRequested) sont
        // encapsulés dans EditModeController.
        private EditMode.EditModeController _editController;

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
        // _editingOMathStart : déplacé dans EditModeController (P2.10).
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
        // Handle de l'OMath original avant revert (Phase 2 ADR 06-05 — bug
        // user 06-05 « revert + re-commit fait sauter les vec »). Lu par
        // TryAbsorbRevertedMultiLineZone pour récupérer le sidecar mémorisé
        // et le passer en MergedSidecar. -1 = pas de revert actif.
        private string _revertedHandleId;

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
            // Contexte global de session : agrège SidecarSignal (L1, votes du
            // sidecar) + ParagraphResolutionsSignal (L2, pins du ¶ courant).
            // Alimenté par PropagateCommittedPinsToParagraphHistory au commit
            // popup, reset par TrackParagraphChangeAndResetIfNeeded au
            // changement de ¶. Cf. brief 2026-05-07-global-context-multi-zoom-ranking.
            _globalCtx = new MathCursor.Core.Resolution.GlobalContext();
            _globalCtx.AddSignal(new MathCursor.Core.Resolution.Signals.SidecarSignal());
            _globalCtx.AddSignal(new MathCursor.Core.Resolution.Signals.ParagraphResolutionsSignal());
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _contextReader = new WordContextReader(_app);
            _lastActionTracker = new LastActionTracker(ReadParagraphContextForReport);
            _handleRegistry = new EquationHandleRegistry(
                createBookmark: CreateBookmarkForRange,
                deleteBookmark: DeleteBookmarkByHandle,
                popupSidecar: () => _popup?.CurrentSidecar
                                    ?? MathCursor.Core.Resolution.ResolutionSidecar.Empty);
            _paraXmlPrefetcher = new ParaXmlPrefetcher(
                new WordParaXmlSource(_app),
                LogDiag);
            _omathStaging = new OMathStagingService(_app, LogDiag);
            _bookmarks = new Bookmarks.EquationBookmarkRegistry(() => _app.ActiveDocument, LogDiag);
            _layoutFinalizer = new Layout.PostCommitLayoutFinalizer(_app, () => _lastInsertUsedXmlTransplant, LogDiag);
            _editController = new EditMode.EditModeController(
                app: _app,
                store: _store,
                bookmarks: _bookmarks,
                handleRegistry: _handleRegistry,
                hideSuggestionPopup: HidePopup,
                getCaretScreenPos: GetCaretScreenPosition,
                log: LogDiag);
            _editController.MultiLineReverted += (s, e, h) =>
            {
                _revertedMultiLineZoneStart = s;
                _revertedMultiLineZoneEnd = e;
                _revertedHandleId = h;
            };
            _editController.InlineReverted += () =>
            {
                _revertedMultiLineZoneStart = -1;
                _revertedMultiLineZoneEnd = -1;
                _revertedHandleId = null;
            };
            _fastPathInserter = new Inserters.PureFastPathInserter(LogDiag);
            _spliceInserter = new Inserters.InlineSpliceInserter(_omathXmlCache, _paraXmlPrefetcher, _omathStaging, LogDiag);
            _atomicInserter = new Inserters.AtomicRangeInserter(_omathStaging, LogDiag);

            // Pipeline de mergers (cf. ADR 2026-05-06-Meta-zone-merger-pipeline) :
            // remplace l'empilement de `if (merged == null)` qui vivait dans
            // OnPopupCommitRequested. Ordre = priorité (intra avant cross,
            // reverted avant cases avant marker). Chaque merger est self-guarding :
            // il retourne null si non-applicable au commit courant.
            _mergerPipeline = new MergerPipeline(new IZoneMerger[]
            {
                new IntraOMathsMerger(
                    getActiveDoc: () => _app.ActiveDocument,
                    store: _store,
                    bookmarks: _bookmarks,
                    getPopupSidecar: () => _popup?.CurrentSidecar ?? MathCursor.Core.Resolution.ResolutionSidecar.Empty,
                    getSidecarForHandle: GetSidecarForHandle,
                    log: LogDiag),
                new RevertedMultiLineMerger(
                    getActiveDoc: () => _app.ActiveDocument,
                    getZone: () => new RevertedMultiLineMerger.RevertedZone(
                        _revertedMultiLineZoneStart, _revertedMultiLineZoneEnd, _revertedHandleId),
                    getSidecarForHandle: GetSidecarForHandle,
                    log: LogDiag),
                new CasesChainCascadeMerger(
                    getActiveDoc: () => _app.ActiveDocument,
                    probe: new ParagraphCascadeProbe(_bookmarks, _store, LogDiag),
                    getPopupSidecar: () => _popup?.CurrentSidecar ?? MathCursor.Core.Resolution.ResolutionSidecar.Empty,
                    getSidecarForHandle: GetSidecarForHandle,
                    log: LogDiag),
                new MarkerChainCascadeMerger(
                    getActiveDoc: () => _app.ActiveDocument,
                    probe: new ParagraphCascadeProbe(_bookmarks, _store, LogDiag),
                    getPopupSidecar: () => _popup?.CurrentSidecar ?? MathCursor.Core.Resolution.ResolutionSidecar.Empty,
                    getSidecarForHandle: GetSidecarForHandle,
                    log: LogDiag),
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
            try { _editController?.Close(); } catch { }
            try { _omathStaging?.Dispose(); } catch { }
            _popup = null;
            _pollTimer = null;
            _installed = false;
        }

        public bool IsPopupVisible => (_popup?.IsVisible == true);
        public bool IsEditPopupVisible => _editController?.IsPopupVisible == true;
        public bool IsAnyPopupVisible => IsPopupVisible || IsEditPopupVisible;
        public bool IsNavMode => (_popup?.IsNavMode == true);

        public void MoveSelection(int delta) => _popup?.MoveSelection(delta);
        public bool MoveSelectionHorizontal(int delta)
            => _popup?.MoveSelectionHorizontal(delta) == true;
        public void EnterNavMode() => _popup?.EnterNavMode();
        public void HidePopup()
        {
            _popup?.HidePopup(resetCaches: true);
            _editController?.HidePopup();
            _resolver?.Clear();
            ResetIterativeExpansion();
        }

        private void HidePopupTransient()
        {
            _popup?.HidePopup(resetCaches: false);
            _editController?.HidePopup();
        }

        private void OnSelectionChange(Word.Selection sel)
        {
            // Try-catch défensif : Word désactive l'add-in après une exception
            // non-gérée dans un event handler. CheckAndUpdate fait beaucoup de
            // travail (lecture paragraphe, NER, etc.) et peut échouer.
            try
            {
                // Tracking du ¶ courant pour reset l'historique paragraphe
                // du _globalCtx au changement de ¶ (cf. brief 2026-05-07).
                try { TrackParagraphChangeAndResetIfNeeded(sel); } catch { }
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

        // ─── Plomberie GlobalContext (brief 2026-05-07) ─────────────────

        /// <summary>
        /// Event émis à chaque résolution via <see cref="ResolveWithContext"/>.
        /// Consommé par le pane debug <c>ContextInspectorPane</c> pour afficher
        /// le contexte et les hints en temps réel. Reste optionnel — pas
        /// d'abonné = pas d'aggregate inutile.
        /// </summary>
        public event System.EventHandler<ContextResolveEventArgs> ContextResolved;

        /// <summary>
        /// Helper qui résout via le <c>_resolver</c> en passant systématiquement
        /// le <c>_globalCtx</c> de session. Ainsi tous les chemins (preview,
        /// commit, IsIncomplete check) bénéficient des signaux contextuels
        /// configurés (Sidecar L1 + ParagraphResolutions L2 pour l'instant).
        /// </summary>
        private MathCursor.Core.ResolvedZone ResolveWithContext(
            string rawSource,
            MathCursor.Core.Resolution.ResolutionSidecar sidecar = null)
        {
            var resolved = _resolver.Resolve(rawSource ?? "", _globalCtx, sidecar);
            EmitContextResolvedIfSubscribed(rawSource, sidecar, resolved);
            return resolved;
        }

        /// <summary>
        /// Trouve l'altIdx active pour une rule donnée (= celle qui sera
        /// appliquée par défaut par <c>ZoneResolver.ResolveBestAlt</c>).
        /// Utilisé par la popup pour filtrer l'alt active de la liste
        /// affichée (demande user 2026-05-07).
        ///
        /// <para><b>Cohérence critique</b> : utilise la MÊME logique que
        /// ZoneResolver — <c>ScoringHints.BestAltForRule</c> sur les hints
        /// du <c>_globalCtx</c> + RulePins du sidecar courant. Sinon
        /// désync entre la finale (ZoneResolver) et le filtrage popup
        /// (= bug user 2026-05-07 « final vec et alt vec dans la
        /// liste »).</para>
        /// </summary>
        private int FindActiveAltIdxForRule(string ruleId)
        {
            if (string.IsNullOrEmpty(ruleId)) return -1;

            // 1) Pref in-session via _popup.CurrentSidecar.RulePins (priorité
            // — c'est ce que ZoneResolver consulte aussi en premier).
            var popupSidecar = _popup?.CurrentSidecar;
            if (popupSidecar != null)
            {
                foreach (var rp in popupSidecar.RulePins)
                    if (rp.RuleId == ruleId) return rp.AltIdx;
            }

            // 2) Hints contextuels (= ce que ZoneResolver utilise comme
            // fallback). MÊME logique BestAltForRule (premier en cas
            // d'égalité, score > 0 obligatoire).
            var snapshot = _globalCtx.Snapshot(
                "", MathCursor.Core.Resolution.ResolutionSidecar.Empty);
            var hints = _globalCtx.Scorer.Aggregate(snapshot);
            var (alt, score) = hints.BestAltForRule(ruleId);
            return score > 0 ? alt : -1;
        }

        private void EmitContextResolvedIfSubscribed(
            string rawSource,
            MathCursor.Core.Resolution.ResolutionSidecar sidecar,
            MathCursor.Core.ResolvedZone resolved)
        {
            var evt = ContextResolved;
            if (evt == null) return; // évite l'aggregate si personne n'écoute
            try
            {
                var snapshot = _globalCtx.Snapshot(
                    rawSource,
                    sidecar ?? MathCursor.Core.Resolution.ResolutionSidecar.Empty);
                var hints = _globalCtx.Scorer.Aggregate(snapshot);
                evt(this, new ContextResolveEventArgs(rawSource, snapshot, hints, resolved));
            }
            catch (System.Exception ex) { LogDiag("context_event_error: " + ex.Message); }
        }

        /// <summary>
        /// Tracking du ¶ d'ancrage du caret. Initialement on resetait
        /// l'historique des résolutions au changement de ¶, mais Word
        /// considère chaque ligne d'un système / chaîne d'équivalences
        /// multi-ligne comme un ¶ séparé → reset trop agressif qui vidait
        /// l'historique entre lignes du même bloc sémantique. Bug constaté
        /// 2026-05-07 : Pins ¶ = 0 sur ligne 2 d'un système alors que
        /// ligne 1 venait de faire 3 désambig vec.
        ///
        /// V1 : pas de reset au changement de ¶. L'historique grandit
        /// (cap 32 dans GlobalContext gère la mémoire). À raffiner plus
        /// tard : decay temporel + reset explicite à des actions discriminantes
        /// (clic dans une autre section, ouverture d'un autre OMath éloigné).
        /// </summary>
        private void TrackParagraphChangeAndResetIfNeeded(Word.Selection sel)
        {
            if (sel == null) return;
            try { _lastTrackedParaStart = sel.Paragraphs[1].Range.Start; }
            catch { /* ignore */ }
            // Pas de ResetParagraphHistory ici — voir summary.
        }

        /// <summary>
        /// Pousse les <see cref="MathCursor.Core.Resolution.SpanPin"/> d'un
        /// sidecar (typiquement <c>_popup.CurrentSidecar</c> au commit) vers
        /// l'historique paragraphe du <see cref="_globalCtx"/>. Cas type :
        /// ligne 1 d'un système résolue en vec → ligne 2 hérite via le
        /// <c>ParagraphResolutionsSignal</c> (cf. brief 2026-05-07 cas AB/AD).
        /// </summary>
        private void PropagateCommittedPinsToParagraphHistory(
            MathCursor.Core.Resolution.ResolutionSidecar sidecar)
        {
            if (sidecar == null || sidecar.IsEmpty) return;
            foreach (var pin in sidecar.SpanPins)
            {
                if (pin == null) continue;
                _globalCtx.RecordParagraphResolution(pin);
            }
        }

        // ─── Fin plomberie GlobalContext ─────────────────────────────────

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

        /// <summary>Délégué — cf. <see cref="Bookmarks.EquationBookmarkRegistry.FindHandleForOMath"/>.</summary>
        private string FindOurHandleForOMath(Word.OMath om) => _bookmarks.FindHandleForOMath(om);

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
                // Sync l'edit mode controller : entrée si caret sur OMath
                // à nous, sortie sinon. Si actif on rend la main au polling
                // (pas de popup de suggestion concurrente).
                var omAtCaret = FindOMathAtCaret();
                if (_editController.Sync(omAtCaret, inPostCommitCooldown))
                {
                    if (omAtCaret != null && inPostCommitCooldown) HidePopup();
                    return;
                }

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

            // Couche 3/3 perf stack (ADR 2026-05-12) : pré-fetch
            // opportuniste du paraXml courant si on est sur un tick
            // STABLE (= même texte que le tick précédent → user idle).
            // Évite de payer 60ms de WordOpenXML pendant la frappe
            // rapide ; payé seulement sur les pauses.
            _paraXmlPrefetcher.Tick();
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
            try { resolved = ResolveWithContext(target.Text); }
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
                try { resolved = ResolveWithContext(span); }
                catch (Exception ex) { LogDiag("manual_engine_error: " + ex.Message); return; }

                int absStart = paragraphAbsStart + spanStart;
                int absEnd = paragraphAbsStart + spanEnd;

                _lastZoneSource = span;
                _editController?.Close();

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
            try { resolved = ResolveWithContext(span); }
            catch (Exception ex) { LogDiag("iterative_extend_error: " + ex.Message); return; }
            if (string.IsNullOrEmpty(resolved.TopLatex)) return;

            int absStart = _iterativeParaAbsStart + _iterativeSpanStart;
            int absEnd = _iterativeParaAbsStart + _iterativeSpanEnd;
            _lastZoneSource = span;
            _editController?.Close();
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
            try { return ResolveWithContext(zone.Text).IsIncomplete; }
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
            // Cherche l'altIdx active pour cette rule via le _globalCtx
            // (cross-commit, contrairement aux _rulePreferences popup qui
            // sont reset au commit). Permet à la popup de filtrer l'alt
            // déjà appliquée par le RulePin/scoring (cf. demande user
            // 2026-05-07).
            int activeAltIdx = FindActiveAltIdxForRule(ruleId);
            _popup.Show(resolved.TopLatex, ruleId, alts, spotStart, spotEnd,
                resolved.AllMatches, popupX, popupY, debugText, activeAltIdx,
                baseTopLatex: resolved.BaseTopLatex);
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
                var resolved = ResolveWithContext(src);
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
            var editingHandle = _editController?.CurrentEditingHandle;
            if (editingHandle != null)
            {
                initialSidecar = MathCursor.Core.Resolution.SidecarMerger.Merge(
                    new[]
                    {
                        GetSidecarForHandle(editingHandle.Id),
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
                editingHandle: editingHandle);
            // Wrap tout le pipeline dans un seul UndoRecord nommé Word →
            // un Ctrl+Z annule le commit entier d'un coup (pas étape par
            // étape) → fini les états partiels incohérents. Cf. ADR
            // 2026-05-11-Fix-commit-grouped-in-single-undo-record.
            using (var _undoScope = new UndoRecordScope(_app, "Convertir formule"))
            {
                try
                {
                    ctx = _commitPipeline.Run(ctx);
                }
                catch (Exception ex) { LogDiag("commit_pipeline_error: " + ex.Message); }
            }

            // Propage les pins du popup vers l'historique paragraphe du
            // _globalCtx. Permet aux zones suivantes du même ¶ de bénéficier
            // automatiquement des choix de désambig (cf. brief 2026-05-07,
            // cas AB/AD système 2 lignes).
            //
            // Note : on lit _popup.CurrentSidecar plutôt que ctx.Sidecar
            // parce que le commit pipeline a typiquement ctx.Sidecar=Empty
            // en commit standard (les pins ne transitent pas par le pipeline
            // pour l'insertion, juste par cross-merge en mode edit). C'est
            // _popup qui détient les pins accumulés pendant la session popup
            // (via _sessionSpanPins). Lecture AVANT HidePopup() qui reset.
            try
            {
                var popupSidecar = _popup?.CurrentSidecar;
                if (popupSidecar != null && !popupSidecar.IsEmpty)
                    PropagateCommittedPinsToParagraphHistory(popupSidecar);
                else if (ctx?.Sidecar != null && !ctx.Sidecar.IsEmpty)
                    PropagateCommittedPinsToParagraphHistory(ctx.Sidecar);
            }
            catch { }

            // Reset état
            _lastZoneAbsStart = -1;
            _lastZoneAbsEnd = -1;
            _lastZoneSource = "";
            _editController?.Close();
            _revertedMultiLineZoneStart = -1;
            _revertedMultiLineZoneEnd = -1;
            _revertedHandleId = null;
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
            // Merger pur : on NE PRÉ-SUPPRIME PLUS rien. InsertOMathAt reçoit
            // les bornes pré-absorption ; il remplace atomiquement la range
            // entière (Word retire les OMaths absorbées au passage via
            // Range.InsertXML). Cleanup store + bookmarks en post-success.
            // Cf. ADR 2026-05-12-Refactor-pure-merger-atomic-insert.
            if (ctx.RemovedHandles != null && ctx.RemovedHandles.Count > 0)
            {
                LogDiag($"merge: {ctx.RemovedHandles.Count} OMath(s) absorbés range=[{ctx.AbsStart},{ctx.AbsEnd}] mergedSource=\"{ctx.Source}\" latex=\"{ctx.Latex}\"");
            }

            int replaceStart = ctx.AbsStart;
            var (newStart, newEnd) = InsertOMathAt(ctx.AbsStart, ctx.AbsEnd, ctx.Latex, ctx.RemovedHandles);
            if (newEnd <= newStart)
            {
                LogDiag($"commit ABORTED latex=\"{ctx.Latex}\" — OMath build failed, doc intact (no pre-mutation)");
                return ctx.WithAbort();
            }

            // Post-success uniquement : on retire les handles absorbés du
            // store. Les bookmarks Word des OMaths absorbées ont été
            // évacuées avec elles par Range.InsertXML (atomique).
            if (ctx.RemovedHandles != null && ctx.RemovedHandles.Count > 0)
            {
                foreach (var h in ctx.RemovedHandles)
                {
                    try { _store.RemoveAsync(new EquationHandle(h)).GetAwaiter().GetResult(); }
                    catch (Exception ex) { LogDiag($"merge_remove_error handle={h}: {ex.Message}"); }
                    _handleRegistry.Forget(h);
                }
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
                    _layoutFinalizer.FinalizeCrossMerge(doc, ctx.ReplaceStart, ref newStart, ref newEnd, out finalizedAnchorIsOursAndEmpty);
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
                    int caretPos = _layoutFinalizer.AppendEmptyParagraphAfterOMath(doc2, newStart, out didCreateAnchorPara);
                    if (caretPos >= 0) _layoutFinalizer.SetCaretAtPosition(caretPos);
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
                if (MarkerChainCascadeMerger.StartsWithAlignMarker(line, out string m)) return m;
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
                // Contenu actuel du ¶ d'ancrage : permet de détecter le cas
                // bug 2026-05-07 (¶ contient déjà juste le marker → ne pas
                // dupliquer) côté ListModeMarkerInjector.
                string existingParaContent = null;
                try { existingParaContent = sel.Paragraphs[1].Range.Text; } catch { }
                var plan = ListModeMarkerInjector.Plan(marker, hostParaIsOursAndEmpty, existingParaContent);

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
                _revertedHandleId = null;
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

        /// <summary>Délégués — cf. <see cref="Bookmarks.EquationBookmarkRegistry"/>.</summary>
        private void DeleteBookmarkByHandle(string handleId) => _bookmarks.Delete(handleId);
        private void CreateBookmarkForRange(string handleId, int absStart, int absEnd)
            => _bookmarks.Create(handleId, absStart, absEnd);

        /// <summary>
        /// Extrait le premier élément <c>&lt;w:p ... &gt;...&lt;/w:p&gt;</c>
        /// d'un XML WordOpenXML package. Utilisé pour récupérer juste le
        /// paragraphe (sans pkg:package wrapper) à splicer dans un autre
        /// fullDocXml.
        /// </summary>
        /// <summary>
        /// Diag : quand para_splice skip, on dump (1) la STRUCTURE
        /// des <w:p> trouvés dans le XML, (2) compte ALL elements par
        /// LocalName (au cas où namespace bizarre), (3) sauvegarde le
        /// paraXml intégral dans %TEMP% pour inspection manuelle.
        /// </summary>
        private void DumpParaRunsForDiag(string paraXml, string mathSource)
        {
            try
            {
                // 0. Toujours dumper paraXml sur disque pour inspection
                //    offline (gros XML 260KB pas dumpable dans le log).
                try
                {
                    string tmpPath = System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(),
                        $"mathcursor_diag_paraXml_{DateTime.Now:yyyyMMdd_HHmmss_fff}.xml");
                    System.IO.File.WriteAllText(tmpPath, paraXml ?? "<null>");
                    LogDiag($"para_splice_diag_dump: {tmpPath} (len={paraXml?.Length ?? 0})");
                }
                catch (Exception dumpEx) { LogDiag("para_splice_diag_dump_error: " + dumpEx.Message); }

                // 1. Compte ALL <w:p>, <w:r>, <w:t> par LocalName (peu
                //    importe le namespace prefix/URI).
                try
                {
                    var xdoc = System.Xml.Linq.XDocument.Parse(paraXml);
                    int pCount = 0, rCount = 0, tCount = 0, mOMathCount = 0;
                    foreach (var el in xdoc.Descendants())
                    {
                        string ln = el.Name.LocalName;
                        if (ln == "p" && el.Name.NamespaceName.Contains("wordprocessingml")) pCount++;
                        else if (ln == "r" && el.Name.NamespaceName.Contains("wordprocessingml")) rCount++;
                        else if (ln == "t" && el.Name.NamespaceName.Contains("wordprocessingml")) tCount++;
                        else if (ln == "oMath" && el.Name.NamespaceName.Contains("officeDocument")) mOMathCount++;
                    }
                    LogDiag($"para_splice_diag: source=\"{Preview(mathSource)}\" w:p={pCount} w:r={rCount} w:t={tCount} m:oMath={mOMathCount}");

                    // 2. Dump structure des paras (par LocalName, donc
                    //    namespace-agnostic).
                    var paras = xdoc.Descendants()
                        .Where(e => e.Name.LocalName == "p" && e.Name.NamespaceName.Contains("wordprocessingml"))
                        .ToList();
                    int paraIdx = 0;
                    foreach (var p in paras)
                    {
                        var sb1 = new System.Text.StringBuilder();
                        sb1.Append($"para_splice_diag p[{paraIdx}]: children=[");
                        int c = 0;
                        foreach (var el in p.Elements())
                        {
                            if (c > 0) sb1.Append(",");
                            string ln = el.Name.LocalName;
                            string tail = "";
                            if (ln == "r")
                            {
                                var t = el.Elements().FirstOrDefault(x => x.Name.LocalName == "t");
                                if (t != null) tail = $"=\"{Preview(t.Value)}\"";
                            }
                            sb1.Append(ln + tail);
                            c++;
                            if (c >= 30) { sb1.Append(",..."); break; }
                        }
                        sb1.Append("]");
                        LogDiag(sb1.ToString());
                        paraIdx++;
                        if (paraIdx >= 5) { LogDiag($"para_splice_diag: ... +{paras.Count - 5} more paras"); break; }
                    }
                }
                catch (Exception parseEx)
                {
                    LogDiag($"para_splice_diag_parse_error: {parseEx.Message}");
                }
            }
            catch (Exception ex) { LogDiag("para_splice_diag_error: " + ex.Message); }
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
        /// <summary>
        /// Délègue à <see cref="OMathStagingService"/> (ghost doc). P2.6 du
        /// refactor archi — zéro mutation du doc actif. Le paramètre
        /// <paramref name="doc"/> est conservé pour compat avec call sites
        /// existants mais ignoré.
        /// </summary>
        private string BuildOMathXmlIsolated(Word.Document doc, string latex)
            => _omathStaging.BuildOMathXml(latex);

        /// <summary>
        /// Remplace le range [absStart, absEnd) du document par un OMath construit
        /// à partir du LaTeX fourni. Word's BuildUp ne parse pas le LaTeX nativement,
        /// on convertit donc d'abord en UnicodeMath (le format natif qu'il comprend).
        /// Renvoie (newStart, newEnd) = bornes réelles de l'OMath inséré pour qu'on
        /// puisse accrocher un bookmark dessus.
        /// </summary>
        private (int newStart, int newEnd) InsertOMathAt(int absStart, int absEnd, string latex,
            System.Collections.Generic.IReadOnlyList<string> absorbedHandles = null)
        {
            var swTotal = System.Diagnostics.Stopwatch.StartNew();
            var doc = _app.ActiveDocument;
            if (doc == null) return (absStart, absEnd);
            int docStart = doc.Content.Start;
            int docEnd = doc.Content.End;
            LogDiag($"PERF InsertOMathAt enter: absStart={absStart} absEnd={absEnd} docEnd={docEnd}");
            if (absStart < docStart) absStart = docStart;
            if (absEnd > docEnd) absEnd = docEnd;
            if (absEnd <= absStart) return (absStart, absEnd);

            // Trim whitespaces aux bords de la zone détectée : le NER inclut
            // parfois un espace avant/après, on ne veut pas le remplacer.
            while (absStart < absEnd && IsWhitespaceCharAt(doc, absStart)) absStart++;
            while (absEnd > absStart && IsWhitespaceCharAt(doc, absEnd - 1)) absEnd--;
            if (absEnd <= absStart) return (absStart, absEnd);

            var ctx = BuildInsertContext(doc, absStart, absEnd, latex, absorbedHandles);
            if (ctx == null) return (absStart, absEnd);

            // Stratégies enchaînées : fast_path → splice → atomic. Première
            // qui Success gagne. P2.7 du refactor archi.
            int newStart = absStart, newEnd = absStart;
            bool ok = false;
            foreach (var result in TryInsertStrategies(ctx))
            {
                if (result.Success)
                {
                    newStart = result.NewStart;
                    newEnd = result.NewEnd;
                    ok = true;
                    break;
                }
            }
            if (!ok)
            {
                LogDiag($"commit ABORTED latex=\"{latex}\" — aucune stratégie d'insert n'a abouti, doc intact");
            }
            _lastInsertUsedXmlTransplant = ok;

            // Positionne le curseur juste après l'OMath, puis nudge.
            int afterPos = ComputeAfterOMathCaret(doc, newEnd);
            try { _app.Selection.SetRange(afterPos, afterPos); } catch { }
            NudgeCursorOutOfMath(doc, maxAttempts: 3);
            swTotal.Stop();
            LogDiag($"PERF InsertOMathAt total={swTotal.ElapsedMilliseconds}ms");
            return (newStart, newEnd);
        }

        /// <summary>
        /// Construit l'<see cref="Inserters.InsertContext"/> : identifie
        /// firstPara, lastPara, targetCount, isDisplayMath. Retourne
        /// <c>null</c> si Word interop échoue.
        /// </summary>
        private Inserters.InsertContext BuildInsertContext(
            Word.Document doc, int absStart, int absEnd, string latex,
            System.Collections.Generic.IReadOnlyList<string> absorbedHandles)
        {
            try
            {
                bool isDisplayMath = latex.IndexOf("\\begin{align", StringComparison.Ordinal) >= 0
                                  || latex.IndexOf("\\begin{cases", StringComparison.Ordinal) >= 0;

                int safeProbeStart = Math.Min(absStart + 1, doc.Content.End - 1);
                int safeProbeEnd = Math.Max(absStart, Math.Min(absEnd - 1, doc.Content.End - 1));
                if (safeProbeStart > safeProbeEnd) safeProbeStart = safeProbeEnd;
                var firstPara = doc.Range(safeProbeStart, safeProbeStart).Paragraphs[1];
                var lastPara = doc.Range(safeProbeEnd, safeProbeEnd).Paragraphs[1];
                int firstParaStart = firstPara.Range.Start;
                int lastParaStart = lastPara.Range.Start;

                // targetCount via navigation Next() (évite doc.Paragraphs[i]
                // qui est O(N) sur gros doc — bug perf 12-05).
                int targetCount = 1;
                if (firstParaStart != lastParaStart)
                {
                    var cursor = firstPara;
                    while (cursor != null && cursor.Range.Start < lastParaStart)
                    {
                        try { cursor = cursor.Next(); } catch { cursor = null; }
                        if (cursor == null) break;
                        targetCount++;
                        if (cursor.Range.Start >= lastParaStart) break;
                    }
                }
                LogDiag($"PERF target_count count={targetCount}");

                return new Inserters.InsertContext(doc, absStart, absEnd, latex,
                    isDisplayMath, targetCount, firstPara, absorbedHandles);
            }
            catch (Exception ex)
            {
                LogDiag("build_insert_context_error: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Enchaîne les stratégies d'insertion dans l'ordre. Yields chaque
        /// <see cref="Inserters.InsertResult"/> ; la 1re Success court-circuite.
        /// </summary>
        private System.Collections.Generic.IEnumerable<Inserters.InsertResult> TryInsertStrategies(
            Inserters.InsertContext ctx)
        {
            yield return _fastPathInserter.TryInsert(ctx);
            yield return _spliceInserter.TryInsert(ctx);
            yield return _atomicInserter.TryInsert(ctx);
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
