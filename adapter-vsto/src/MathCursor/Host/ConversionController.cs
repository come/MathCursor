using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MathCursor.Host.Caret;
using MathCursor.Host.Blocks;
using MathCursor.Host.Detection;
using MathCursor.UI;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host
{
    /// <summary>
    /// Orchestrateur du flux de conversion MANUEL (Ctrl+Espace / bouton
    /// ribbon). Remplace l'ex-SuggestionService (cf. ADR
    /// 2026-06-10-Refactor-phase2-adapter-orchestration-rewrite) :
    ///
    /// <code>
    /// Trigger → WordContextReader (¶ borné, OMaths masqués)
    ///         → ComputeSpanStart (délimiteur / stopword / OMath / début ¶)
    ///         → ForestEngine.Analyze → candidats LaTeX classés
    ///         → SuggestionPopupWindow (toujours montrée, même 1 candidat)
    ///         → commit sous UndoRecordScope → OMathInserter (OMML + anchor CC)
    /// </code>
    ///
    /// Ctrl+Espace répété popup ouverte = extension itérative d'un cran à
    /// gauche (passe outre la borne qui bloquait). Ctrl+Z restaure le texte
    /// source en un coup (UndoRecordScope).
    /// </summary>
    internal sealed class ConversionController : IDisposable
    {
        private readonly Word.Application _app;
        private readonly WordContextReader _contextReader;
        private readonly OMathInserter _inserter;
        private readonly Blocks.ChainController _chain;
        private readonly Action<string> _log;
        private readonly Func<Feedback.FeedbackReport> _buildFeedbackReport;

        private SuggestionPopupWindow _popup;
        private ZoneSpan _zone;          // span en cours (état d'extension itérative)
        private bool _committing;        // supprime la réaction aux SelectionChange induits

        // Undo-grab (UX 2026-06-12, reconstruit sur le pipeline walker) :
        // un Ctrl+Z juste après un commit SIMPLE replace le caret en FIN de
        // la sténo restaurée. One-shot, armé au commit, garde-fou « match ou
        // Redo intégral » (cf. TryGrabUndo).
        private (int absStart, string steno)? _undoGrab;

        // Pré-détection BLOC (M2 multiligne) : la zone committée commence par
        // un marqueur de chaîne ou un « { » → le moteur n'a vu que le RESTE,
        // la popup affichait le marqueur en préfixe, le commit route vers
        // ChainController. _pendingRestCandidates[i] = LaTeX SANS préfixe.
        private RelationLineMatch _pendingChainMatch;
        private bool _pendingSystemOpener;
        private List<string> _pendingRestCandidates;

        public ConversionController(
            Word.Application app,
            Func<Feedback.FeedbackReport> buildFeedbackReport,
            Action<string> log = null)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _buildFeedbackReport = buildFeedbackReport;
            _log = log ?? LogDiag;
            _contextReader = new WordContextReader(app);
            _inserter = new OMathInserter(app, _log);
            _chain = new Blocks.ChainController(app, _inserter, _log);
            Resolver = new SourceMap.SourceMapResolver(_inserter.SourceMap);
        }

        /// <summary>Résolveur équation→source partagé (map CustomXMLParts) —
        /// consommé par EditModeController et EquationDeletionGuard.</summary>
        internal SourceMap.SourceMapResolver Resolver { get; }

        public bool IsPopupVisible => _popup?.IsVisible == true;
        public bool IsNavMode => _popup?.IsNavMode == true;

        /// <summary>Vrai pendant un commit : les SelectionChange induits par
        /// les SetRange internes ne doivent pas refermer/relancer quoi que ce soit.</summary>
        public bool IsCommitting => _committing;

        // Stopwords + délimiteurs de span → Host/SpanComputer.cs (logique pure).

        // ── Entrée du flux ───────────────────────────────────────────────

        /// <summary>
        /// Trigger explicite. Si la popup est déjà ouverte → étend la span
        /// d'un cran à gauche. Sinon calcule la span AUTOUR du caret (avant
        /// ET après — un caret replacé au milieu d'une expression la prend
        /// en entier, retour user 2026-06-10) et propose.
        /// </summary>
        public void Trigger()
        {
            try
            {
                if (_app.Documents.Count == 0) return;
                if (IsPopupVisible && _zone != null) { ExtendOneStop(); return; }

                var paragraph = _contextReader.ReadCurrentParagraph();
                string text = paragraph.Text ?? "";
                int caret = paragraph.CaretOffset;
                if (string.IsNullOrEmpty(text)) return;

                // La fin de ¶ Word inclut un \r virtuel — le sauter, sinon
                // il est pris pour un délimiteur et la span est vide.
                while (caret > 0 && (text[caret - 1] == '\r' || text[caret - 1] == '\n')) caret--;
                if (caret < 0) return;

                int spanStart = SpanComputer.ComputeSpanStart(text, caret, paragraph.OMathRegions);
                int spanEnd = SpanComputer.ComputeSpanEnd(text, caret, paragraph.OMathRegions);
                while (spanStart < spanEnd && char.IsWhiteSpace(text[spanStart])) spanStart++;
                while (spanEnd > spanStart && char.IsWhiteSpace(text[spanEnd - 1])) spanEnd--;
                if (spanEnd <= spanStart) return;

                var zone = new ZoneSpan(paragraph.ParagraphAbsStart, spanStart, spanEnd, text, paragraph.OMathRegions);
                _log($"convert: span=[{spanStart},{spanEnd}] → \"{Preview(zone.Text)}\"");
                AnalyzeAndShow(zone);
            }
            catch (Exception ex) { _log("convert_trigger_error: " + ex.Message); }
        }

        /// <summary>
        /// Extension itérative : passe outre la borne courante (délimiteur /
        /// stopword) et propose la span élargie. Borne dans un OMath = stop.
        /// </summary>
        private void ExtendOneStop()
        {
            var iter = _zone;
            if (iter == null || string.IsNullOrEmpty(iter.ParagraphText) || iter.StringStart <= 0) return;

            int boundary = iter.StringStart - 1;
            while (boundary >= 0 && char.IsWhiteSpace(iter.ParagraphText[boundary])) boundary--;
            if (boundary < 0) return;

            foreach (var (s, e) in iter.OMaths)
                if (s <= boundary && boundary < e) { _log("convert: extension bloquée par OMath, stop"); return; }

            int newStart = SpanComputer.ComputeSpanStart(iter.ParagraphText, boundary, iter.OMaths);
            while (newStart < iter.StringEnd && char.IsWhiteSpace(iter.ParagraphText[newStart])) newStart++;
            if (newStart >= iter.StringStart) return;

            var extended = new ZoneSpan(iter.ParagraphAbsStart, newStart, iter.StringEnd, iter.ParagraphText, iter.OMaths);
            _log($"convert: extension span=[{extended.StringStart},{extended.StringEnd}] → \"{Preview(extended.Text)}\"");
            AnalyzeAndShow(extended);
        }

        /// <summary>
        /// Proposition AUTO (NER, cf. ADR 2026-06-10-Feat-ner-auto-detection-
        /// debounce) : même moteur/popup que le manuel mais SILENCIEUX —
        /// pas de StatusBar en échec (la popup se masque si la zone ne donne
        /// rien), pas de re-show si la zone est inchangée (anti-flicker).
        /// Recalcule MÊME en nav mode : la popup préserve nav mode +
        /// sélection à travers le rafraîchissement (cf. ShowCandidates).
        /// </summary>
        public bool TryProposeAuto(ZoneSpan zone)
        {
            if (zone == null || zone.IsEmpty)
            {
                if (IsPopupVisible) HidePopup();
                return false;
            }
            if (_committing) return false;
            // Pas de garde nav mode : on recalcule TOUJOURS — c'est la popup
            // qui préserve nav mode + sélection à travers le rafraîchissement
            // (cf. ShowCandidates, retour user 2026-06-10).
            if (IsPopupVisible && _zone != null
                && _zone.ParagraphAbsStart == zone.ParagraphAbsStart
                && _zone.StringStart == zone.StringStart
                && _zone.StringEnd == zone.StringEnd
                && string.Equals(_zone.Text, zone.Text, StringComparison.Ordinal))
                return true; // zone identique → rien à rafraîchir
            return AnalyzeAndShow(zone, silent: true);
        }

        /// <summary>Moteur forest + affichage popup. Garde la zone si OK.
        /// <paramref name="silent"/> : échec sans StatusBar, popup masquée.</summary>
        private bool AnalyzeAndShow(ZoneSpan zone, bool silent = false)
        {
            // Pré-détection BLOC (chaîne « = / ⟺ … » ou système « { … »,
            // hors moteur — ADR multiline-chain-eqarr) : le moteur ne voit
            // que le RESTE ; la popup affiche le marqueur rendu en préfixe
            // (« si l'utilisateur l'a écrit il veut le voir »). Le detector
            // ne trim pas la fin → l'espace-signal (R*␣) survit dans Rest.
            _pendingSystemOpener = false;
            _pendingChainMatch = null;
            string engineInput = zone.TextForEngine;
            string displayPrefix = "";
            var sys = RelationLineDetector.TryDetectSystemOpener(engineInput);
            if (sys != null)
            {
                _pendingSystemOpener = true;
                engineInput = sys.Rest;
                displayPrefix = "\\{";
            }
            else
            {
                var cm = RelationLineDetector.TryDetect(engineInput);
                if (cm != null)
                {
                    _pendingChainMatch = cm;
                    engineInput = cm.Rest;
                    displayPrefix = cm.MarkerLatex;
                }
            }
            if (string.IsNullOrWhiteSpace(engineInput))
            {
                // Marqueur seul (« = » en cours de frappe) : on attend la suite.
                if (silent && IsPopupVisible) HidePopup();
                return false;
            }

            MathCursor.Engine.AnalyzeResult result;
            // Culture relue à chaque trigger : un changement dans la popup
            // Paramètres s'applique sans redémarrage.
            try { result = MathCursor.Engine.ForestEngine.Analyze(engineInput, Settings.SettingsStore.Current.ToEngineCulture()); }
            catch (Exception ex)
            {
                _log($"convert: engine error sur \"{Preview(zone.Text)}\": {ex.Message}");
                if (silent) { if (IsPopupVisible) HidePopup(); }
                else TryStatusBar(Strings.ConvertNothingRecognized);
                return false;
            }
            if (result.Decision == "erreur" || result.Ranked.Count == 0)
            {
                _log($"convert: aucune lecture pour \"{Preview(zone.Text)}\"");
                if (silent) { if (IsPopupVisible) HidePopup(); }
                else TryStatusBar(Strings.ConvertNothingRecognized);
                return false;
            }

            var candidates = result.Ranked.Select(c => c.Latex).ToList();
            _log($"convert: {result.Decision}, {candidates.Count} candidat(s), top=\"{(displayPrefix + candidates[0])}\"");

            // Aperçu de MERGE (demande user 2026-06-10) : si la ligne du
            // dessus sera fusionnée au commit, chaque candidat est rendu
            // INTÉGRÉ dans l'aperçu du bloc (lignes précédentes grisées,
            // « ⋯ » au-delà de 2 lignes, accolade pour les systèmes).
            var (mergeKind, mergePrevLines) = ProbeMergePreview(zone);

            // Bloc en attente : la popup AFFICHE le marqueur en préfixe, mais
            // le commit utilise le LaTeX du RESTE (aligné par index).
            // Exception : aperçu SYSTÈME actif → l'accolade de l'aperçu
            // représente déjà le « { », pas de préfixe (sinon double {{).
            _pendingRestCandidates = candidates;
            if (mergeKind == Blocks.BlockTypes.System) displayPrefix = "";
            var display = displayPrefix.Length == 0
                ? candidates
                : candidates.Select(c => displayPrefix + c).ToList();

            EnsurePopup();
            var (x, yBelow, yAbove) = ComputeAnchor(zone);
            _zone = zone;
            _popup.ShowCandidates(display, x, yBelow, yAbove, zone.Text, mergeKind, mergePrevLines);
            return true;
        }

        /// <summary>Contexte de merge pour l'aperçu popup : ("chain"|"system",
        /// LaTeX des lignes du bloc du dessus) si la ligne committée sera
        /// fusionnée, (null, null) sinon. Règle système 2026-06-10 : « { »
        /// requis sur la ligne courante ET système au-dessus. Sonde
        /// non-mutante, silencieuse.</summary>
        private (string Kind, IReadOnlyList<string> PrevLines) ProbeMergePreview(ZoneSpan zone)
        {
            try
            {
                var doc = _app.ActiveDocument;
                if (doc == null || zone == null) return (null, null);
                if (!_pendingSystemOpener && _pendingChainMatch == null) return (null, null);
                var probe = _chain.ProbeMergeAbove(doc, zone);
                if (probe == null) return (null, null);
                var (t, lines) = probe.Value;
                if (_pendingSystemOpener)
                    return t == Blocks.BlockTypes.System
                        ? (Blocks.BlockTypes.System, (IReadOnlyList<string>)lines)
                        : (null, null);
                return (t == "" || t == Blocks.BlockTypes.Chain)
                    ? (Blocks.BlockTypes.Chain, (IReadOnlyList<string>)lines)
                    : (null, null);
            }
            catch { return (null, null); }
        }

        /// <summary>
        /// Ancre de la popup = DÉBUT de la zone convertie (choix UX 2026-06-10) :
        /// bord gauche aligné sur le 1er caractère de la zone, juste SOUS sa
        /// ligne. Position lue via <c>Window.GetPoint(Range)</c> (Word donne
        /// les pixels écran exacts — plus fiable que le caret GDI). Retourne
        /// aussi <c>yAbove</c> (haut de ligne) pour que la popup bascule
        /// AU-DESSUS si pas de place en bas d'écran. Fallback : caret GDI.
        /// </summary>
        private (double x, double yBelow, double yAbove) ComputeAnchor(ZoneSpan zone)
        {
            // X = début de zone (GetPoint Word) ; Y = caret GDI. Pourquoi ce
            // mix : la boîte de ligne de GetPoint inclut interligne + espace
            // de paragraphe (8 pt par défaut) → son « bas » tombe ~20 px sous
            // le texte (retour user 2026-06-10). Le caret GDI a exactement la
            // hauteur du texte → bas du caret = ancre collée sous la ligne.
            double? anchorX = null;
            double boxBelow = 0, boxAbove = 0;
            bool hasBox = false;
            try
            {
                var doc = _app.ActiveDocument;
                if (doc != null && zone.TryToInternal(doc, out int absStart, out _))
                {
                    var anchorRange = doc.Range(absStart, Math.Min(absStart + 1, doc.Content.End));
                    int left, top, width, height;
                    _app.ActiveWindow.GetPoint(out left, out top, out width, out height, anchorRange);
                    if (height > 0)
                    {
                        double scale = CaretScreenPositionReader.GetDpiScale();
                        anchorX = left / scale;
                        boxBelow = (top + height) / scale;
                        boxAbove = top / scale;
                        hasBox = true;
                    }
                }
            }
            catch (Exception ex) { _log("anchor_getpoint_error: " + ex.Message); }

            const double GapDip = 2.0;
            if (CaretScreenPositionReader.TryReadRect(out double cx, out double cTop, out double cBottom))
                return (anchorX ?? cx, cBottom + GapDip, cTop - GapDip);

            // Caret indispo (sélection étendue…) : boîte de ligne GetPoint.
            if (hasBox && anchorX.HasValue)
                return (anchorX.Value, boxBelow + GapDip, boxAbove - GapDip);

            var (fx, fy) = CaretScreenPositionReader.Read();
            return (fx, fy, fy - 20);
        }

        // ── Commit ───────────────────────────────────────────────────────

        /// <summary>
        /// Commit du candidat sélectionné (Tab = top si pas de nav, Enter =
        /// sélection courante, clic = ligne cliquée). Retourne true si un
        /// commit a eu lieu.
        /// </summary>
        public bool CommitSelected()
        {
            var zone = _zone;
            var popup = _popup;
            if (zone == null || popup == null || !popup.IsVisible) return false;
            string latex = popup.SelectedLatex;
            if (string.IsNullOrEmpty(latex)) return false;

            // Le LaTeX à committer = celui du RESTE (sans le préfixe marqueur
            // affiché), aligné par index de sélection.
            int idx = popup.SelectedIndex;
            if (_pendingRestCandidates != null && idx >= 0 && idx < _pendingRestCandidates.Count)
                latex = _pendingRestCandidates[idx];

            _committing = true;
            try
            {
                var doc = _app.ActiveDocument;
                if (doc == null) return false;
                if (!zone.TryToInternal(doc, out int absStart, out int absEnd))
                {
                    _log("convert: TryToInternal a échoué, abort commit");
                    return false;
                }

                _log($"commit: [{absStart},{absEnd}) latex=\"{latex}\" source=\"{Preview(zone.Text)}\""
                    + (_pendingSystemOpener ? " [système]" : _pendingChainMatch != null ? $" [chaîne {_pendingChainMatch.MarkerTyped}]" : ""));
                using (new UndoRecordScope(_app, "MathCursor : conversion"))
                {
                    if (_pendingSystemOpener)
                        _chain.CommitSystemLine(doc, zone, latex);
                    else if (_pendingChainMatch != null)
                        _chain.CommitChainLine(doc, zone, _pendingChainMatch, latex);
                    else
                    {
                        _inserter.Insert(absStart, absEnd, latex, zone.Text);
                        // Arme l'undo-grab (commits simples seulement — le
                        // restore multiligne d'un bloc est déjà tracké à part).
                        string needle = (zone.Text ?? "").Trim();
                        if (needle.Length > 0) _undoGrab = (absStart, needle);
                    }

                    // M4 — flow multiligne (validé user 2026-06-10) : la ligne
                    // committée portait un séparateur → on ouvre la ligne
                    // suivante avec le même séparateur pré-placé (« { » pour
                    // les systèmes, le marqueur tapé pour les chaînes).
                    // Dans le MÊME record undo : 1 Ctrl+Z annule tout.
                    // Sortie de flow : Entrée sur la ligne marqueur-seul
                    // l'efface (cf. TryExitFlowOnEnter).
                    string nextPrefix = _pendingSystemOpener ? "{ "
                        : _pendingChainMatch != null ? _pendingChainMatch.MarkerTyped + " "
                        : null;
                    if (nextPrefix != null)
                    {
                        try
                        {
                            var sel = _app.Selection;
                            sel.TypeParagraph();
                            sel.TypeText(nextPrefix);
                        }
                        catch (Exception exF) { _log("flow_next_line_error: " + exF.Message); }
                    }
                }
                // Source en map APRÈS la fermeture du record undo (la lecture
                // WordOpenXML du Record fermerait le record — mesuré
                // 2026-06-12, ADR hash-source-map amendé).
                _inserter.FlushPendingRecord();

                // Compteur d'usage anonyme (un entier, aucun contenu) — seulement
                // si l'utilisateur n'a pas coupé l'opt-out. Cf. ADR
                // 2026-06-18-Feat-usage-counter-telemetry.
                if (Settings.SettingsStore.Current.SendUsageStats)
                    Usage.UsageCounter.Increment();

                return true;
            }
            catch (Exception ex) { _log("commit_error: " + ex.Message); TryStatusBar(Strings.ConvertCommitFailed); return false; }
            finally
            {
                HidePopup();
                _committing = false;
            }
        }

        // ── Encadrer le résultat (bouton ruban) ──────────────────────────

        /// <summary>
        /// Encadre l'OMath sous le caret EN PLACE : branche une
        /// <c>m:borderBox</c> autour de son contenu via
        /// <c>OMath.Functions.Add(…, wdOMathFunctionBorderBox)</c>, sans
        /// delete+redraw (ADR 2026-06-22). Confirme d'abord la source par K2
        /// (l'OMath doit être à nous), puis ré-enregistre la map après mutation
        /// (le contenu — donc le hash — change). Déclenché par le bouton ruban
        /// « Encadrer » ou l'entrée « Encadrer cette formule » de la popup
        /// d'édition. No-op (loggué) si : pas d'équation sous le caret ;
        /// équation hors map (éditée à la main, K2 non confirmée) ; déjà
        /// encadrée ; bloc multiligne (hors périmètre v1). Retourne true si
        /// l'encadrement a eu lieu.
        /// </summary>
        public bool BoxAtCaret()
        {
            var doc = _app.ActiveDocument;
            if (doc == null) return false;

            var om = FindOMathAtCaret();
            if (om == null) { _log("box: pas d'équation sous le caret"); return false; }

            // Source CONFIRMÉE par K2 : on REMPLACE l'OMath (destructif), jamais
            // sur un simple match K1. Miss = équation éditée main, on s'abstient.
            var entry = Resolver.ResolveConfirmed(doc, om);
            if (entry == null || string.IsNullOrEmpty(entry.Latex))
            { _log("box: source introuvable (hors map / éditée main)"); return false; }

            if (entry.IsAlreadyBoxed)
            { _log("box: déjà encadrée, no-op"); return false; }

            // Matrices / environnements LaTeX (structure tableau) : hors
            // périmètre encadrement v1.
            if (entry.IsMatrixLike)
            { _log("box: matrice/env. non encadrable (v1)"); return false; }

            // boxed = décoration appliquée à un OMath EXISTANT (ADR 2026-06-22) :
            // on branche une m:borderBox DIRECTEMENT autour du contenu via
            // Functions.Add, EN PLACE — pas de delete+redraw (qui butait sur
            // ZoneCleaner/Range.Delete « Cannot edit Range » pour les OMaths
            // structurées). Pour un BLOC (chaîne/système), on ne vise que la
            // DERNIÈRE ligne. La source garde la sténo INTÉRIEURE (revert →
            // formule simple) ; le latex porte le \boxed (garde anti
            // double-encadrement).
            bool lastLineOnly = entry.IsBlock;
            string newSource = entry.Steno ?? "";
            string newLatex = lastLineOnly
                ? BoxLastLatexLine(entry.Latex)
                : "\\boxed{" + entry.Latex + "}";

            _committing = true;
            try
            {
                int omStart = om.Range.Start, omEnd = om.Range.End;
                _log($"box(inplace): om [{omStart},{omEnd}) lastLine={lastLineOnly} source=\"{newSource}\" latex=\"{newLatex}\"");
                using (new UndoRecordScope(_app, "MathCursor : encadrer"))
                {
                    try
                    {
                        // Cible : l'OMath entier, ou la DERNIÈRE ligne d'un bloc
                        // (chaîne/système) = dernière row de son eqArr.
                        Word.OMath target = om;
                        Word.Range boxRange;
                        if (lastLineOnly)
                        {
                            var eqFn = FindEqArray(om);
                            var rows = eqFn?.EqArray?.E;
                            if (rows == null || rows.Count == 0)
                            { _log("box(inplace): bloc sans eqArr → no-op"); return false; }
                            target = (Word.OMath)rows.Item(rows.Count);

                            // Une row d'eqArr porte des marqueurs d'alignement
                            // « & » (cf. ChainComposer). Un borderBox n'est PAS
                            // une cellule d'eqArr → s'il les enveloppe, ils
                            // s'affichent EN CLAIR. Règle (choix user 2026-06-22) :
                            // on GARDE le PREMIER & (il cale la boîte sur la
                            // colonne du connecteur), on SUPPRIME les autres &, et
                            // on encadre tout ce qui suit le premier &. La dernière
                            // ligne perd son alignement interne (contenu boxé = un
                            // bloc), mais son bord gauche reste calé sur la colonne.
                            boxRange = target.Range;
                            try
                            {
                                // Localisation des & via Range.Characters
                                // (énumération native Word, FIABLE) — pas de
                                // MoveRight(n) qui dérive sur les marqueurs
                                // d'alignement. On collecte d'abord, puis on
                                // supprime de la FIN vers le début (les ranges
                                // amont restent valides).
                                var amps = new System.Collections.Generic.List<Word.Range>();
                                foreach (Word.Range ch in target.Range.Characters)
                                {
                                    string ct = null; try { ct = ch.Text; } catch { }
                                    if (ct == "&") amps.Add(ch);
                                }
                                if (amps.Count > 0)
                                {
                                    // Combien de & garder en tête : ChainComposer
                                    // met une colonne PAD en tête sur les chaînes
                                    // à connecteur (Row3 = `& ⟺ & lhs & relRhs`,
                                    // 3 &) → garder 2 (pad + sép. connecteur)
                                    // laisse le ⟺ DEHORS et encadre lhs+relRhs.
                                    // Chaîne « = » / système (1 &) → garder 1.
                                    int keep = amps.Count >= 3 ? 2 : 1;
                                    // supprime les & au-delà des `keep` premiers,
                                    // de la fin vers le début, via Range.Delete.
                                    for (int i = amps.Count - 1; i >= keep; i--)
                                    {
                                        try { amps[i].Delete(); }
                                        catch (Exception exD) { _log($"box(inplace): del & #{i} KO: {exD.Message}"); }
                                    }
                                    int boxStart = amps[keep - 1].End;
                                    boxRange = doc.Range(boxStart, target.Range.End);
                                    _log($"box(inplace): {amps.Count} & (gardé {keep}), box=[{boxStart},{boxRange.End}]");
                                }
                            }
                            catch (Exception exAmp) { _log("box(inplace): & KO: " + exAmp.Message); }
                        }
                        else boxRange = target.Range;

                        var fn = target.Functions.Add(boxRange, Word.WdOMathFunctionType.wdOMathFunctionBorderBox);

                        // Padding intérieur : la borderBox colle au contenu par
                        // défaut (pas d'attribut de marge en OMML). On insère un
                        // espace DANS l'argument E (donc dans la boîte), de
                        // chaque côté. U+2004 (three-per-em, 1/3 em) = espace
                        // EXPLICITE que Word préserve en mode math (un espace
                        // ASCII y est avalé par l'auto-spacing). InsertAfter
                        // d'abord (ne décale pas le début), puis InsertBefore.
                        const string Pad = " ";
                        try
                        {
                            var er = fn.BorderBox.E.Range;
                            er.InsertAfter(Pad);
                            er.InsertBefore(Pad);
                        }
                        catch (Exception exP) { _log("box(inplace): padding KO: " + exP.Message); }

                        try { _log($"box(inplace): borderBox posée, fn=[{fn.Range.Start},{fn.Range.End}] om now [{om.Range.Start},{om.Range.End}]"); }
                        catch { }
                    }
                    catch (Exception exA) { _log("box(inplace): Functions.Add KO: " + exA.Message); return false; }
                }

                // Source en map APRÈS le scope undo (Record lit WordOpenXML →
                // fermerait le record). Le contenu a changé (borderBox) → K1/K2
                // changent : sans re-Record, l'équation n'est plus « à nous »
                // (plus de revert/edit popup). Re-trouve l'OMath au voisinage.
                Word.OMath boxed = null;
                int probeEnd = Math.Min(doc.Content.End, omStart + 2);
                foreach (Word.OMath o in doc.Range(Math.Max(0, omStart - 1), probeEnd).OMaths) { boxed = o; break; }
                if (boxed != null)
                    _inserter.SourceMap.Record(doc, boxed, newSource, newLatex, entry.Type, OMathInserter.NewHandleId());
                else
                    _log("box(inplace): OMath introuvable au re-probe, source non réenregistrée");
                return true;
            }
            catch (Exception ex) { _log("box_error: " + ex.Message); return false; }
            finally { _committing = false; }
        }

        /// <summary>Première fonction eqArr de l'OMath (top-level pour une
        /// chaîne, nichée dans le delim pour un système), ou null. Récurse via
        /// les arguments génériques (<c>OMathFunction.Args</c>).</summary>
        private static Word.OMathFunction FindEqArray(Word.OMath om)
        {
            if (om == null) return null;
            Word.OMathFunctions fns;
            try { fns = om.Functions; } catch { return null; }
            if (fns == null) return null;
            foreach (Word.OMathFunction f in fns)
            {
                try { if (f.Type == Word.WdOMathFunctionType.wdOMathFunctionEqArray) return f; }
                catch { continue; }
                Word.OMathArgs args = null;
                try { args = f.Args; } catch { }
                if (args == null) continue;
                int n = 0;
                try { n = args.Count; } catch { }
                for (int i = 1; i <= n; i++)
                {
                    Word.OMath arg = null;
                    try { arg = (Word.OMath)args.Item(i); } catch { }
                    var found = FindEqArray(arg);
                    if (found != null) return found;
                }
            }
            return null;
        }

        /// <summary>Enveloppe la DERNIÈRE ligne d'un LaTeX de bloc (lignes
        /// jointes par '\n', cf. <c>EquationSource.Latex</c>) dans
        /// <c>\boxed{…}</c>. Métadonnée map only (le rendu se fait sur l'OMath,
        /// le revert sur la sténo) — sert surtout à la garde anti
        /// double-encadrement.</summary>
        private static string BoxLastLatexLine(string latex)
        {
            if (string.IsNullOrEmpty(latex)) return "\\boxed{}";
            int nl = latex.LastIndexOf('\n');
            return nl < 0
                ? "\\boxed{" + latex + "}"
                : latex.Substring(0, nl + 1) + "\\boxed{" + latex.Substring(nl + 1) + "}";
        }

        /// <summary>OMath inclus dans la sélection courante (caret), ou null.</summary>
        private Word.OMath FindOMathAtCaret()
        {
            try
            {
                var sel = _app.Selection;
                if (sel?.OMaths != null && sel.OMaths.Count > 0)
                    foreach (Word.OMath om in sel.OMaths) return om;
            }
            catch { }
            return null;
        }

        // ── Navigation popup (déléguée par le hook clavier) ─────────────

        public void EnterNavMode() => _popup?.EnterNavMode();
        public bool MoveSelection(int delta) => _popup?.MoveSelection(delta) ?? false;

        public void HidePopup()
        {
            _popup?.HidePopup();
            _zone = null;
            _pendingChainMatch = null;
            _pendingSystemOpener = false;
            _pendingRestCandidates = null;
        }

        /// <summary>
        /// Caret déplacé (event natif WindowSelectionChange). On ferme la
        /// popup : le contexte a changé. Ignoré pendant un commit (les
        /// SetRange internes déclenchent l'event).
        /// </summary>
        public void OnSelectionChanged()
        {
            if (_committing) return;
            if (IsPopupVisible) HidePopup();
            TryApplyUndoGrab();
        }

        /// <summary>
        /// Undo-grab RÉACTIF (UX 2026-06-12 — « pas d'interception de
        /// Ctrl+Z, c'est dégueu ») : l'undo reste 100 % NATIF (Ctrl+Z,
        /// ribbon, pile undo intacte). Après coup, au SelectionChange, si
        /// l'équation du dernier commit a disparu et que la sténo est revenue
        /// à sa position, le caret est poussé en FIN de la saisie restaurée.
        /// One-shot, et seulement au voisinage de la zone (pas de lecture du
        /// ¶ sur les clics lointains).
        /// </summary>
        private void TryApplyUndoGrab()
        {
            var grab = _undoGrab;
            if (grab == null) return;
            try
            {
                var doc = _app.ActiveDocument;
                var sel = _app.Selection;
                if (doc == null || sel == null) return;
                if (Math.Abs(sel.Start - grab.Value.absStart) > grab.Value.steno.Length + 16) return;

                // L'équation du commit est-elle toujours là ? (rien à faire)
                int probeEnd = Math.Min(doc.Content.End, grab.Value.absStart + 2);
                if (doc.Range(Math.Max(0, grab.Value.absStart - 1), probeEnd).OMaths.Count > 0) return;

                var para = doc.Range(grab.Value.absStart, grab.Value.absStart).Paragraphs[1].Range;
                string text = para.Text ?? "";
                int idx = text.IndexOf(grab.Value.steno, StringComparison.Ordinal);
                if (idx < 0) { _undoGrab = null; return; }   // état divergé (suppression manuelle…) → désarme

                _undoGrab = null;   // one-shot
                int caretPos = Detection.ParagraphPositionTranslator.StringPosToInternal(
                    para, idx + grab.Value.steno.Length);
                sel.SetRange(caretPos, caretPos);
                _log($"undo-grab: undo natif détecté, caret en fin de sténo ({caretPos})");
            }
            catch (Exception ex) { _log("undo_grab_error: " + ex.Message); }
        }

        /// <summary>
        /// Sortie du flow multiligne (M4) : Entrée sur une ligne qui ne
        /// contient QUE le séparateur pré-placé (« ⟺ », « = », « { »…) →
        /// on efface le séparateur et on consomme l'Entrée. La ligne reste,
        /// vide — la chaîne est close (« double entrée c'est naturel »).
        /// False = Entrée normale.
        /// </summary>
        public bool TryExitFlowOnEnter()
        {
            try
            {
                var doc = _app.ActiveDocument;
                var sel = _app.Selection;
                if (doc == null || sel == null) return false;

                var para = sel.Range.Paragraphs[1];
                try { if (para.Range.OMaths != null && para.Range.OMaths.Count > 0) return false; } catch { }

                string line = (para.Range.Text ?? "").TrimEnd('\r', '\n');
                string t = line.Trim();
                if (t.Length == 0) return false;

                bool markerOnly = false;
                var sys = Blocks.RelationLineDetector.TryDetectSystemOpener(t);
                if (sys != null && string.IsNullOrWhiteSpace(sys.Rest)) markerOnly = true;
                else
                {
                    var m = Blocks.RelationLineDetector.TryDetect(t);
                    if (m != null && string.IsNullOrWhiteSpace(m.Rest)) markerOnly = true;
                }
                if (!markerOnly) return false;

                int s = para.Range.Start;
                int e = para.Range.End - 1; // préserve la marque de ¶
                if (e > s) doc.Range(s, e).Delete();
                HidePopup();
                _log("flow: sortie (ligne séparateur-seul effacée)");
                return true; // consomme l'Entrée
            }
            catch (Exception ex) { _log("flow_exit_error: " + ex.Message); return false; }
        }

        public void Dispose()
        {
            try { _popup?.Close(); } catch { }
            _popup = null;
        }

        // ── Internals ────────────────────────────────────────────────────

        /// <summary>
        /// Préchauffage (UX 2026-06-12 : « la première apparition fait tout
        /// lagguer ») : paye UNE fois, au démarrage, les coûts de première
        /// popup — création du HWND WPF, layout, premier rendu WpfMath (JIT
        /// + fontes) — en jouant le chemin complet HORS ÉCRAN. À appeler à
        /// priorité idle sur le thread UI. Le moteur et le sérialiseur sont
        /// chauffés à part (thread de fond, cf. ThisAddIn).
        /// </summary>
        public void WarmUpPopup()
        {
            try
            {
                // Popup déjà créée = déjà chaude par usage réel — et surtout
                // ne JAMAIS écraser une popup en cours d'utilisation (mesuré
                // 2026-06-12 : l'idle est arrivé 30 s après le boot, en
                // pleine frappe — la popup live partait à −10000 puis Hide).
                if (_popup != null) { _log("warmup: popup déjà créée, skip"); return; }
                EnsurePopup();
                _popup.ShowCandidates(
                    new[] { "f(x)=\\frac{1}{2}x^{2}+\\sqrt{x}" },
                    anchorX: -10000, anchorYBelow: -10000, anchorYAbove: -10000,
                    sourceText: "");
                _popup.HidePopup();
                _zone = null;
                _log("warmup: popup préchauffée (HWND + WpfMath)");
            }
            catch (Exception ex) { _log("warmup_popup_error: " + ex.Message); }
        }

        private void EnsurePopup()
        {
            if (_popup != null) return;
            _popup = new SuggestionPopupWindow();
            _popup.CommitRequested += () => CommitSelected();
            _popup.ReportRequested += OpenFeedbackDialog;
        }

        private void OpenFeedbackDialog()
        {
            try
            {
                // Screenshot AVANT d'ouvrir le dialog (même ordre validé que
                // OnReportIssueClicked côté ruban) : la popup de suggestion
                // visible fait partie du contexte du bug, le dialog lui ne
                // doit JAMAIS être dans l'image. Sans cette pré-capture, le
                // fallback de FeedbackDialog capture au moment d'Envoyer,
                // dialog ouvert en plein milieu de l'écran.
                byte[] preScreenshot = null;
                try { preScreenshot = FeedbackBundle.CaptureScreenshotPng(); } catch { }
                var report = _buildFeedbackReport?.Invoke() ?? new Feedback.FeedbackReport();
                report.NerText = _zone?.Text ?? "";
                report.RecognizedFormula = _popup?.SelectedLatex ?? "";
                if (preScreenshot != null && preScreenshot.Length > 0)
                    report.ScreenshotPngBase64 = Convert.ToBase64String(preScreenshot);
                var sender = Feedback.FeedbackSenderFactory.Create();
                new FeedbackDialog(report, sender).ShowDialog();
            }
            catch (Exception ex) { _log("feedback_open_error: " + ex.Message); }
        }

        // ComputeSpanStart / ComputeSpanEnd extraits dans Host/SpanComputer.cs
        // (logique pure, testable en unitaire). Cf. ADR
        // 2026-06-18-Fix-input-autocorrect-fraction-factorial.

        private void TryStatusBar(string message)
        {
            try { _app.StatusBar = message; } catch { }
        }

        private static string Preview(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length > 60 ? s.Substring(0, 60) + "…" : s;
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
                    $"{DateTime.UtcNow:o} convert {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
