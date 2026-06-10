using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Threading;
using MathCursor.Detection;
using MathCursor.Host.Detection;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host
{
    /// <summary>
    /// Auto-détection NER en cours de frappe (ADR 2026-06-10-Feat-ner-auto-
    /// detection-debounce). PAS de polling : le hook clavier réarme un timer
    /// one-shot (<see cref="DebounceMs"/>) à chaque frappe texte — la
    /// détection ne tourne qu'à la PAUSE de frappe, rien ne tourne au repos.
    ///
    /// Pipeline au tick :
    /// <code>
    /// guards (réglage off / commit / nav / popup edit / caret dans OMath /
    ///         signal de sortie tab|double-espace)
    ///   → WordContextReader (¶, OMaths masquées)
    ///   → NerInputWindow (fenêtre entre OMaths voisines)
    ///   → MathNerDetector.Detect → coords retraduites ¶
    ///   → ZoneRefiner (filtre OMaths, plus proche du caret, extensions)
    ///   → zone finit AU caret ? → ConversionController.TryProposeAuto
    /// </code>
    ///
    /// Le détecteur est attaché APRÈS chargement async du modèle
    /// (<see cref="AttachDetector"/>) — sans modèle, tout reste inerte et
    /// Ctrl+Espace fonctionne normalement.
    /// </summary>
    internal sealed class AutoDetectController : IDisposable
    {
        private const int DebounceMs = 400;

        private readonly Word.Application _app;
        private readonly ConversionController _conversion;
        private readonly Func<bool> _isEditPopupVisible;
        private readonly WordContextReader _contextReader;
        private readonly Action<string> _log;
        private readonly DispatcherTimer _timer;

        private MathNerDetector _detector; // null tant que le modèle n'est pas chargé

        public AutoDetectController(
            Word.Application app,
            ConversionController conversion,
            Func<bool> isEditPopupVisible,
            Action<string> log = null)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _conversion = conversion ?? throw new ArgumentNullException(nameof(conversion));
            _isEditPopupVisible = isEditPopupVisible ?? (() => false);
            _log = log ?? LogDiag;
            _contextReader = new WordContextReader(app);
            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(DebounceMs),
            };
            _timer.Tick += (_, __) => { _timer.Stop(); RunDetection(); };
        }

        /// <summary>Vrai si le modèle NER est chargé (auto-détection possible).</summary>
        public bool IsReady => _detector != null;

        /// <summary>Branche le détecteur une fois le modèle chargé (thread-safe :
        /// simple affectation de référence, lue au tick).</summary>
        public void AttachDetector(MathNerDetector detector)
        {
            _detector = detector;
            _log("auto: détecteur NER attaché, auto-détection active");
        }

        /// <summary>Frappe texte observée par le hook → réarme le debounce.</summary>
        public void OnTextKeyTyped()
        {
            if (_detector == null) return;
            if (!Settings.SettingsStore.Current.AutoDetect) return;
            _timer.Stop();
            _timer.Start();
        }

        public void Dispose()
        {
            try { _timer.Stop(); } catch { }
        }

        // ── Pipeline ─────────────────────────────────────────────────────

        private void RunDetection()
        {
            try
            {
                var detector = _detector;
                if (detector == null) return;
                if (!Settings.SettingsStore.Current.AutoDetect) return;
                if (_conversion.IsCommitting || _conversion.IsNavMode) return;
                if (_isEditPopupVisible()) return;
                if (_app.Documents.Count == 0) return;

                var paragraph = _contextReader.ReadCurrentParagraph();
                string text = paragraph.Text ?? "";
                int caret = paragraph.CaretOffset;
                if (text.Length == 0 || caret <= 0) { HideAuto(); return; }

                // Caret DANS une OMath → c'est le mode édition qui gère.
                foreach (var (s, e) in paragraph.OMathRegions)
                    if (caret > s && caret < e) return;

                // Skip le \r virtuel de fin de ¶.
                int effCaret = caret;
                while (effCaret > 0 && (text[effCaret - 1] == '\r' || text[effCaret - 1] == '\n')) effCaret--;
                if (effCaret <= 0) { HideAuto(); return; }

                // Signal de sortie : tab ou double-espace = « pas maintenant »
                // (un espace simple est toléré, cf. brief architecture-flow §2.1).
                if (text[effCaret - 1] == '\t'
                    || (effCaret >= 2 && text[effCaret - 1] == ' ' && text[effCaret - 2] == ' '))
                { HideAuto(); return; }

                // Fenêtre NER entre les OMaths voisines du caret.
                var window = NerInputWindow.Compute(text, paragraph.OMathRegions, effCaret);
                if (string.IsNullOrWhiteSpace(window.Input)) { HideAuto(); return; }

                IReadOnlyList<DetectedZone> zones;
                try { zones = detector.Detect(window.Input); }
                catch (Exception ex) { _log("auto_ner_error: " + ex.Message); return; }

                // Coords fenêtre → coords ¶.
                var translated = new List<DetectedZone>(zones.Count);
                foreach (var z in zones)
                    translated.Add(new DetectedZone(z.Start + window.LeftCut, z.End + window.LeftCut, z.Text, z.Confidence));

                var filtered = ZoneRefiner.FilterOutOMathOverlap(translated, paragraph.OMathRegions);
                if (filtered.Count == 0) { HideAuto(); return; }

                var zone = ZoneRefiner.PickNearestZone(filtered, effCaret, out _);
                if (zone == null) { HideAuto(); return; }

                // « En cours de frappe » = la zone se termine AU caret (les
                // blancs entre zone et caret sont absorbés, ≤ 5).
                zone = ZoneRefiner.TryExtendForwardWhitespace(text, zone, effCaret);
                if (zone.End != effCaret || effCaret < zone.Start) { HideAuto(); return; }

                // « limite », « racine »… juste avant la zone → inclus.
                zone = ZoneRefiner.ExtendBackwardWithKeyword(text, zone, ZoneRefiner.DefaultMathPrefixKeywords);

                int spanStart = zone.Start, spanEnd = zone.End;
                while (spanStart < spanEnd && char.IsWhiteSpace(text[spanStart])) spanStart++;
                while (spanEnd > spanStart && char.IsWhiteSpace(text[spanEnd - 1])) spanEnd--;
                if (spanEnd <= spanStart) { HideAuto(); return; }

                var span = new ZoneSpan(paragraph.ParagraphAbsStart, spanStart, spanEnd, text, paragraph.OMathRegions);
                _log($"auto: zone NER [{spanStart},{spanEnd}] conf={zone.Confidence:F2} → \"{Preview(span.Text)}\"");
                _conversion.TryProposeAuto(span);
            }
            catch (Exception ex) { _log("auto_detect_error: " + ex.Message); }
        }

        /// <summary>Plus de zone au caret → masque la popup auto (jamais en
        /// nav mode : la sélection en cours appartient à l'utilisateur).</summary>
        private void HideAuto()
        {
            if (_conversion.IsPopupVisible && !_conversion.IsNavMode)
                _conversion.HidePopup();
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
                    $"{DateTime.UtcNow:o} auto {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
