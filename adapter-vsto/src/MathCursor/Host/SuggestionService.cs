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
// P11.14 (2026-05-22) : alias renommé pour éviter conflit avec namespace
// MathCursor.Engine (= POC v2). Cf. ADR 2026-05-22-Feat-engine-poc-isolation.
using LatticeEng = MathCursor.Core.LatticeEngine;
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
        private readonly Word.Application _app;
        private readonly WordContextReader _contextReader;
        private readonly MathNerDetector _ner;
        private readonly LatticeEng _engine;
        private readonly ZoneResolver _resolver;
        // Vocab locale (= stopwords, span_delimiters, math_prefix_keywords, …
        // data-driven via YAML). Chantier 1 — 2026-05-25.
        private MathCursor.Engine.Vocabulary.LocaleVocabulary _vocab;

        // Contexte global de session pour le ranking contextuel multi-zoom.
        // Cf. brief 2026-05-07. Initialisé dans le constructeur, alimenté
        // au fil des commits popup et resets au changement de ¶.
        private readonly MathCursor.Core.Resolution.GlobalContext _globalCtx;

        // Position du début du ¶ d'ancrage du caret au dernier tracking.
        // -1 = pas encore tracké. Au changement (caret a quitté le ¶),
        // l'historique paragraphe du _globalCtx est reset.
        private int _lastTrackedParaStart = -1;
        // _store + IEquationStore retirés (Phase B) — source/latex/hash vivent
        // dans cc.Tag MCMeta. Cf. brief 2026-05-18 backlink natif.

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

        // _bookmarks supprimé (Phase B) — identification "à nous" via
        // CC MathCursor + cc.Tag, plus de scan doc.Bookmarks.

        // Layout finalizer post-commit (P2.13 refactor archi).
        private readonly Layout.PostCommitLayoutFinalizer _layoutFinalizer;

        // Caches XML (OMathXmlCache, ParaXmlPrefetcher), ghost-doc staging
        // (OMathStagingService), CaretPositioner, 3 Inserters (fast_path,
        // splice, atomic) tous supprimés 2026-05-14 : InsertOMathAt utilise
        // la recette minimale SetRange + TypeText + OMaths.Add + BuildUp
        // natif. Plus de splice XML, plus de ghost doc, plus de stratégies
        // enchaînées, plus de caret custom.

        // État de la dernière popup affichée — nécessaire pour commit sur Enter.
        // Encapsulé dans un ZoneSpan unique (paraStart interne + string-pos
        // bornes + snapshot text + OMaths regions) qui sert (a) à positionner
        // la popup via TryToInternal, (b) à traduire les coords au commit
        // sans dépendre de fields séparés à synchroniser. Cf. ADR
        // 2026-05-23-Refactor-zonespan-popup-commit-coords.
        private MathCursor.Host.Detection.ZoneSpan _currentZoneSpan;
        // Positions absolues internes dérivées de _currentZoneSpan au show.
        // Sert à l'anti-spam (= compare dismissed avec ces vraies positions),
        // au check d'entrée en edit mode (l.1545), et au repositionnement
        // popup. Re-calculées à chaque ShowPopup via ZoneSpan.TryToInternal.
        private int _lastZoneAbsStart = -1;
        private int _lastZoneAbsEnd = -1;

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

        // État d'extension itérative (ADR 29-04). Activé au 1er Ctrl+Espace
        // qui ouvre la popup ; chaque appui suivant tant que la popup est
        // ouverte étend la zone d'un cran vers la gauche.
        // Reset à HidePopup ou OnSelectionChange.
        // État iteratif déplacé dans ManualTriggerController (P2.16).
        private ManualTrigger.ManualTriggerController _manualTrigger;

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

        public SuggestionService(Word.Application app, MathNerDetector ner, LatticeEng engine)
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
            // P7b (2026-05-21) : injection du PatternPipeline + Registry pour
            // activer les templates compositionnels (forall-belongs, ensemble,
            // interval-union) — restauration UX de la popup après P6 et
            // extension via composition. Cf. ADR
            // 2026-05-21-Feat-pattern-pipeline-integration-zone-resolver (P7a)
            // + 2026-05-21-Feat-suggestion-service-pattern-injection (P7b).
            var (patternPipeline, patternRegistry) =
                MathCursor.Core.Patterns.DefaultPatternRegistry.BuildBoth();

            // P32 (2026-05-23) : MathCursor.Engine v2 est désormais le moteur
            // PRINCIPAL. Le legacy MathCursor.Core (LatticeEngine + Patterns)
            // reste comme fallback pour les ~10% de cas non couverts —
            // marqué [Obsolete]. Kill-switch d'urgence : MATHCURSOR_ENGINE_V2=0.
            // Cf. ADR 2026-05-23-Feat-engine-v2-promotion.
            MathCursor.Core.IResolvedZoneSource? engineSource = null;
            bool engineV2Off = string.Equals(
                System.Environment.GetEnvironmentVariable("MATHCURSOR_ENGINE_V2"),
                "0", System.StringComparison.Ordinal);
            if (!engineV2Off)
            {
                try
                {
                    var engineV2 = MathCursor.Engine.MathEngine.BuildDefault("fr");
                    engineSource = new MathCursor.Engine.Adapter.EngineZoneSource(engineV2);
                    _vocab = engineV2.Vocab;
                    LogDiag("engine-v2 active (principal — legacy in fallback)");
                }
                catch (System.Exception ex)
                {
                    LogDiag("engine-v2 init failed (legacy will handle): " + ex.Message);
                }
            }
            else
            {
                LogDiag("engine-v2 disabled by env MATHCURSOR_ENGINE_V2=0 — legacy only");
            }
            // Fallback : si engine v2 KO ou désactivé, charge le vocab quand
            // même (= ManualTrigger/ZoneRefiner data-driven).
            if (_vocab == null)
                _vocab = MathCursor.Engine.Vocabulary.LocaleVocabulary.LoadEmbedded("fr");
            _resolver = new ZoneResolver(_engine, patternPipeline, patternRegistry, engineSource);
            // Contexte global de session : agrège SidecarSignal (L1, votes du
            // sidecar) + ParagraphResolutionsSignal (L2, pins du ¶ courant).
            // Alimenté par PropagateCommittedPinsToParagraphHistory au commit
            // popup, reset par TrackParagraphChangeAndResetIfNeeded au
            // changement de ¶. Cf. brief 2026-05-07-global-context-multi-zoom-ranking.
            _globalCtx = new MathCursor.Core.Resolution.GlobalContext();
            _globalCtx.AddSignal(new MathCursor.Core.Resolution.Signals.SidecarSignal());
            _globalCtx.AddSignal(new MathCursor.Core.Resolution.Signals.ParagraphResolutionsSignal());
            _contextReader = new WordContextReader(_app);
            _lastActionTracker = new LastActionTracker(ReadParagraphContextForReport);
            _handleRegistry = new EquationHandleRegistry(
                popupSidecar: () => _resolver.BuildSidecar()
                                    ?? MathCursor.Core.Resolution.ResolutionSidecar.Empty);
            _layoutFinalizer = new Layout.PostCommitLayoutFinalizer(_app, LogDiag);
            _manualTrigger = new ManualTrigger.ManualTriggerController(
                app: _app,
                contextReader: _contextReader,
                resolveWithContext: s => ResolveWithContext(s),
                isCaretInOurOMath: () => FindOMathAtCaret() != null,
                passThroughToPolling: CheckAndUpdate,
                isSuggestionPopupVisible: () => _popup?.IsVisible == true,
                closeEditMode: () => _editController?.Close(),
                showPopupAndEnterNavMode: (resolved, zone, rawLen, dbg) =>
                {
                    ShowPopup(resolved, zone, rawLen, dbg);
                    _popup?.EnterNavMode();
                },
                log: LogDiag,
                vocab: _vocab);
            _editController = new EditMode.EditModeController(
                app: _app,
                handleRegistry: _handleRegistry,
                hideSuggestionPopup: HidePopup,
                getCaretScreenPos: Caret.CaretScreenPositionReader.Read,
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
            // Pipeline merger DÉBRANCHÉ 2026-05-18 (étape intermédiaire user
            // request) : le pipeline ne traite que la zone NER courante,
            // insère un OMath pour celle-ci uniquement. Pas d'absorption de
            // voisins, pas de cascade marker, pas de revert-multi-line.
            // Les classes Merging/* restent en place pour le moment, à virer
            // au prochain cleanup ou à re-câbler avec la nouvelle approche
            // (Phase 5 = NeighborFinder.FindAbove unifié intra+cross).
            _commitPipeline = new MathCursor.Host.Pipeline.CommitPipeline(
                new MathCursor.Host.Pipeline.ICommitStage[]
                {
                    new MathCursor.Host.Pipeline.Stages.ResolverStage(_resolver, () => _globalCtx),
                    new MathCursor.Host.Pipeline.Stages.SnapshotStage(_lastActionTracker),
                    new MathCursor.Host.Pipeline.Stages.InserterStage(InserterImpl),
                    new MathCursor.Host.Pipeline.Stages.StoreStage(_handleRegistry, LogDiag),
                    new MathCursor.Host.Pipeline.Stages.LayoutStage(LayoutImpl),
                },
                log: LogDiag);
        }

        // ─── Debug commit trace (2026-05-15) ──────────────────────────
        // Buffer rempli pendant un commit (NeighborFinder + merger +
        // InsertOMathAt). Reset à chaque start de commit, fired via
        // CommitTraced à la fin → ContextInspectorPane affiche le contenu.
        // null = on n'est PAS dans une commit, Log() bypass le buffer.
        private System.Text.StringBuilder _commitTrace;

        // Données pour bloc SUMMARY (compte des chars à remplacer, voisins
        // mergés). Set au début du commit + dans InserterImpl, lu à la fin
        // pour formatter le résumé human-readable.
        private int _commitOrigAbsStart = -1;
        private int _commitOrigAbsEnd = -1;
        private string _commitOrigSource;
        private int _commitPreInsertAbsStart = -1;
        private int _commitPreInsertAbsEnd = -1;
        // Sources des voisins absorbés — capturées AVANT que InserterImpl
        // ne les retire du store en post-success.
        private string _commitMergedNeighborSources;
        // Bornes internes Word post-SetRange snap (= vraie range à remplacer
        // dans le doc, wrappers OMath inclus). Capturées dans InsertOMathAt.
        private int _commitInternalStart = -1;
        private int _commitInternalEnd = -1;

        /// <summary>Event fired after each commit pipeline run (succès ou
        /// abort), avec la trace concaténée des logs émis pendant le commit.
        /// Consommé par <c>ThisAddIn.ContextInspector</c> pour afficher dans
        /// le pane de debug. Pas de fire si pas d'abonné.</summary>
        public event EventHandler<string> CommitTraced;

        /// <summary>Log instance : <see cref="LogDiag"/> + append au buffer
        /// de trace commit s'il est actif. Permet aux delegate <c>log:</c>
        /// passés à NeighborFinder/IntraOMathsMerger/... d'être capturés
        /// dans la trace sans toucher à leur API.</summary>
        private void Log(string message)
        {
            LogDiag(message);
            var buf = _commitTrace;
            if (buf != null)
            {
                try { lock (buf) buf.AppendLine(message); } catch { }
            }
        }

        /// <summary>Bloc SUMMARY human-readable affiché à la fin du commit
        /// dans l'inspecteur debug. Lit les fields _commitOrig* +
        /// _commitPreInsert* + le ctx final pour décomposer :
        ///  - Current formula (source tapée par l'user)
        ///  - Merged (sources des voisins absorbés par le merger)
        ///  - Final convert (latex + unicodeMath + nb char)
        ///  - Nb char à remplacer dans le doc : currentformula + oMathToMerge = TOTAL
        /// </summary>
        private void EmitCommitSummary(MathCursor.Host.Pipeline.CommitContext ctx)
        {
            Log("");
            Log("==== SUMMARY ====");
            Log($"Current formula  => \"{Preview(_commitOrigSource)}\"");
            string mergedNeighbors = string.IsNullOrEmpty(_commitMergedNeighborSources)
                ? "(none)"
                : "\"" + _commitMergedNeighborSources + "\"";
            Log($"Merged           => {mergedNeighbors}");

            string finalUnicode = "";
            try { finalUnicode = MathCursor.Core.LatexToUnicodeMath.Convert(ctx?.Latex ?? "") ?? ""; } catch { }
            Log($"Final convert    => latex \"{Preview(ctx?.Latex)}\"  unicode \"{Preview(finalUnicode)}\" (nb char = {finalUnicode.Length})");

            Log("");
            Log("Nb char à remplacer dans le doc :");
            int currentChars = (_commitOrigAbsEnd >= 0 && _commitOrigAbsStart >= 0)
                ? (_commitOrigAbsEnd - _commitOrigAbsStart) : -1;
            // Word interne = bornes post-SetRange snap. C'est ÇA que Word va
            // toucher (wrappers OMath inclus). Merger rapporte seulement la
            // largeur d'ancre (souvent 1) ce qui est trompeur — pas affiché.
            int totalCharsInternal = (_commitInternalEnd >= 0 && _commitInternalStart >= 0)
                ? (_commitInternalEnd - _commitInternalStart) : -1;
            int omathCharsInternal = (totalCharsInternal >= 0 && currentChars >= 0) ? (totalCharsInternal - currentChars) : -1;
            Log($"  currentformula => {currentChars}");
            Log($"  oMathToMerge   => {omathCharsInternal}  (range Word interne — wrappers inclus)");
            Log($"  TOTAL          => {totalCharsInternal}  (= ce que SetRange opère réellement)");
        }
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

        // Doc dont on a hooké ContentControlOnExit pour cleanup auto des
        // CCs orphelins. Best-effort single-doc (Phase B). Multi-doc à
        // affiner si besoin.
        private Word.Document _hookedDocForCcExit;

        public void Install()
        {
            if (_installed) return;
            _app.WindowSelectionChange += OnSelectionChange;
            _app.WindowDeactivate += OnWindowDeactivate;
            _app.WindowActivate += OnWindowActivate;

            // Hook cleanup opportuniste : quand le caret quitte un CC,
            // on vérifie qu'il a encore son OMath dedans. Sinon → delete
            // le wrapper. Brief 2026-05-18 §4 (cycle de vie CC).
            try
            {
                _hookedDocForCcExit = _app.ActiveDocument;
                if (_hookedDocForCcExit != null)
                {
                    _hookedDocForCcExit.ContentControlOnExit += OnContentControlExit;
                }
            }
            catch (Exception ex) { LogDiag("install_cc_hook_error: " + ex.Message); }

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
            try { if (_hookedDocForCcExit != null) _hookedDocForCcExit.ContentControlOnExit -= OnContentControlExit; } catch { }
            _hookedDocForCcExit = null;
            try { _pollTimer?.Stop(); } catch { }
            try { _popup?.Close(); } catch { }
            try { _editController?.Close(); } catch { }
            _popup = null;
            _pollTimer = null;
            _installed = false;
        }

        /// <summary>
        /// Hook event-driven : quand le caret quitte un ContentControl,
        /// vérifie qu'il y a toujours une OMath dedans. Sinon → CC orphelin
        /// (vidé par une suppression user ou un Word quirk), on supprime le
        /// wrapper seul. Évite l'accumulation de CCs fantômes.
        /// </summary>
        private void OnContentControlExit(Word.ContentControl cc, ref bool cancel)
        {
            try
            {
                if (cc == null) return;
                if (cc.Title != MathCursor.Host.CCMeta.MCMetaJson.CcTitle) return;
                int omCount = 0;
                try { omCount = cc.Range.OMaths.Count; } catch { }
                if (omCount > 0) return; // CC sain, OMath toujours dedans
                int rs = -1, re = -1;
                try { rs = cc.Range.Start; re = cc.Range.End; } catch { }
                try { cc.Delete(false); } catch { return; }
                LogDiag($"cc_on_exit: orphan MathCursor CC deleted, was at [{rs},{re})");
            }
            catch { /* event handler, jamais propager */ }
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
            _manualTrigger?.Reset();
        }

        private void HidePopupTransient()
        {
            _popup?.HidePopup(resetCaches: false);
            _editController?.HidePopup();
        }

        /// <summary>
        /// Si le caret est actuellement DANS un CC MathCursor (LockContents=true
        /// depuis le fix anti-auto-grow), le sortir vers le côté le plus
        /// proche basé sur <c>_lastCaretPos</c>. Retourne true si éjecté.
        /// </summary>
        public bool EjectCaretFromLockedCcIfAny()
        {
            try
            {
                var sel = _app?.Selection;
                if (sel == null) return false;
                if (sel.Start != sel.End) return false; // skip si sélection range

                Word.ContentControl cc = null;
                try { cc = sel.Range.ParentContentControl; } catch { }
                if (cc == null) return false;
                if (cc.Title != MathCursor.Host.CCMeta.MCMetaJson.CcTitle) return false;

                int ccS = cc.Range.Start;
                int ccE = cc.Range.End;
                int target = (_lastCaretPos >= ccE) ? Math.Max(0, ccS - 1) : ccE + 1;

                var doc = _app.ActiveDocument;
                if (doc != null && target >= doc.Content.End) target = doc.Content.End - 1;
                if (target < 0) target = 0;

                sel.SetRange(target, target);
                LogDiag($"eject_locked_cc: caret {sel.Start} ← from cc=[{ccS},{ccE}) prev={_lastCaretPos}");
                return true;
            }
            catch (Exception ex) { LogDiag("eject_locked_cc_error: " + ex.Message); return false; }
        }

        /// <summary>
        /// Si appuyer Left placerait le caret au bord d'une CC MathCursor,
        /// sélectionne l'OMath entière à la place (comme Word fait pour
        /// les images/shapes inline). L'utilisateur voit la formule
        /// surlignée → peut Delete/Backspace pour supprimer, Enter pour
        /// éditer, ou re-arrow pour collapse et continuer la navigation.
        /// Retourne true si on a sélectionné (consomme la touche).
        /// </summary>
        public bool TrySelectOMathOnLeft()
        {
            try
            {
                var sel = _app?.Selection;
                if (sel == null) return false;
                if (sel.Start != sel.End) return false; // déjà une sélection → laisse Word collapse normalement
                int caret = sel.Start;
                if (caret <= 0) return false;

                var doc = _app.ActiveDocument;
                if (doc == null) return false;
                // Probe sur 2 positions à gauche : le hook tire AVANT que Word
                // bouge le caret. Post-CcSticky le caret est à cc.End+1, donc :
                //  delta=1 → doc.Range(caret-1, caret) = [cc.End, cc.End+1) → null
                //  delta=2 → doc.Range(caret-2, caret-1) = [cc.End-1, cc.End) → DANS la CC ✓
                Word.ContentControl cc = null;
                for (int delta = 1; delta <= 2 && cc == null; delta++)
                {
                    int p = caret - delta;
                    if (p < 0) break;
                    try
                    {
                        var probe = doc.Range(p, p + 1).ParentContentControl;
                        if (probe != null && probe.Title == MathCursor.Host.CCMeta.MCMetaJson.CcTitle)
                            cc = probe;
                    }
                    catch { }
                }
                if (cc == null) return false;

                // Récupère l'OMath dans la CC → sélectionne sa range exact.
                Word.OMath om = null;
                try { foreach (Word.OMath o in cc.Range.OMaths) { om = o; break; } } catch { }
                if (om == null) return false;

                sel.SetRange(om.Range.Start, om.Range.End);
                LogDiag($"select_omath_left: caret {caret} → sélectionne om=[{om.Range.Start},{om.Range.End})");
                return true;
            }
            catch (Exception ex) { LogDiag("select_omath_left_error: " + ex.Message); return false; }
        }

        /// <summary>
        /// Symétrique pour la touche Right : sélectionne l'OMath quand on est
        /// adjacent à une CC MC à droite.
        /// </summary>
        public bool TrySelectOMathOnRight()
        {
            try
            {
                var sel = _app?.Selection;
                if (sel == null) return false;
                if (sel.Start != sel.End) return false;
                int caret = sel.Start;
                var doc = _app.ActiveDocument;
                if (doc == null) return false;
                int docEnd = doc.Content.End;
                if (caret >= docEnd - 1) return false;

                // Probe sur 2 positions à droite (symétrique du Left) :
                //  delta=0 → doc.Range(caret, caret+1)
                //  delta=1 → doc.Range(caret+1, caret+2)
                Word.ContentControl cc = null;
                for (int delta = 0; delta <= 1 && cc == null; delta++)
                {
                    int p = caret + delta;
                    if (p + 1 > docEnd) break;
                    try
                    {
                        var probe = doc.Range(p, p + 1).ParentContentControl;
                        if (probe != null && probe.Title == MathCursor.Host.CCMeta.MCMetaJson.CcTitle)
                            cc = probe;
                    }
                    catch { }
                }
                if (cc == null) return false;

                Word.OMath om = null;
                try { foreach (Word.OMath o in cc.Range.OMaths) { om = o; break; } } catch { }
                if (om == null) return false;

                sel.SetRange(om.Range.Start, om.Range.End);
                LogDiag($"select_omath_right: caret {caret} → sélectionne om=[{om.Range.Start},{om.Range.End})");
                return true;
            }
            catch (Exception ex) { LogDiag("select_omath_right_error: " + ex.Message); return false; }
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
                _manualTrigger?.Reset();
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
            // P11.14 : push la trace engine v2 dans l'inspecteur (= no-op si
            // engine v2 OFF ou pane fermé).
            try
            {
                var trace = _resolver.LastEngineDiagTrace;
                if (!string.IsNullOrEmpty(trace))
                    Globals.ThisAddIn?.PushEngineV2Trace(trace);
            }
            catch { /* inspecteur ne doit jamais propager */ }
            // P32.1 : signale visiblement dans les logs si le legacy a été
            // appelé. En condition normale ce path doit être muet.
            if (_resolver.LastResolveUsedLegacy)
            {
                LogDiag($"[LEGACY-PATH] source=\"{rawSource ?? string.Empty}\" "
                    + $"legacy-calls-total={_resolver.LegacyFallbackCalls}");
            }
            return resolved;
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

        private void OnWindowDeactivate(Word.Document doc, Word.Window wnd)
        {
            HidePopup();
            try { _pollTimer?.Stop(); } catch { }
        }

        private void OnWindowActivate(Word.Document doc, Word.Window wnd)
        {
            try { _pollTimer?.Start(); } catch { }
        }

        /// <summary>
        /// Lit <c>_app.Selection.Start</c> de façon silencieuse :
        /// - Guard <c>Documents.Count &gt; 0</c> pour éviter le boot Word
        ///   (<c>Selection.get</c> jette si aucun doc actif).
        /// - <c>[DebuggerHidden]</c> + <c>[DebuggerStepThrough]</c> empêchent
        ///   VS de signaler les COMException levées dans cette méthode dans
        ///   la fenêtre Output Debug. Crucial pour ne pas polluer le log
        ///   pendant le développement avec des exceptions catchées
        ///   silencieusement (= rien à faire côté user, l'opération est
        ///   no-op en cas d'échec).
        /// </summary>
        [System.Diagnostics.DebuggerHidden]
        [System.Diagnostics.DebuggerStepThrough]
        private bool TryGetCaretSilently(out int caret)
        {
            caret = -1;
            try
            {
                int docsCount = 0;
                try { docsCount = _app.Documents?.Count ?? 0; } catch { return false; }
                if (docsCount <= 0) return false;
                caret = _app.Selection.Start;
                return true;
            }
            catch { return false; }
        }

        /// <summary>Flag du bouton debug : quand true, CheckAndUpdate est
        /// no-op. Permet aux callbacks ribbon de manipuler le doc sans que
        /// le NER tire en parallèle (= évite re-entrancy et cascade d'events
        /// qui crashe Word en cellule de tableau).</summary>
        internal bool DebugInProgress { get; set; }

        private void CheckAndUpdate()
        {
            if (DebugInProgress) return;
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
                if (!TryGetCaretSilently(out int currentCaret)) return;
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
                    // NER input = ce qu'il y a autour du caret entre les
                    // OMaths les plus proches (ou bornes du paragraphe).
                    // Cf. Detection.NerInputWindow.
                    var window = Detection.NerInputWindow.Compute(paragraphText, omathRegions, caretInParagraph);
                    int nerOffset = window.LeftCut;
                    string nerInput = window.Input;
                    LogDiag($"ner_input offset={nerOffset} rightCut={window.RightCut} len={nerInput.Length} omaths={omathRegions.Count} text=\"{nerInput.Replace("\r", "\\r").Replace("\n", "\\n")}\"");

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
                    var filteredZones = Detection.ZoneRefiner.FilterOutOMathOverlap(zones, omathRegions);
                    LogDiag($"zones={zones.Count} → filtered={filteredZones.Count} (omath_overlap dropped={zones.Count - filteredZones.Count})");

                    // Push live trace au pane debug — visible à chaque tick.
                    try
                    {
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine($"⟳ NER tick @ {DateTime.Now:HH:mm:ss.fff}");
                        sb.AppendLine();
                        sb.AppendLine($"Paragraphe masqué (len={paragraphText.Length}, caret={caretInParagraph}):");
                        sb.AppendLine($"  \"{paragraphText.Replace("\r", "\\r").Replace("\n", "\\n")}\"");
                        sb.AppendLine();
                        sb.AppendLine($"OMath regions ({omathRegions.Count}) :");
                        foreach (var (s, e) in omathRegions) sb.AppendLine($"  [{s},{e}) string-pos");
                        sb.AppendLine();
                        sb.AppendLine($"nerOffset = {nerOffset}");
                        sb.AppendLine($"nerInput  (len={nerInput.Length}) :");
                        sb.AppendLine($"  \"{nerInput.Replace("\r", "\\r").Replace("\n", "\\n")}\"");
                        sb.AppendLine();
                        sb.AppendLine($"Zones détectées : {zones.Count}  (filtrées : {filteredZones.Count})");
                        foreach (var z in zones)
                            sb.AppendLine($"  [{z.Start},{z.End}) conf={z.Confidence:F2} text=\"{z.Text}\"");
                        Globals.ThisAddIn?.PushNerLive(sb.ToString());
                    }
                    catch { /* debug pane, jamais propager */ }

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
            var target = Detection.ZoneRefiner.PickNearestZone(zones, caretInParagraph, out int dist);
            LogDiag($"pick caret={caretInParagraph} target={(target == null ? "null" : target.ToString())} dist={dist}");
            if (target == null) { HidePopupTransient(); return; }
            if (dist > 0)
            {
                // Tolérance : si le caret est pile sur le paragraph mark
                // (`\r`, dist=1), c'est qu'on est en fin de ¶ — on ne peut
                // pas être plus près. On laisse la popup ouverte pour
                // permettre le commit (ex: tape "=1" puis Enter pour
                // merger avec OMath au-dessus).
                bool caretOnParaMark = (dist == 1)
                    && target.End >= 0
                    && _lastParagraph != null
                    && target.End < _lastParagraph.Length
                    && _lastParagraph[target.End] == '\r';

                if (!caretOnParaMark)
                {
                    if (!ShouldExtendZoneForward(target))
                    {
                        LogDiag($"hide_reason=zone_complete_no_extend (target end='{target.Text}')");
                        HidePopupTransient();
                        return;
                    }
                    target = Detection.ZoneRefiner.TryExtendForwardWhitespace(_lastParagraph, target, caretInParagraph);
                    if (target == null || (caretInParagraph - target.End) > 0)
                    {
                        LogDiag("hide_reason=caret_still_outside_after_forward_extend");
                        HidePopupTransient();
                        return;
                    }
                    LogDiag($"forward_extended target={target}");
                }
                else
                {
                    LogDiag($"caret on \\r at dist=1, keep popup open (zone end at end of ¶)");
                }
            }

            // Le NER rate parfois des mots-clés math en début de zone (lim, sqrt, etc.)
            // On tente une extension arrière : si le mot immédiatement avant la zone est
            // un keyword math connu, on l'absorbe.
            target = Detection.ZoneRefiner.ExtendBackwardWithKeyword(_lastParagraph, target, _vocab);
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

            // Guard P9g (2026-05-21) : ne hide que si lattice ET patterns sont vides.
            // Patterns postfix comme PrimedDerivative (f') produisent une completion
            // sans que le lattice ne sache tokenizer le source brut. Cf. ADR
            // 2026-05-21-Fix-popup-guard-pattern-completions.
            bool hasPatterns = resolved.PatternCompletions != null
                && resolved.PatternCompletions.Count > 0;
            if (string.IsNullOrEmpty(resolved.TopLatex) && !hasPatterns)
            {
                HidePopupTransient();
                return;
            }

            // Construit la zone unifiée (paraStart interne + bornes string-pos
            // + snapshot text + OMaths regions). La traduction string→interne
            // (= positions absolues Word incluant wrappers cachés) est faite
            // dans ShowPopup et au commit via ZoneSpan.TryToInternal. Cf. ADR
            // 2026-05-23-Refactor-zonespan-popup-commit-coords.
            var zone = new MathCursor.Host.Detection.ZoneSpan(
                paragraphAbsStart, target.Start, target.End,
                _lastParagraph ?? "", omathRegions);

            // Anti-spam Esc : si l'utilisateur a déjà fermé la popup pour
            // CETTE zone exacte, on ne re-spawn pas. La comparaison est en
            // coords absolues internes (résolues par TryToInternal). Le flag
            // est reset dès que la zone change.
            int probeAbsStart, probeAbsEnd;
            zone.TryToInternal(_app.ActiveDocument, out probeAbsStart, out probeAbsEnd);
            if (probeAbsStart == _dismissedZoneStart && probeAbsEnd == _dismissedZoneEnd)
                return;
            _dismissedZoneStart = -1;
            _dismissedZoneEnd = -1;

            int rawLen = target.Text?.Length ?? 0;
            ShowPopup(resolved, zone, rawLen, target.Text ?? "");

            // Initialise l'état d'extension itérative depuis la zone NER
            // courante. Permet à Ctrl+Espace suivants d'étendre cette zone
            // (cf. ADR 29-04 iterative-zone-expansion). Sans ce hook,
            // l'extension itérative ne marche que pour les popups venues
            // du manual trigger (TriggerManual), pas du polling NER.
            _manualTrigger.InitFromAutoZone(zone);
        }

        /// <summary>
        /// Trigger explicite (Ctrl+Espace). Délégué à <see cref="ManualTrigger.ManualTriggerController"/>.
        /// </summary>
        public void TriggerManual()
        {
            LogDiag("[CTRL+SPACE] TriggerManual() entered from keyboard");
            _manualTrigger.Trigger();
        }

        // MathPrefixKeywords déplacé dans Host/Detection/ZoneRefiner.cs (P2.14).

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
                if (_currentZoneSpan == null || _currentZoneSpan.IsEmpty) return;
                var latex = _popup.CurrentFinalLatex ?? "";
                if (string.IsNullOrWhiteSpace(latex)) return;
                CommitLatexAndOMath(latex, _currentZoneSpan.Text);
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
                NerText = snap?.SourceText ?? (_currentZoneSpan?.Text ?? ""),
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

        private void ShowPopup(ResolvedZone resolved, MathCursor.Host.Detection.ZoneSpan zone, int rawZoneLength, string debugText = "")
        {
            if (_popup == null)
            {
                _popup = new SuggestionPopupWindow();
                _popup.ReportRequested += OnReportRequested;
                _popup.SourceMutationRequested += OnSourceMutationRequested;
                _popup.CommitRequested += OnPopupCommitRequested;
            }

            _currentZoneSpan = zone;

            // Traduit string→interne pour le positioning popup + anti-spam +
            // edit-mode-entry-check. Échec gracieux : fallback sur le mix
            // (= ancien comportement, OK sur ¶ vierge d'OMath).
            int absStart = -1, absEnd = -1;
            if (zone != null)
                zone.TryToInternal(_app.ActiveDocument, out absStart, out absEnd);

            // Repositionnement : seulement si nouvelle zone, sinon on garde la
            // position actuelle (clic dans la popup → Word perd focus, GetCaretPos
            // rate et renverrait fallback 200,200).
            bool shouldReposition =
                !_popup.IsVisible
                || absStart != _lastZoneAbsStart || absEnd != _lastZoneAbsEnd;

            double popupX, popupY;
            if (shouldReposition)
            {
                var pos = Caret.CaretScreenPositionReader.Read();
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
            _lastActionTracker.RecordPopupOpen(zone?.Text ?? "", resolved?.TopLatex);

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
            // Pas de fallback activeAltIdxFromCaller : le ZoneResolver annote
            // déjà AppliedAltIdx sur chaque match via Resolve(...). La popup
            // lit cette annotation pour filtrer l'alt active. Cf. refacto
            // désambig 2026-05-21 (audit B — suppression FindActiveAltIdxForRule).
            // P7c (2026-05-21) : transmettre les PatternCompletion[] produites
            // par le ZoneResolver (P7a) à la popup. Pour l'instant pass-through
            // pour log diag — le rendering UX sera ajusté en P7d après test
            // manuel Word. Cf. ADR 2026-05-21-Feat-popup-pattern-completion-spike.
            _popup.Show(resolved.TopLatex, ruleId, alts, spotStart, spotEnd,
                resolved.AllMatches, popupX, popupY, debugText,
                resolved.PatternCompletions);
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
                // Pas de bail sur mutation == null : seuls ruleId + altIdx
                // comptent. Le ZoneResolver.ApplyPreferences trouve l'alt
                // sur la source originale et applique sa mutation native.
                // Le paramètre `mutation` reste pour rétro-compat (signature
                // event) mais n'est plus utilisé ici.
                if (string.IsNullOrEmpty(ruleId)) return;
                // altIdx == AltIdxRevert (-1) = clic sur defaultLatex brut
                // dans la popup → retire la pref pour cette rule (et donc
                // re-resolve repart de la source originale sans mutation).
                if (altIdx == MathCursor.Core.Resolution.SpanOverride.AltIdxRevert)
                    _resolver.RemovePreference(ruleId);
                else
                    _resolver.AddPreference(ruleId, altIdx);

                var src = _currentZoneSpan?.Text ?? string.Empty;
                var resolved = ResolveWithContext(src);
                LogDiag($"pref applied rule=\"{ruleId}\" altIdx={altIdx} src=\"{src}\" → muted=\"{resolved.MutedSource}\" incomplete={resolved.IsIncomplete}");

                // Auto-commit retiré (29-04). Avec la décomposition modulaire
                // de forall (Const " \forall " seul), la mutation V→forall sur
                // `V` produit `\forall ` qui a IsIncomplete=false alors que
                // sémantiquement il manque var et ensemble. L'auto-commit
                // "volait" la frappe de l'utilisateur. Désormais l'utilisateur
                // commit toujours via flèche bas + Enter, comportement prévisible.
                ShowPopup(resolved, _currentZoneSpan, src.Length, debugText: resolved.MutedSource);
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
        ///
        /// <para>Garde-fou contre le bug 2026-05-13 (perte formule après
        /// cross-merge + Escape) : si le ¶ contient une OMath ou trop de
        /// texte pour être un simple marker, on refuse le strip — la
        /// méthode est censée nettoyer un marker auto-injecté, pas effacer
        /// du contenu utilisateur. Cf.
        /// <see cref="ListModeStripGuard.CanStripMarkerFromLine"/> +
        /// ADR 2026-05-13-Fix-list-mode-strip-guard-omath.</para>
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
                int contentLength = contentEnd - contentStart;

                int omathsCount = 0;
                try { omathsCount = paraRange.OMaths?.Count ?? 0; } catch { }

                if (!ListModeStripGuard.CanStripMarkerFromLine(omathsCount, contentLength))
                {
                    LogDiag($"list_mode: strip REFUSED for ¶[{contentStart},{contentEnd}] " +
                            $"omaths={omathsCount} contentLen={contentLength} — guard preserve user content");
                    return;
                }

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
            if (_currentZoneSpan == null || _currentZoneSpan.IsEmpty) return false;

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

            return CommitLatexAndOMath(latex, _currentZoneSpan?.Text ?? "");
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
        /// Résultat de l'orchestration des mergers (intra puis cross-line
        /// cascade). Si aucun merger n'a matché, contient les bornes/source/
        /// latex d'entrée inchangés et <c>WasCrossMerge=false</c>.
        /// </summary>
        private readonly struct MergeOutcome
        {
            public int AbsStart { get; }
            public int AbsEnd { get; }
            public string Source { get; }
            public string Latex { get; }
            public bool WasCrossMerge { get; }
            public string CrossMergeMarker { get; }

            public MergeOutcome(int absStart, int absEnd, string source, string latex,
                bool wasCrossMerge, string crossMergeMarker)
            {
                AbsStart = absStart;
                AbsEnd = absEnd;
                Source = source;
                Latex = latex;
                WasCrossMerge = wasCrossMerge;
                CrossMergeMarker = crossMergeMarker;
            }

            public static MergeOutcome Identity(int absStart, int absEnd, string source, string latex)
                => new MergeOutcome(absStart, absEnd, source, latex, false, null);
        }

        /// <summary>
        /// Orchestre les mergers : intra-¶ d'abord (LaTeX-preserving via
        /// <c>cc.Tag.Latex</c>), puis cross-line cascade (markers align ou
        /// cases <c>{</c>). En mode édition, no-op. Renvoie l'identité si
        /// aucun merger n'a matché.
        /// </summary>
        private MergeOutcome ApplyMergers(int absStart, int absEnd, string source,
            string latex, EquationHandle editingHandle)
        {
            if (editingHandle != null)
                return MergeOutcome.Identity(absStart, absEnd, source, latex);

            // 1. Intra-¶ (Phase B revival, ADR 2026-05-18). Si la source
            //    commence par marker (=, <=>, =>, {) et qu'une OMath voisine
            //    existe à gauche, fusionne en préservant son LaTeX.
            var intra = TryIntraMerge(absStart, absEnd, source, latex);
            if (intra != null)
                return new MergeOutcome(
                    intra.AbsStart, intra.AbsEnd,
                    intra.MergedSource, intra.MergedLatex,
                    wasCrossMerge: false, crossMergeMarker: null);

            // 2. Cross-line cascade (ADR 2026-04 + ADR 04-05 + ADR 05-05).
            //    MarkerChain pour align, CasesChain pour {. Re-resolve via
            //    ZoneResolver pour générer le LaTeX multi-ligne (eqArray/cases).
            var cross = TryCrossLineCascade(absStart, absEnd, source);
            if (cross != null)
            {
                var resolved = ResolveWithContext(cross.MergedSource, cross.MergedSidecar);
                if (resolved != null && !string.IsNullOrEmpty(resolved.TopLatex))
                {
                    string marker = ExtractMarkerFromMergedSource(cross.MergedSource);
                    LogDiag(string.Format(
                        "cross_cascade applied: zone [{0},{1}) → [{2},{3}), source=\"{4}\", latex=\"{5}\", marker=\"{6}\"",
                        absStart, absEnd, cross.AbsStart, cross.AbsEnd,
                        Preview(cross.MergedSource), Preview(resolved.TopLatex), marker));
                    return new MergeOutcome(
                        cross.AbsStart, cross.AbsEnd,
                        cross.MergedSource, resolved.TopLatex,
                        wasCrossMerge: true, crossMergeMarker: marker);
                }
                LogDiag("cross_cascade: re-resolve failed, skip");
            }

            return MergeOutcome.Identity(absStart, absEnd, source, latex);
        }

        /// <summary>
        /// Intra-¶ merger (LaTeX-preserving). Construit les helpers puis
        /// appelle <see cref="MathCursor.Host.Merging.IntraOMathsMerger.TryMergeWithLeft"/>.
        /// Retourne <c>null</c> si pas de match ou erreur.
        /// </summary>
        private MathCursor.Host.Merging.MergeResult TryIntraMerge(
            int absStart, int absEnd, string source, string latex)
        {
            try
            {
                var finder = new MathCursor.Host.Merging.NeighborFinder(
                    () => _app.ActiveDocument, LogDiag);
                var merger = new MathCursor.Host.Merging.IntraOMathsMerger(
                    finder,
                    () => _resolver.BuildSidecar(),
                    h => GetSidecarForHandle(h),
                    LogDiag);
                var result = merger.TryMergeWithLeft(absStart, absEnd, source, latex);
                if (result == null || string.IsNullOrEmpty(result.MergedLatex)) return null;
                LogDiag(string.Format(
                    "merge_left applied: zone [{0},{1}) → [{2},{3}), source=\"{4}\", latex=\"{5}\"",
                    absStart, absEnd, result.AbsStart, result.AbsEnd,
                    Preview(result.MergedSource), Preview(result.MergedLatex)));
                return result;
            }
            catch (Exception ex) { LogDiag("merge_left_call_error: " + ex.Message); return null; }
        }

        /// <summary>
        /// Cross-line cascade : tente le MarkerChain (align) puis le
        /// CasesChain ({). Premier match wins. Retourne <c>null</c> si aucun
        /// n'a matché ou erreur.
        /// </summary>
        private MathCursor.Host.Merging.MergeResult TryCrossLineCascade(
            int absStart, int absEnd, string source)
        {
            try
            {
                var probe = new MathCursor.Host.Merging.ParagraphCascadeProbe(LogDiag);
                Func<MathCursor.Core.Resolution.ResolutionSidecar> popupSc =
                    () => _resolver.BuildSidecar();
                Func<string, MathCursor.Core.Resolution.ResolutionSidecar> handleSc =
                    h => GetSidecarForHandle(h);

                var markerChain = new MathCursor.Host.Merging.MarkerChainCascadeMerger(
                    () => _app.ActiveDocument, probe, popupSc, handleSc, LogDiag);
                var result = markerChain.TryMerge(absStart, absEnd, source);
                if (result != null && !string.IsNullOrEmpty(result.MergedSource)) return result;

                var casesChain = new MathCursor.Host.Merging.CasesChainCascadeMerger(
                    () => _app.ActiveDocument, probe, popupSc, handleSc, LogDiag);
                result = casesChain.TryMerge(absStart, absEnd, source);
                if (result != null && !string.IsNullOrEmpty(result.MergedSource)) return result;

                return null;
            }
            catch (Exception ex) { LogDiag("cross_cascade_call_error: " + ex.Message); return null; }
        }

        /// <summary>
        /// Corps du commit (séparé de l'enveloppe ScreenUpdating). Cf.
        /// <see cref="CommitLatexAndOMath"/> pour le wrapper.
        ///
        /// <para>Pipeline du commit (Phase 3b — ADR
        /// 2026-05-06-Meta-l4-pipeline-and-session). Le flow se lit en 4
        /// étapes :</para>
        /// <list type="number">
        /// <item>Initial sidecar (edit mode : merge stored + popup).</item>
        /// <item>Traduction string-pos → Word interne via <see cref="Detection.ZoneSpan.TryToInternal"/>.</item>
        /// <item>Orchestration mergers via <see cref="ApplyMergers"/> (intra
        /// puis cross-line cascade).</item>
        /// <item><see cref="MathCursor.Host.Pipeline.CommitPipeline.Run"/> :
        /// Resolver → Snapshot → Inserter → Store → Layout (les stages
        /// délèguent à <see cref="InserterImpl"/> / <see cref="LayoutImpl"/>).</item>
        /// </list>
        /// </summary>
        private bool CommitLatexAndOMathCore(string latex, string source)
        {
            // 1. Initial sidecar. Edit mode : pre-load stored + popup mergé
            //    (fix canary 4) sinon le revert d'OMath multi-ligne avec vec
            //    perd ses désambiguïsations. Last-write-wins via ZoneResolver.
            var initialSidecar = MathCursor.Core.Resolution.ResolutionSidecar.Empty;
            var editingHandle = _editController?.CurrentEditingHandle;
            if (editingHandle != null)
            {
                initialSidecar = MathCursor.Core.Resolution.SidecarMerger.Merge(
                    new[]
                    {
                        GetSidecarForHandle(editingHandle.Id),
                        _resolver.BuildSidecar(),
                    },
                    new[] { 0, 0 });
            }

            // 2. Traduction string-pos → Word interne via ZoneSpan unifié.
            //    En mode édition, on bypass la traduction (les bornes du
            //    handle d'édition sont déjà internes via TryEnterEditMode).
            //    Sinon, ZoneSpan.TryToInternal itère paragraph.Range.Characters
            //    pour compter les wrappers structurels invisibles. Cf. ADR
            //    2026-05-23-Refactor-zonespan-popup-commit-coords.
            int translatedStart, translatedEnd;
            if (editingHandle != null)
            {
                translatedStart = _lastZoneAbsStart;
                translatedEnd = _lastZoneAbsEnd;
            }
            else if (_currentZoneSpan != null)
            {
                _currentZoneSpan.TryToInternal(_app.ActiveDocument, out translatedStart, out translatedEnd);
            }
            else
            {
                translatedStart = _lastZoneAbsStart;
                translatedEnd = _lastZoneAbsEnd;
            }

            // 3. Orchestration mergers (intra puis cross-line cascade).
            var outcome = ApplyMergers(translatedStart, translatedEnd, source, latex, editingHandle);

            // 4. CommitContext + run pipeline.
            var ctx = new MathCursor.Host.Pipeline.CommitContext(
                absStart: outcome.AbsStart,
                absEnd: outcome.AbsEnd,
                source: outcome.Source,
                latex: outcome.Latex,
                sidecar: initialSidecar,
                editingHandle: editingHandle,
                wasCrossParagraphMerge: outcome.WasCrossMerge,
                crossMergeMarker: outcome.CrossMergeMarker);

            // Trace commit (debug inspecteur) : ouvre un buffer, capture les
            // logs des stages + InsertOMathAt, fire CommitTraced à la fin.
            _commitTrace = new System.Text.StringBuilder();
            _commitOrigAbsStart = outcome.AbsStart;
            _commitOrigAbsEnd = outcome.AbsEnd;
            _commitOrigSource = source;
            _commitPreInsertAbsStart = -1;
            _commitPreInsertAbsEnd = -1;
            _commitInternalStart = -1;
            _commitInternalEnd = -1;
            Log($"=== COMMIT @ {DateTime.Now:HH:mm:ss.fff} ===");
            string spanDebug = _currentZoneSpan == null
                ? "stringPos=(no-span) paraStart=(no-span)"
                : $"stringPos=[{_currentZoneSpan.StringStart},{_currentZoneSpan.StringEnd}) paraStart={_currentZoneSpan.ParagraphAbsStart}";
            Log($"INPUT  {spanDebug} → internal=[{outcome.AbsStart},{outcome.AbsEnd}) source=\"{Preview(source)}\" latex=\"{Preview(latex)}\" editing={(editingHandle != null ? editingHandle.Id : "no")}");

            // UndoRecordScope conservé : sans, le BuildUp crée une entrée
            // undo séparée du TypeText (1 Ctrl+Z annule le BuildUp, 2e
            // annule le TypeText). Avec le wrapper, tout le pipeline est
            // groupé en 1 seul undo nommé « Convertir formule ».
            // Cf. ADR 2026-05-11-Fix-commit-grouped-in-single-undo-record.
            using (var _undoScope = new UndoRecordScope(_app, "Convertir formule"))
            {
                try
                {
                    ctx = _commitPipeline.Run(ctx);
                }
                catch (Exception ex) { Log("commit_pipeline_error: " + ex.Message); }
            }

            // Émission de la trace au pane debug.
            try
            {
                EmitCommitSummary(ctx);
                Log($"FINAL  absStart={ctx?.AbsStart} absEnd={ctx?.AbsEnd} aborted={ctx?.IsAborted} newHandle={(ctx?.NewHandle?.Id ?? "null")}");
                var traceText = _commitTrace?.ToString() ?? string.Empty;
                _commitTrace = null;
                CommitTraced?.Invoke(this, traceText);
            }
            catch { _commitTrace = null; }

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
                var popupSidecar = _resolver.BuildSidecar();
                if (popupSidecar != null && !popupSidecar.IsEmpty)
                    PropagateCommittedPinsToParagraphHistory(popupSidecar);
                else if (ctx?.Sidecar != null && !ctx.Sidecar.IsEmpty)
                    PropagateCommittedPinsToParagraphHistory(ctx.Sidecar);
            }
            catch { }

            // Reset état
            _currentZoneSpan = null;
            _lastZoneAbsStart = -1;
            _lastZoneAbsEnd = -1;
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
                Log($"merge: {ctx.RemovedHandles.Count} OMath(s) absorbés range=[{ctx.AbsStart},{ctx.AbsEnd}] mergedSource=\"{Preview(ctx.Source)}\" latex=\"{Preview(ctx.Latex)}\" handles=[{string.Join(",", ctx.RemovedHandles)}]");
            }
            else
            {
                Log($"InserterImpl: no neighbors absorbed by merger — bounds=[{ctx.AbsStart},{ctx.AbsEnd}] source=\"{Preview(ctx.Source)}\" latex=\"{Preview(ctx.Latex)}\"");
            }

            int replaceStart = ctx.AbsStart;
            // Capture pour le bloc SUMMARY (bornes post-merge, pré-insert).
            _commitPreInsertAbsStart = ctx.AbsStart;
            _commitPreInsertAbsEnd = ctx.AbsEnd;

            // Capture des sources voisins AVANT InsertOMathAt (qui supprime
            // les handles du store en post-success → trop tard pour SUMMARY).
            _commitMergedNeighborSources = null;
            // SUMMARY : pour les sources voisines, on n'a plus de store —
            // elles sont déjà dans ctx.Source (mergedSource). Pas besoin de
            // retrieve séparé.
            _commitMergedNeighborSources = (ctx.RemovedHandles != null && ctx.RemovedHandles.Count > 0)
                ? $"({ctx.RemovedHandles.Count} handle(s) absorbé(s), sources mergées dans ctx.Source)"
                : null;

            var (newStart, newEnd, newHandle) = InsertOMathAt(ctx.AbsStart, ctx.AbsEnd, ctx.Latex, ctx.Source, ctx.RemovedHandles);
            if (newEnd <= newStart)
            {
                Log($"commit ABORTED latex=\"{ctx.Latex}\" — OMath build failed, doc intact (no pre-mutation)");
                return ctx.WithAbort();
            }

            // Cleanup sidecar in-memory uniquement. Les CCs des OMaths
            // absorbées ont été supprimées par sel.Delete dans InsertOMathAt.
            if (ctx.RemovedHandles != null && ctx.RemovedHandles.Count > 0)
            {
                foreach (var h in ctx.RemovedHandles)
                {
                    _handleRegistry.Forget(h);
                }
            }

            var withBounds = ctx.WithInsertedBounds(newStart, newEnd, replaceStart);
            return newHandle != null ? withBounds.WithNewHandle(new EquationHandle(newHandle)) : withBounds;
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
                // Inject a échoué (typiquement Word refuse les ¶ marks dans
                // une OMath). Reset COMPLET du list_mode (pas juste l'ancre) :
                // si on laisse le state machine actif, ExitListMode pourra
                // déclencher StripListModeMarkerFromCurrentLine plus tard sur
                // un ¶ qui n'a pas de marker injecté → risque d'effacer du
                // contenu user (bug 2026-05-13 perte formule).
                // Cf. ADR 2026-05-13-Fix-list-mode-strip-guard-omath.
                LogDiag("list_mode_inject_error: " + ex.Message + " — list_mode reset to inactive");
                _listMode.Reset();
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
            // Strict ' '/'\t' uniquement (pas char.IsWhiteSpace) : l'ancre
            // d'une OMath buildup renvoie un caractère Unicode que
            // char.IsWhiteSpace considère comme whitespace, ce qui faisait
            // trimmer l'OMath voisine et produire le bug f(x)F(x)=1
            // (2026-05-15). Aligne sur NeighborFinder.IsSingleSpaceAt.
            try
            {
                var t = doc.Range(pos, pos + 1).Text ?? "";
                return t.Length > 0 && (t[0] == ' ' || t[0] == '\t');
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
        /// Remplace le range [absStart, absEnd) du document par un OMath construit
        /// à partir du LaTeX fourni. Word's BuildUp ne parse pas le LaTeX nativement,
        /// on convertit donc d'abord en UnicodeMath. Enveloppe ensuite l'OMath
        /// dans un <c>ContentControl</c> MathCursor + Tag JSON
        /// (<see cref="MathCursor.Host.CCMeta.MCMeta"/>) pour le backlink O(1).
        /// Retourne (newStart, newEnd) = bornes de l'OMath ET le handleId
        /// fraîchement généré (mémorisé dans le Tag, clé du registry sidecar).
        /// </summary>
        ///
        /// <remarks>
        /// Entrée de test pour <see cref="MathCursor.Host.Debug.WordScenarioRunner"/> :
        /// expose <c>InsertOMathAt</c> sans passer par le pipeline NER+popup.
        /// </remarks>
        internal (int newStart, int newEnd, string newHandle) InsertOMathForScenarioTest(
            int absStart, int absEnd, string latex, string source)
            => InsertOMathAt(absStart, absEnd, latex, source, null);

        /// <summary>
        /// Entrée de test qui passe par <see cref="ApplyMergers"/> AVANT
        /// <see cref="InsertOMathAt"/> — couvre les scenarios cross-merge
        /// (chaînes équivalences align*, systèmes cases multi-ligne). Pas
        /// de NER, pas de popup, pas de store/layout. Sidecar.Empty.
        /// </summary>
        internal (int newStart, int newEnd, string newHandle) CommitWithMergersForScenarioTest(
            int absStart, int absEnd, string latex, string source)
        {
            var outcome = ApplyMergers(absStart, absEnd, source, latex, editingHandle: null);
            return InsertOMathAt(outcome.AbsStart, outcome.AbsEnd, outcome.Latex, outcome.Source, null);
        }

        /// <summary>
        /// Entrée de test qui invoque le <c>LayoutFinalizer</c> pour le cas
        /// cases single-line (= reproduit la branche IsCasesLatex de
        /// <see cref="LayoutImpl"/>). À appeler APRÈS un commit cases pour
        /// déclencher la création du ¶ vide d'atterrissage (ex. en cellule
        /// de tableau).
        /// </summary>
        internal void FinalizeLayoutForCasesScenarioTest(int omPosition)
        {
            try
            {
                var doc = _app.ActiveDocument;
                if (doc == null) return;
                bool didCreate;
                int caretPos = _layoutFinalizer.AppendEmptyParagraphAfterOMath(doc, omPosition, out didCreate);
                if (caretPos >= 0) _layoutFinalizer.SetCaretAtPosition(caretPos);
            }
            catch (Exception ex) { LogDiag("finalize_layout_test_error: " + ex.Message); }
        }

        private (int newStart, int newEnd, string newHandle) InsertOMathAt(int absStart, int absEnd, string latex,
            string source,
            System.Collections.Generic.IReadOnlyList<string> absorbedHandles = null)
        {
            // ┌─────────────────────────────────────────────────────────────┐
            // │ RECETTE ROBUSTE 2026-05-15 — positions internes Word        │
            // │                                                             │
            // │ Word reporte OMath.Range.Start/End en positions « visibles »│
            // │ mais en interne stocke des wrapper chars cachés autour des  │
            // │ OMaths. SetRange snap silencieusement aux bornes internes,  │
            // │ TypeText laisse parfois survivre une OMath au début de la   │
            // │ sélection. Bug f(x)F(x)=1 et 𝐴A=1.                          │
            // │                                                             │
            // │ Solution générique (pas de if "est-ce une OMath") :         │
            // │   1. Normaliser absStart/absEnd via SetRange + readback     │
            // │      → on connaît les vraies bornes internes Word           │
            // │   2. SetRange sur ces bornes internes                       │
            // │   3. sel.Delete() explicite → force la suppression incluant │
            // │      OMaths (TypeText seul ne les supprime pas toujours)    │
            // │   4. TypeText sur range vide → comportement linéaire        │
            // │   5. OMaths.Add + BuildUp + Justification = Left            │
            // │   6. NE PAS toucher au caret — Word le place                │
            // │                                                             │
            // │ + suspendre NER pendant l'opération (DebugInProgress).      │
            // └─────────────────────────────────────────────────────────────┘
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var doc = _app.ActiveDocument;
            Log($"InsertOMathAt: IN  absStart={absStart} absEnd={absEnd} latex=\"{Preview(latex)}\" absorbedHandles=[{(absorbedHandles == null ? "" : string.Join(",", absorbedHandles))}]");
            if (doc == null) { Log("InsertOMathAt: no active document → bail"); return (absStart, absEnd, null); }

            // Clamp + trim whitespaces aux bords.
            int docStart = doc.Content.Start;
            int docEnd = doc.Content.End;
            if (absStart < docStart) absStart = docStart;
            if (absEnd > docEnd) absEnd = docEnd;
            if (absEnd <= absStart) return (absStart, absEnd, null);
            int beforeTrimStart = absStart, beforeTrimEnd = absEnd;
            while (absStart < absEnd && IsWhitespaceCharAt(doc, absStart)) absStart++;
            while (absEnd > absStart && IsWhitespaceCharAt(doc, absEnd - 1)) absEnd--;
            if (beforeTrimStart != absStart || beforeTrimEnd != absEnd)
                Log($"InsertOMathAt: TRIM ws [{beforeTrimStart},{beforeTrimEnd}) → [{absStart},{absEnd})");
            if (absEnd <= absStart) { Log("InsertOMathAt: range empty after trim → bail"); return (absStart, absEnd, null); }

            // LaTeX → UnicodeMath (= format natif BuildUp).
            string unicodeMath;
            try { unicodeMath = MathCursor.Core.LatexToUnicodeMath.Convert(latex); }
            catch (Exception ex) { Log("insert_l2um_error: " + ex.Message); return (absStart, absEnd, null); }
            if (string.IsNullOrEmpty(unicodeMath)) { Log("InsertOMathAt: unicodeMath empty → bail"); return (absStart, absEnd, null); }
            Log($"InsertOMathAt: unicodeMath=\"{Preview(unicodeMath)}\" (len={unicodeMath.Length})");

            // Suspend NER pendant l'opération.
            bool prevDebug = DebugInProgress;
            DebugInProgress = true;
            try
            {
                var sel = _app.Selection;
                if (sel == null) { Log("InsertOMathAt: sel null → bail"); return (absStart, absEnd, null); }

                // 1. Normalisation des bornes en positions internes Word.
                int internalStart, internalEnd;
                try
                {
                    sel.SetRange(absStart, absStart);
                    internalStart = sel.Start;
                    sel.SetRange(absEnd, absEnd);
                    internalEnd = sel.Start;
                    Log($"InsertOMathAt: NORMALIZE [{absStart},{absEnd}) → [{internalStart},{internalEnd})");
                }
                catch (Exception ex) { Log("insert_normalize_error: " + ex.Message); return (absStart, absEnd, null); }

                // 2. Cleanup structurel de la plage via ZoneCleaner (CCs +
                //    OMaths résiduelles + plain text). Indispensable quand
                //    la plage englobe une CC voisine (cas merger left) :
                //    sel.Delete seul laisse la CC vide → placeholder Word.
                int afterCleanupPos;
                try
                {
                    afterCleanupPos = MathCursor.Host.ZoneCleaner.ClearZone(
                        doc, internalStart, internalEnd, Log);
                    Log($"InsertOMathAt: ZoneCleaner cleared [{internalStart},{internalEnd}) → pos={afterCleanupPos}");
                }
                catch (Exception ex) { Log("insert_clearzone_error: " + ex.Message); return (absStart, absEnd, null); }

                // 3. SetRange collapsed sur la position post-cleanup.
                try
                {
                    sel.SetRange(afterCleanupPos, afterCleanupPos);
                    _commitInternalStart = sel.Start;
                    _commitInternalEnd = sel.End;
                    Log($"InsertOMathAt: SetRange collapsed @ {afterCleanupPos}, sel=[{sel.Start},{sel.End})");
                }
                catch (Exception ex) { Log("insert_setrange_error: " + ex.Message); return (absStart, absEnd, null); }

                // 3b. Détection liste (numérotée, puce, outline). En liste,
                //     deux comportements diffèrent (cf. bug 2026-05-20) :
                //     - Font.Hidden sur le ZWSP est appliqué APRÈS cc.Add (sinon
                //       Word foire le wrap du run vanish → SDT vide + placeholder).
                //     - DecideOMathTyping force Inline (pas de promotion display
                //       qui sortirait la formule de la ligne de bullet).
                bool isInList = false;
                try
                {
                    var listFormat = doc.Range(afterCleanupPos, afterCleanupPos).Paragraphs[1].Range.ListFormat;
                    isInList = listFormat != null && listFormat.ListType != Word.WdListType.wdListNoNumbering;
                    if (isInList) Log($"InsertOMathAt: liste détectée (type={listFormat.ListType}) → Inline forcé + Font.Hidden post-cc.Add");
                }
                catch (Exception exL) { Log("insert_list_probe_error: " + exL.Message); }

                // 4. TypeText ZWSP en plain text. Font.Hidden appliqué tout
                //    de suite HORS liste (comportement validé) ; APRÈS cc.Add
                //    EN liste (workaround Word bug, cf. 3b).
                int caretBeforeZwsp = sel.Start;
                try { sel.TypeText("​"); }
                catch (Exception ex) { Log("insert_zwsp_typetext_error: " + ex.Message); return (absStart, absEnd, null); }
                int zwspStart = caretBeforeZwsp;
                int zwspEnd = sel.Start;
                Log($"InsertOMathAt: ZWSP typed at {zwspStart}, sel=[{sel.Start},{sel.End})");
                if (!isInList)
                {
                    try { doc.Range(zwspStart, zwspEnd).Font.Hidden = -1; } catch { }
                }

                // 5-6. Produit l'OMath en OMML (structure native) inséré
                //      CHIRURGICALEMENT sur une range placeholder 1-char après
                //      le ZWSP — JAMAIS sur le ¶ (remplacer le ¶ casse les
                //      positions / la prose inline, cf. mémoire surgical). Word
                //      ne re-parse rien → fin du bug lim/fraction & précédence.
                //      Approche A validée en POC (inline, prose préservée, CC
                //      backlink OK). Cf. ADR 2026-06-02-Feat-omml-insertion.
                int newStart = zwspEnd, newEnd = zwspEnd;
                Word.OMath om = null;
                Word.WdOMathType omType = Word.WdOMathType.wdOMathInline;
                Word.WdOMathJc omJc = Word.WdOMathJc.wdOMathJcLeft;
                try
                {
                    om = BuildOMathViaOmml(doc, sel, latex, zwspEnd);

                    if (om != null)
                    {
                        newStart = om.Range.Start;
                        newEnd = om.Range.End;
                        Log($"InsertOMathAt: OMath built, range=[{newStart},{newEnd})");

                        if (isInList)
                        {
                            omType = Word.WdOMathType.wdOMathInline;
                            omJc = Word.WdOMathJc.wdOMathJcLeft;
                            Log("InsertOMathAt: liste → Inline + Left (skip DecideOMathTyping)");
                        }
                        else
                        {
                            (omType, omJc) = DecideOMathTyping(om, source, Log);
                        }
                        Word.WdOMathType currentType;
                        try { currentType = om.Type; }
                        catch { currentType = Word.WdOMathType.wdOMathInline; }
                        if (currentType != omType)
                        {
                            try { om.Type = omType; Log($"InsertOMathAt: om.Type SET {currentType}→{omType}"); }
                            catch (Exception exType) { Log("insert_omath_type_error: " + exType.Message); }
                        }
                        else { Log($"InsertOMathAt: om.Type déjà {omType} (skip setter)"); }

                        try { om.Justification = omJc; }
                        catch (Exception exJc) { Log("insert_omath_jc_error: " + exJc.Message); }
                        newStart = om.Range.Start;
                        newEnd = om.Range.End;
                        Log($"InsertOMathAt: OMath finalized as {omType} / {omJc}, range=[{newStart},{newEnd})");
                    }
                    else { Log("InsertOMathAt: OMML inséré mais OMath introuvable au re-probe"); }
                }
                catch (Exception ex) { Log("insert_omml_error: " + ex.Message); return (zwspEnd, zwspEnd, null); }

                // 7. Wrap le ZWSP dans une CC RichText hidden EN DERNIER.
                //    Le math/OMath est settled, la CC ne le perturbe plus.
                Word.ContentControl cc = null;
                try
                {
                    var anchorRange = doc.Range(zwspStart, zwspEnd);
                    cc = anchorRange.ContentControls.Add(Word.WdContentControlType.wdContentControlRichText);
                    cc.Title = MathCursor.Host.CCMeta.MCMetaJson.CcTitle;
                    try { cc.Appearance = Word.WdContentControlAppearance.wdContentControlHidden; } catch { }
                    try { cc.LockContentControl = false; } catch { }
                    try { cc.LockContents = false; } catch { }
                    // En liste : Font.Hidden APRÈS cc.Add (sinon Word foire
                    // le wrap du run vanish, cf. 3b). Hors liste : déjà
                    // appliqué à l'étape 4.
                    if (isInList)
                    {
                        try { cc.Range.Font.Hidden = -1; } catch (Exception exH) { Log("insert_cc_font_hidden_error: " + exH.Message); }
                    }
                    Log($"InsertOMathAt: anchor CC créé sur ZWSP : cc.Range=[{cc.Range.Start},{cc.Range.End})");

                    // Re-probe om car positions ont pu shifter post-CC wrap.
                    try
                    {
                        foreach (Word.OMath o2 in doc.Range(cc.Range.End, Math.Min(doc.Content.End, cc.Range.End + (newEnd - newStart) + 5)).OMaths)
                        { om = o2; break; }
                        if (om != null)
                        {
                            newStart = om.Range.Start;
                            newEnd = om.Range.End;
                        }
                    }
                    catch (Exception exP) { Log("insert_reprobe_om_error: " + exP.Message); }

                    // Caret APRÈS l'OMath, HORS de la zone math sticky.
                    // SetRange(om.End) seul laisse Word en « math input mode »
                    // → la frappe suivante sort en italique math (bug
                    // retour-saisie + □-leak adjacent). MoveRight 1 char
                    // franchit la frontière de l'OMath et CLÔT la saisie math
                    // (= flèche droite). Technique validée POC Escape 1 (les
                    // alternatives SetRange(om.End+1)/EndKey/Italic off n'y
                    // arrivent pas). Cf. ADR 2026-06-02-Feat-omml-insertion.
                    if (om != null)
                    {
                        try
                        {
                            _app.Selection.SetRange(om.Range.End, om.Range.End);
                            _app.Selection.MoveRight(Word.WdUnits.wdCharacter, 1, Word.WdMovementType.wdMove);
                        }
                        catch { }
                    }
                }
                catch (Exception exCc) { Log("insert_anchor_cc_error: " + exCc.Message); cc = null; }

                // 7. Génère handle + écrit Tag JSON sur le CC (hash POST wrap
                //    pour que store-hash == read-hash sinon stale=True).
                string newHandle = null;
                if (cc != null && om != null)
                {
                    try
                    {
                        newHandle = NewHandleId();
                        string hash = MathCursor.Host.CCMeta.Sha1Helper.Compute(om.Range.WordOpenXML ?? "");
                        var meta = new MathCursor.Host.CCMeta.MCMeta
                        {
                            V = 1,
                            HandleId = newHandle,
                            Steno = source ?? "",
                            Latex = latex ?? "",
                            Version = typeof(SuggestionService).Assembly.GetName().Version?.ToString() ?? "0",
                            OmmlHash = hash,
                            ParsedAt = DateTime.UtcNow,
                        };
                        cc.Tag = MathCursor.Host.CCMeta.MCMetaJson.Serialize(meta);
                        Log($"InsertOMathAt: Tag set handle={newHandle} hash={hash.Substring(0, 8)}…");
                    }
                    catch (Exception exTag) { Log("insert_tag_error: " + exTag.Message); }
                }

                // 7b. (retiré 2026-05-19) Anciennement : cc.LockContents=true
                //     pour empêcher l'auto-grow. Cassait l'edit mode + revert.
                //     Défense gardée : flèches qui sélectionnent l'OMath
                //     (TrySelectOMathOnLeft/Right) + ZoneCleaner qui skip
                //     les CCs étrangères. Risque accepté : auto-grow peut
                //     toujours arriver mais bornes contrôlées en aval.

                // 8. CcSticky.EscapeCaretAfter : caduc avec le pattern anchor.
                //    Le CC est tiny et avant l'OMath, pas autour. Le caret est
                //    déjà repositionné à om.Range.End dans l'étape 6 ci-dessus.
                //    L'auto-grow de la sticky-zone du CC ne peut pas affecter
                //    l'OMath (séparée). On laisse Word gérer naturellement.
                //    if (cc != null) MathCursor.Host.CCMeta.CcSticky.EscapeCaretAfter(_app, cc);

                // 9. Cleanup absorbed handles : juste sidecar in-memory
                //    (les CCs des OMaths absorbées ont été supprimées par
                //    sel.Delete avec leur contenu).
                if (absorbedHandles != null && absorbedHandles.Count > 0)
                {
                    foreach (var h in absorbedHandles)
                    {
                        _handleRegistry.Forget(h);
                    }
                }

                sw.Stop();
                Log($"InsertOMathAt: OUT range=[{newStart},{newEnd}) handle={(newHandle ?? "null")} total={sw.ElapsedMilliseconds}ms");
                return (newStart, newEnd, newHandle);
            }
            finally
            {
                DebugInProgress = prevDebug;
            }
        }

        /// <summary>
        /// Produit l'OMath en OMML natif (structure, pas de re-parse Word → fin
        /// du bug lim/fraction) et l'insère CHIRURGICALEMENT sur une range
        /// placeholder 1-char à <paramref name="mathStart"/> (après le ZWSP).
        /// JAMAIS sur le ¶ : remplacer le ¶ casse positions + prose inline
        /// (cf. mémoire feedback_omml_insertion_surgical, ADR 2026-06-02).
        /// Lecture WordOpenXML LOCALE (le ¶ courant), pas O(doc). Renvoie null
        /// si l'insertion échoue (l'appelant retombe sur le chemin d'abandon).
        /// </summary>
        private Word.OMath BuildOMathViaOmml(Word.Document doc, Word.Selection sel, string latex, int mathStart)
        {
            var w = System.Xml.Linq.XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
            System.Xml.Linq.XElement oMath;
            try { oMath = MathCursor.Core.LatexToOmml.Convert(latex); }
            catch (Exception ex) { Log("BuildOMathViaOmml: LatexToOmml error: " + ex.Message); return null; }
            if (oMath == null) { Log("BuildOMathViaOmml: oMath null"); return null; }

            // Placeholder éphémère 1-char à la position math (juste après ZWSP).
            sel.SetRange(mathStart, mathStart);
            int phStart = sel.Start;
            sel.TypeText("□"); // □ marqueur transitoire
            int phEnd = sel.Start;
            var phRange = doc.Range(phStart, phEnd);

            // Lit le package WordOpenXML LOCAL (¶ courant), remplace le run du
            // placeholder par l'<m:oMath>, ré-insère sur la RANGE 1-char.
            // InsertXML sur une range étroite = insertion inline chirurgicale :
            // la prose avant/après est préservée (validé POC inline).
            System.Xml.Linq.XDocument xdoc;
            try { xdoc = System.Xml.Linq.XDocument.Parse(phRange.WordOpenXML); }
            catch (Exception ex) { Log("BuildOMathViaOmml: parse WordOpenXML error: " + ex.Message); return null; }

            System.Xml.Linq.XElement phRun = null;
            foreach (var r in xdoc.Descendants(w + "r"))
            {
                var t = r.Element(w + "t");
                if (t != null && t.Value == "□") { phRun = r; break; }
            }
            if (phRun == null) { Log("BuildOMathViaOmml: run placeholder introuvable dans WordOpenXML"); return null; }
            phRun.ReplaceWith(oMath);

            try { phRange.InsertXML(xdoc.ToString(System.Xml.Linq.SaveOptions.DisableFormatting)); }
            catch (Exception ex) { Log("BuildOMathViaOmml: InsertXML error: " + ex.Message); return null; }

            // Re-probe l'OMath fraîchement insérée — LOCAL, autour de phStart.
            Word.OMath om = null;
            try
            {
                int probeEnd = System.Math.Min(doc.Content.End, phStart + 200);
                foreach (Word.OMath o in doc.Range(phStart, probeEnd).OMaths) { om = o; break; }
            }
            catch (Exception ex) { Log("BuildOMathViaOmml: probe error: " + ex.Message); }
            if (om == null) Log("BuildOMathViaOmml: OMath introuvable au re-probe");
            return om;
        }


        /// <summary>
        /// Décide le mode d'affichage de l'OMath fraîchement créée
        /// (Display = bloc centré sur sa propre ligne, Inline = dans le flux)
        /// ET sa justification (= alignement). 3 cas :
        ///
        /// <list type="number">
        ///   <item><b>Source commence par un espace</b> (override utilisateur
        ///         explicite « je veux inline ») → Inline + Left.</item>
        ///   <item><b>OMath seule dans son ¶</b> (= pas de prose autour) →
        ///         Display + Left.</item>
        ///   <item><b>Autres cas</b> (OMath mixée avec du texte) → Inline + Left.</item>
        /// </list>
        ///
        /// <para>Tous les cas alignent à gauche (jamais centré ni justifié)
        /// pour respecter l'ergonomie standard d'un cours de maths au PAP.</para>
        /// </summary>
        private static (Word.WdOMathType type, Word.WdOMathJc justification)
            DecideOMathTyping(Word.OMath om, string source, Action<string> log)
        {
            var fallback = (Word.WdOMathType.wdOMathInline, Word.WdOMathJc.wdOMathJcLeft);
            if (om == null) return fallback;

            try
            {
                // Cas 1 : source commence par espace → override user explicite
                if (!string.IsNullOrEmpty(source) && source.StartsWith(" "))
                {
                    log?.Invoke("decide_typing: leading space dans source → Inline + Left (user override)");
                    return fallback;
                }

                // Cas 2 : OMath toute seule dans son contexte (¶, cellule, etc.) ?
                string paraText, omText;
                try
                {
                    paraText = om.Range.Paragraphs[1].Range.Text ?? "";
                    omText = om.Range.Text ?? "";
                }
                catch (Exception exRead)
                {
                    log?.Invoke("decide_typing_read_error: " + exRead.Message + " → Inline default");
                    return fallback;
                }

                // Strip l'OMath + chars structurels Word (= non-prose) :
                //   \r (Chr 13) paragraph mark
                //   \n (Chr 10) line feed
                //   \v (Chr 11) vertical tab / soft line break
                //   \a (Chr  7) cell marker (= contexte cellule de tableau)
                //   \t (Chr  9) tab
                //   \f (Chr 12) page break
                //   ​ ZWSP de l'anchor CC (= notre marker, pas de la prose user)
                // → si reste vide après strip, l'OMath est « seule dans son contexte »
                //   (¶ vide ou cellule vide) → DISPLAY.
                string remaining = paraText.Replace(omText, "")
                    .Replace("\r", "").Replace("\n", "")
                    .Replace("\v", "").Replace("\a", "")
                    .Replace("\t", "").Replace("\f", "")
                    .Replace("​", "")
                    .Trim();

                if (string.IsNullOrEmpty(remaining))
                {
                    log?.Invoke("decide_typing: OMath seule sur sa ligne → Display + Left");
                    return (Word.WdOMathType.wdOMathDisplay, Word.WdOMathJc.wdOMathJcLeft);
                }

                // Cas 3 : OMath mixée avec du texte → Inline + Left
                string previewRem = remaining.Length > 30
                    ? remaining.Substring(0, 30) + "…"
                    : remaining;
                log?.Invoke($"decide_typing: OMath mixée (rest=\"{previewRem}\") → Inline + Left");
                return fallback;
            }
            catch (Exception ex)
            {
                log?.Invoke("decide_typing_error: " + ex.Message + " → Inline default");
                return fallback;
            }
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
    }
}
