// MathCursor — capture d'intention mathématique depuis une saisie clavier linéaire.
// Copyright (C) 2026  Côme de Percin
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

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
        private const int DebounceMs = 100;

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

        /// <summary>Caret déplacé (clic, flèches) — QOL 2026-06-12 : même
        /// debounce + même pipeline que la frappe. Cliquer au milieu d'une
        /// expression déjà tapée (re)propose la popup ; cliquer dans de la
        /// prose ferme une proposition périmée. L'appelant filtre les
        /// sélections non réduites.</summary>
        public void OnCaretMoved()
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
                if (_conversion.IsCommitting) return;
                // PAS de garde nav mode : on recalcule toujours, la popup
                // préserve nav mode + sélection au rafraîchissement
                // (retour user 2026-06-10).
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

                // Le caret doit être DANS la zone (bords inclus) : couvre la
                // frappe en fin d'expression ET l'édition au milieu d'une
                // expression déjà tapée — la zone proposée englobe alors
                // aussi ce qui est APRÈS le caret (retour user 2026-06-10).
                // Les blancs entre zone et caret sont absorbés (≤ 5).
                zone = ZoneRefiner.TryExtendForwardWhitespace(text, zone, effCaret);
                if (effCaret < zone.Start || effCaret > zone.End) { HideAuto(); return; }

                // Le NER fragmente parfois UNE formule en zones adjacentes
                // (mesuré 2026-06-12 : « (a b c d ; | e (sum x 0 1 ») et le
                // morceau au caret seul est imparsable → on tente la FUSION
                // blancs-seulement d'abord, la zone seule en repli si le
                // moteur refuse la fusion.
                var merged = ZoneRefiner.MergeWhitespaceAdjacent(filtered, text, zone);

                // Repli anti-fragmentation NER : le SpanComputer (logique Ctrl+
                // Espace) ancre sur l'ouvrante ( / [ NON fermée englobant le caret
                // et traite ; , internes comme STRUCTURELS. S'il démarre AVANT la
                // zone NER, c'est que le NER a largué la tête de la matrice (ex.
                // « (a n ;c d » → zone « c d » seule, imparsable) → on tente CE span
                // EN PREMIER : il parse, donc TryProposeAuto affiche sans HidePopup
                // intermédiaire (pas de flash). No-op quand la zone NER démarre déjà
                // à l'ouvrante ou qu'il n'y a pas de bracket ouvert (aStart >= zone.Start).
                // Cf. ADR 2026-06-29-Fix-auto-detect-anchor-unclosed-bracket.
                var attempts = new List<DetectedZone>();
                int aStart = SpanComputer.ComputeSpanStart(text, effCaret, paragraph.OMathRegions);
                int aEnd = SpanComputer.ComputeSpanEnd(text, effCaret, paragraph.OMathRegions);
                if (aStart < zone.Start && aEnd > aStart)
                    attempts.Add(new DetectedZone(aStart, aEnd, text.Substring(aStart, aEnd - aStart), 1.0));
                if (merged.Start != zone.Start || merged.End != zone.End)
                    attempts.Add(merged);
                attempts.Add(zone);

                foreach (var attempt in attempts)
                {
                    // « limite », « racine »… juste avant la zone → inclus.
                    var z2 = ZoneRefiner.ExtendBackwardWithKeyword(text, attempt, ZoneRefiner.DefaultMathPrefixKeywords);

                    int spanStart = z2.Start, spanEnd = z2.End;
                    while (spanStart < spanEnd && char.IsWhiteSpace(text[spanStart])) spanStart++;
                    while (spanEnd > spanStart && char.IsWhiteSpace(text[spanEnd - 1])) spanEnd--;
                    if (spanEnd <= spanStart) continue;

                    var span = new ZoneSpan(paragraph.ParagraphAbsStart, spanStart, spanEnd, text, paragraph.OMathRegions);
                    _log($"auto: zone NER [{spanStart},{spanEnd}] conf={z2.Confidence:F2} → \"{Preview(span.Text)}\"");
                    if (_conversion.TryProposeAuto(span)) return;
                }
            }
            catch (Exception ex) { _log("auto_detect_error: " + ex.Message); }
        }

        /// <summary>Plus de zone au caret → masque la popup. Appelé seulement
        /// depuis un tick déclenché par la FRAPPE : l'utilisateur écrit, une
        /// proposition périmée ne doit pas rester affichée (nav mode compris).</summary>
        private void HideAuto()
        {
            if (_conversion.IsPopupVisible)
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
