using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MathCursor.Host.Caret;
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
        private readonly Action<string> _log;
        private readonly Func<Feedback.FeedbackReport> _buildFeedbackReport;

        private SuggestionPopupWindow _popup;
        private ZoneSpan _zone;          // span en cours (état d'extension itérative)
        private bool _committing;        // supprime la réaction aux SelectionChange induits

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
        }

        public bool IsPopupVisible => _popup?.IsVisible == true;
        public bool IsNavMode => _popup?.IsNavMode == true;

        /// <summary>Vrai pendant un commit : les SelectionChange induits par
        /// les SetRange internes ne doivent pas refermer/relancer quoi que ce soit.</summary>
        public bool IsCommitting => _committing;

        // ── Données de span : stopwords + délimiteurs FR (table portée de
        //    l'ex data/locale/fr.yml, le YAML appartenait à l'ancien moteur).
        private static readonly HashSet<string> Stopwords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "soit", "soient", "et", "ou", "donc", "alors", "avec", "si", "on",
            "car", "mais", "ainsi", "puis", "comme", "tout", "un", "une",
            "le", "la", "les", "des", "du", "de", "pour", "par", "sur",
            "dans", "au", "aux",
        };

        private static readonly HashSet<char> SpanDelimiters = new HashSet<char>
        {
            '.', ';', '!', '?', '=', '<', '>', '\n', '\r',
        };

        // ── Entrée du flux ───────────────────────────────────────────────

        /// <summary>
        /// Trigger explicite. Si la popup est déjà ouverte → étend la span
        /// d'un cran à gauche. Sinon calcule la span au caret et propose.
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
                if (string.IsNullOrEmpty(text) || caret <= 0) return;

                // La fin de ¶ Word inclut un \r virtuel — le sauter, sinon
                // il est pris pour un délimiteur et la span est vide.
                while (caret > 0 && (text[caret - 1] == '\r' || text[caret - 1] == '\n')) caret--;

                int spanStart = ComputeSpanStart(text, caret, paragraph.OMathRegions);
                while (spanStart < caret && char.IsWhiteSpace(text[spanStart])) spanStart++;
                int spanEnd = caret;
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

            int newStart = ComputeSpanStart(iter.ParagraphText, boundary, iter.OMaths);
            while (newStart < iter.StringEnd && char.IsWhiteSpace(iter.ParagraphText[newStart])) newStart++;
            if (newStart >= iter.StringStart) return;

            var extended = new ZoneSpan(iter.ParagraphAbsStart, newStart, iter.StringEnd, iter.ParagraphText, iter.OMaths);
            _log($"convert: extension span=[{extended.StringStart},{extended.StringEnd}] → \"{Preview(extended.Text)}\"");
            AnalyzeAndShow(extended);
        }

        /// <summary>Moteur forest + affichage popup. Garde la zone si OK.</summary>
        private void AnalyzeAndShow(ZoneSpan zone)
        {
            MathCursor.Engine.AnalyzeResult result;
            try { result = MathCursor.Engine.ForestEngine.Analyze(zone.Text); }
            catch (Exception ex)
            {
                _log($"convert: engine error sur \"{Preview(zone.Text)}\": {ex.Message}");
                TryStatusBar(Strings.ConvertNothingRecognized);
                return;
            }
            if (result.Decision == "erreur" || result.Ranked.Count == 0)
            {
                _log($"convert: aucune lecture pour \"{Preview(zone.Text)}\"");
                TryStatusBar(Strings.ConvertNothingRecognized);
                return;
            }

            var candidates = result.Ranked.Select(c => c.Latex).ToList();
            _log($"convert: {result.Decision}, {candidates.Count} candidat(s), top=\"{candidates[0]}\"");

            EnsurePopup();
            var (x, yBelow, yAbove) = ComputeAnchor(zone);
            _zone = zone;
            _popup.ShowCandidates(candidates, x, yBelow, yAbove, zone.Text);
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
                        const double GapDip = 3.0; // petit jour sous la ligne
                        return (left / scale, (top + height) / scale + GapDip, top / scale - GapDip);
                    }
                }
            }
            catch (Exception ex) { _log("anchor_getpoint_error: " + ex.Message); }

            // Fallback : position du caret GDI (déjà sous la ligne, en DIP).
            var (cx, cy) = CaretScreenPositionReader.Read();
            return (cx, cy, cy - 20);
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

                _log($"commit: [{absStart},{absEnd}) latex=\"{latex}\" source=\"{Preview(zone.Text)}\"");
                using (new UndoRecordScope(_app, "MathCursor : conversion"))
                {
                    _inserter.Insert(absStart, absEnd, latex, zone.Text);
                }
                return true;
            }
            catch (Exception ex) { _log("commit_error: " + ex.Message); return false; }
            finally
            {
                HidePopup();
                _committing = false;
            }
        }

        // ── Navigation popup (déléguée par le hook clavier) ─────────────

        public void EnterNavMode() => _popup?.EnterNavMode();
        public bool MoveSelection(int delta) => _popup?.MoveSelection(delta) ?? false;

        public void HidePopup()
        {
            _popup?.HidePopup();
            _zone = null;
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
        }

        public void Dispose()
        {
            try { _popup?.Close(); } catch { }
            _popup = null;
        }

        // ── Internals ────────────────────────────────────────────────────

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
                var report = _buildFeedbackReport?.Invoke() ?? new Feedback.FeedbackReport();
                report.NerText = _zone?.Text ?? "";
                report.RecognizedFormula = _popup?.SelectedLatex ?? "";
                var sender = Feedback.FeedbackSenderFactory.Create();
                new FeedbackDialog(report, sender).ShowDialog();
            }
            catch (Exception ex) { _log("feedback_open_error: " + ex.Message); }
        }

        /// <summary>
        /// Début de la span : max(début ¶, après le dernier délimiteur hors
        /// brackets/parens, fin du dernier OMath avant caret, après le
        /// dernier stopword mot-entier). Logique reprise de
        /// l'ex-ManualTriggerController (comportement validé).
        /// </summary>
        internal static int ComputeSpanStart(string text, int caret,
            IReadOnlyList<(int start, int end)> omathRegions)
        {
            int start = 0;

            // Après le dernier délimiteur — walk backward, suivi profondeur.
            int bracketDepth = 0, parenDepth = 0;
            for (int k = caret - 1; k >= 0; k--)
            {
                char c = text[k];
                if (c == ']') { bracketDepth++; continue; }
                if (c == '[') { if (bracketDepth > 0) bracketDepth--; continue; }
                if (c == ')') { parenDepth++; continue; }
                if (c == '(') { if (parenDepth > 0) parenDepth--; continue; }

                if (!SpanDelimiters.Contains(c)) continue;
                if ((c == ';' || c == ',') && (bracketDepth > 0 || parenDepth > 0)) continue;
                start = Math.Max(start, k + 1);
                break;
            }

            // Après la fin du dernier OMath qui se termine avant le caret.
            if (omathRegions != null)
                foreach (var (s, e) in omathRegions)
                    if (e <= caret) start = Math.Max(start, e);

            // Après le dernier stopword (mot entier).
            int i = caret - 1;
            while (i >= start)
            {
                while (i >= start && char.IsWhiteSpace(text[i])) i--;
                if (i < start) break;
                int wordEnd = i + 1;
                while (i >= start && IsWordChar(text[i])) i--;
                int wordStart = i + 1;
                if (wordEnd <= wordStart) { i--; continue; }
                string w = text.Substring(wordStart, wordEnd - wordStart);
                if (Stopwords.Contains(w)) { start = wordEnd; break; }
            }

            return start;
        }

        private static bool IsWordChar(char c) => char.IsLetter(c) || c == '\'' || c == '-';

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
