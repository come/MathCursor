using System;
using System.Collections.Generic;
using MathCursor.Core;
using MathCursor.Core.Resolution;
using MathCursor.Host.Detection;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.ManualTrigger
{
    /// <summary>
    /// Bounded context "user trigger explicite" (Ctrl+Espace).
    ///
    /// <para>Bypass le NER, calcule la span texte autour du caret par
    /// remontée jusqu'au premier séparateur (délimiteur ponctuation,
    /// stopword mot-outil, fin d'OMath précédent ou début ¶), passe au
    /// resolver, affiche la popup. Si la popup est déjà ouverte et qu'on
    /// a un état d'extension itérative actif, étend la span d'un cran à
    /// gauche au lieu de re-détecter.</para>
    ///
    /// <para>P2.16 du refactor archi. Extrait du god class SuggestionService.</para>
    /// </summary>
    internal sealed class ManualTriggerController
    {
        // Stopwords + Delimiters : maintenant data-driven via LocaleVocabulary
        // (= data-v2/locale/fr.yml `stopwords:` + `span_delimiters:`).
        // Migration Chantier 1 — 2026-05-25.

        private readonly Word.Application _app;
        private readonly WordContextReader _contextReader;
        private readonly Func<string, ResolvedZone> _resolveWithContext;
        private readonly Func<bool> _isCaretInOurOMath;
        private readonly Action _passThroughToPolling;
        private readonly Func<bool> _isSuggestionPopupVisible;
        private readonly Action _closeEditMode;
        private readonly Action<ResolvedZone, ZoneSpan, int, string> _showPopupAndEnterNavMode;
        private readonly Action<string> _log;
        private readonly MathCursor.Engine.Vocabulary.LocaleVocabulary _vocab;

        // État d'extension itérative — chaque Ctrl+Espace suivant tant que
        // la popup est ouverte étend la zone d'un cran (ADR 29-04).
        // Cf. ADR 2026-05-23-Refactor-zonespan-popup-commit-coords pour
        // le passage de 5 fields séparés à un seul ZoneSpan.
        private ZoneSpan _iterativeSpan;

        public ManualTriggerController(
            Word.Application app,
            WordContextReader contextReader,
            Func<string, ResolvedZone> resolveWithContext,
            Func<bool> isCaretInOurOMath,
            Action passThroughToPolling,
            Func<bool> isSuggestionPopupVisible,
            Action closeEditMode,
            Action<ResolvedZone, ZoneSpan, int, string> showPopupAndEnterNavMode,
            Action<string> log,
            MathCursor.Engine.Vocabulary.LocaleVocabulary vocab)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _contextReader = contextReader ?? throw new ArgumentNullException(nameof(contextReader));
            _resolveWithContext = resolveWithContext ?? throw new ArgumentNullException(nameof(resolveWithContext));
            _isCaretInOurOMath = isCaretInOurOMath ?? (() => false);
            _passThroughToPolling = passThroughToPolling ?? (() => { });
            _isSuggestionPopupVisible = isSuggestionPopupVisible ?? (() => false);
            _closeEditMode = closeEditMode ?? (() => { });
            _showPopupAndEnterNavMode = showPopupAndEnterNavMode ?? ((_, __, ___, ____) => { });
            _log = log ?? (s => { });
            _vocab = vocab ?? throw new ArgumentNullException(nameof(vocab));
        }

        public bool HasIterativeState => _iterativeSpan != null;

        /// <summary>
        /// Trigger explicite : entrée du flow.
        /// (1) Si caret dans OMath → passthrough polling.
        /// (2) Si popup déjà ouverte + état itératif → ExtendOneStop.
        /// (3) Sinon → compute span + show popup + init iterative state.
        /// </summary>
        public void Trigger()
        {
            _log("[CTRL+SPACE] Trigger() called");
            try
            {
                if (_app.Documents.Count == 0)
                {
                    _log("[CTRL+SPACE] ABORT: Documents.Count==0");
                    return;
                }
                bool inOMath = _isCaretInOurOMath();
                _log($"[CTRL+SPACE] isCaretInOurOMath={inOMath}");
                if (inOMath) { _passThroughToPolling(); return; }

                bool popupVisible = _isSuggestionPopupVisible();
                _log($"[CTRL+SPACE] popupVisible={popupVisible} hasIterative={HasIterativeState}");
                if (popupVisible && HasIterativeState)
                {
                    _log("[CTRL+SPACE] → ExtendOneStop");
                    ExtendOneStop();
                    return;
                }

                var paragraph = _contextReader.ReadCurrentParagraph();
                int caretInParagraph = paragraph.CaretOffset;
                int paragraphAbsStart = paragraph.ParagraphAbsStart;
                string text = paragraph.Text ?? "";
                _log($"[CTRL+SPACE] paragraph text=\"{Preview(text)}\" caret={caretInParagraph} omaths={paragraph.OMathRegions?.Count ?? 0}");
                if (string.IsNullOrEmpty(text) || caretInParagraph <= 0)
                {
                    _log("[CTRL+SPACE] ABORT: empty text OR caret<=0");
                    return;
                }

                // Skip \r/\n en fin (= la fin de para Word inclut un \r
                // virtuel qui sinon est traité comme délimiteur et fait
                // que ComputeSpanStart retourne caret directement → span vide).
                int effectiveCaret = caretInParagraph;
                while (effectiveCaret > 0
                    && (text[effectiveCaret - 1] == '\r' || text[effectiveCaret - 1] == '\n'))
                    effectiveCaret--;
                if (effectiveCaret != caretInParagraph)
                    _log($"[CTRL+SPACE] effectiveCaret adjusted {caretInParagraph}→{effectiveCaret} (skip \\r\\n)");

                int spanStart = ComputeSpanStart(text, effectiveCaret, paragraph.OMathRegions, _vocab);
                _log($"[CTRL+SPACE] ComputeSpanStart raw spanStart={spanStart}");
                while (spanStart < effectiveCaret && char.IsWhiteSpace(text[spanStart])) spanStart++;
                int spanEnd = effectiveCaret;
                while (spanEnd > spanStart && char.IsWhiteSpace(text[spanEnd - 1])) spanEnd--;
                _log($"[CTRL+SPACE] after trim span=[{spanStart},{spanEnd}]");
                if (spanEnd <= spanStart)
                {
                    _log("[CTRL+SPACE] ABORT: spanEnd<=spanStart (empty after trim)");
                    return;
                }

                var zone = new ZoneSpan(paragraphAbsStart, spanStart, spanEnd, text, paragraph.OMathRegions);
                _log($"manual trigger span=[{spanStart},{spanEnd}] → \"{Preview(zone.Text)}\"");

                ResolvedZone resolved;
                try { resolved = _resolveWithContext(zone.Text); }
                catch (Exception ex) { _log("manual_engine_error: " + ex.Message); return; }

                _closeEditMode();

                bool hasPatterns = resolved.PatternCompletions != null
                    && resolved.PatternCompletions.Count > 0;
                _log($"[CTRL+SPACE] resolved top=\"{Preview(resolved.TopLatex)}\" hasPatterns={hasPatterns}");
                if (string.IsNullOrEmpty(resolved.TopLatex) && !hasPatterns)
                {
                    _log("[CTRL+SPACE] ABORT: top empty AND no patterns");
                    return;
                }
                _log("[CTRL+SPACE] → ShowPopupAndEnterNavMode");
                _showPopupAndEnterNavMode(resolved, zone, zone.Text.Length, "manuel: " + zone.Text);

                // Initialise l'état d'extension itérative.
                _iterativeSpan = zone;
            }
            catch (Exception ex) { _log("manual_trigger_error: " + ex.Message); }
        }

        /// <summary>Reset l'état d'extension itérative.</summary>
        public void Reset()
        {
            _iterativeSpan = null;
        }

        /// <summary>
        /// Initialise l'état d'extension itérative depuis une zone détectée
        /// automatiquement par le NER. Permet à Ctrl+Espace suivants d'étendre
        /// cette zone même si la popup vient du polling auto, pas du manual
        /// trigger. Cf. ADR 29-04 iterative-zone-expansion.
        /// </summary>
        public void InitFromAutoZone(ZoneSpan zone)
        {
            _iterativeSpan = zone;
        }

        // ── Internals ─────────────────────────────────────────────────

        /// <summary>
        /// Étend la span itérative d'un cran à gauche. Passe outre la borne
        /// courante (délim/stopword qui bloquait) et cherche la borne suivante
        /// en amont. Si la borne est dans un OMath → STOP FINAL.
        /// </summary>
        private void ExtendOneStop()
        {
            var iter = _iterativeSpan;
            if (iter == null || string.IsNullOrEmpty(iter.ParagraphText))
            {
                _log("iterative extend: empty paragraph, no-op");
                return;
            }
            if (iter.StringStart <= 0)
            {
                _log("iterative extend: at paragraph start (spanStart=0), no-op");
                return;
            }

            // Recule d'un cran au-delà de la borne courante.
            int boundary = iter.StringStart - 1;
            while (boundary >= 0 && char.IsWhiteSpace(iter.ParagraphText[boundary])) boundary--;
            if (boundary < 0)
            {
                _log("iterative extend: at paragraph start, no-op");
                return;
            }

            // Borne dans un OMath = stop final.
            foreach (var (s, e) in iter.OMaths)
            {
                if (s <= boundary && boundary < e)
                {
                    _log($"iterative extend: blocked by OMath at [{s},{e}], no-op (stop final)");
                    return;
                }
            }

            int newStart = ComputeSpanStart(iter.ParagraphText, boundary, iter.OMaths, _vocab);
            while (newStart < iter.StringEnd && char.IsWhiteSpace(iter.ParagraphText[newStart])) newStart++;

            if (newStart >= iter.StringStart)
            {
                _log($"iterative extend no-op: spanStart={iter.StringStart} unchanged (newStart={newStart}, boundary={boundary})");
                return;
            }

            var extended = new ZoneSpan(iter.ParagraphAbsStart, newStart, iter.StringEnd, iter.ParagraphText, iter.OMaths);
            _log($"iterative extend: span=[{extended.StringStart},{extended.StringEnd}] → \"{Preview(extended.Text)}\"");

            ResolvedZone resolved;
            try { resolved = _resolveWithContext(extended.Text); }
            catch (Exception ex) { _log("iterative_extend_error: " + ex.Message); return; }
            // Guard P9g (2026-05-21) : voir SuggestionService.cs ligne 1062.
            bool hasPatternsIter = resolved.PatternCompletions != null
                && resolved.PatternCompletions.Count > 0;
            if (string.IsNullOrEmpty(resolved.TopLatex) && !hasPatternsIter) return;

            _iterativeSpan = extended;
            _closeEditMode();
            _showPopupAndEnterNavMode(resolved, extended, extended.Text.Length, "iterative: " + extended.Text);
        }

        /// <summary>
        /// Trouve le début de la span manuelle en remontant depuis le caret.
        /// Boundary = max(début ¶, fin du dernier OMath avant caret, position
        /// après le dernier délimiteur, position après le dernier stopword).
        /// Détail : `;` et `,` ne sont des délimiteurs QUE hors brackets/parens
        /// (sinon ce sont des séparateurs d'intervalle ou d'args de fonction).
        ///
        /// <para>Data-driven : stopwords + delimiters viennent de
        /// <paramref name="vocab"/> (= YAML <c>data-v2/locale/fr.yml</c>),
        /// pas de hardcoded. Migration Chantier 1 — 2026-05-25.</para>
        /// </summary>
        public static int ComputeSpanStart(string text, int caret,
            IReadOnlyList<(int start, int end)> omathRegions,
            MathCursor.Engine.Vocabulary.LocaleVocabulary vocab)
        {
            int start = 0;

            // Après le dernier délimiteur — walk backward avec suivi profondeur brackets/parens.
            int bracketDepth = 0;
            int parenDepth = 0;
            for (int k = caret - 1; k >= 0; k--)
            {
                char c = text[k];
                if (c == ']') { bracketDepth++; continue; }
                if (c == '[') { if (bracketDepth > 0) bracketDepth--; continue; }
                if (c == ')') { parenDepth++; continue; }
                if (c == '(') { if (parenDepth > 0) parenDepth--; continue; }

                if (!vocab.SpanDelimiters.Contains(c)) continue;
                if ((c == ';' || c == ',') && (bracketDepth > 0 || parenDepth > 0)) continue;
                start = Math.Max(start, k + 1);
                break;
            }

            // Après la fin du dernier OMath qui se termine avant le caret.
            if (omathRegions != null)
            {
                foreach (var (s, e) in omathRegions)
                {
                    if (e <= caret) start = Math.Max(start, e);
                }
            }

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
                if (vocab.Stopwords.Contains(w))
                {
                    start = wordEnd;
                    break;
                }
            }

            return start;
        }

        private static bool IsWordChar(char c) => char.IsLetter(c) || c == '\'' || c == '-';

        private static string Preview(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length > 30 ? s.Substring(0, 30) + "..." : s;
        }
    }
}
