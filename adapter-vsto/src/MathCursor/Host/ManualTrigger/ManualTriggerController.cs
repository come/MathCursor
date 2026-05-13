using System;
using System.Collections.Generic;
using MathCursor.Core;
using MathCursor.Core.Resolution;
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
        // Stopwords courts FR qui bornent la span du trigger manuel.
        // Mots-outils qui introduisent ou séparent des expressions math.
        private static readonly HashSet<string> Stopwords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "soit", "soient", "et", "ou", "donc", "alors", "avec", "si",
            "on", "car", "mais", "ainsi", "puis", "comme", "tout",
            "un", "une", "le", "la", "les", "des", "du", "de",
            "pour", "par", "sur", "dans", "au", "aux",
        };

        // Délimiteurs qui bornent la span. Inclut `=`/`<`/`>` pour couper
        // les relations. `,` et `:` exclus (opérateurs math légitimes).
        private static readonly char[] Delimiters =
            { '.', ';', '!', '?', '=', '<', '>', '\n', '\r' };

        private readonly Word.Application _app;
        private readonly WordContextReader _contextReader;
        private readonly Func<string, ResolvedZone> _resolveWithContext;
        private readonly Func<bool> _isCaretInOurOMath;
        private readonly Action _passThroughToPolling;
        private readonly Func<bool> _isSuggestionPopupVisible;
        private readonly Action _closeEditMode;
        private readonly Action<string> _setLastZoneSource;
        private readonly Action<ResolvedZone, int, int, int, string> _showPopupAndEnterNavMode;
        private readonly Action<string> _log;

        // État d'extension itérative — chaque Ctrl+Espace suivant tant que
        // la popup est ouverte étend la zone d'un cran (ADR 29-04).
        private string _iterativeParagraph;
        private int _iterativeParaAbsStart = -1;
        private int _iterativeSpanStart = -1;
        private int _iterativeSpanEnd = -1;
        private IReadOnlyList<(int start, int end)> _iterativeOMaths;

        public ManualTriggerController(
            Word.Application app,
            WordContextReader contextReader,
            Func<string, ResolvedZone> resolveWithContext,
            Func<bool> isCaretInOurOMath,
            Action passThroughToPolling,
            Func<bool> isSuggestionPopupVisible,
            Action closeEditMode,
            Action<string> setLastZoneSource,
            Action<ResolvedZone, int, int, int, string> showPopupAndEnterNavMode,
            Action<string> log)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _contextReader = contextReader ?? throw new ArgumentNullException(nameof(contextReader));
            _resolveWithContext = resolveWithContext ?? throw new ArgumentNullException(nameof(resolveWithContext));
            _isCaretInOurOMath = isCaretInOurOMath ?? (() => false);
            _passThroughToPolling = passThroughToPolling ?? (() => { });
            _isSuggestionPopupVisible = isSuggestionPopupVisible ?? (() => false);
            _closeEditMode = closeEditMode ?? (() => { });
            _setLastZoneSource = setLastZoneSource ?? (s => { });
            _showPopupAndEnterNavMode = showPopupAndEnterNavMode ?? ((_, __, ___, ____, _____) => { });
            _log = log ?? (s => { });
        }

        public bool HasIterativeState => _iterativeSpanStart >= 0;

        /// <summary>
        /// Trigger explicite : entrée du flow.
        /// (1) Si caret dans OMath → passthrough polling.
        /// (2) Si popup déjà ouverte + état itératif → ExtendOneStop.
        /// (3) Sinon → compute span + show popup + init iterative state.
        /// </summary>
        public void Trigger()
        {
            try
            {
                if (_app.Documents.Count == 0) return;
                if (_isCaretInOurOMath()) { _passThroughToPolling(); return; }

                if (_isSuggestionPopupVisible() && _iterativeSpanStart >= 0)
                {
                    ExtendOneStop();
                    return;
                }

                var paragraph = _contextReader.ReadCurrentParagraph();
                int caretInParagraph = paragraph.CaretOffset;
                int paragraphAbsStart = paragraph.ParagraphAbsStart;
                string text = paragraph.Text ?? "";
                if (string.IsNullOrEmpty(text) || caretInParagraph <= 0) return;

                int spanStart = ComputeSpanStart(text, caretInParagraph, paragraph.OMathRegions);
                // Trim ws aux bords (mais pas les opérateurs unaires +/-).
                while (spanStart < caretInParagraph && char.IsWhiteSpace(text[spanStart])) spanStart++;
                int spanEnd = caretInParagraph;
                while (spanEnd > spanStart && char.IsWhiteSpace(text[spanEnd - 1])) spanEnd--;
                if (spanEnd <= spanStart) return;

                string span = text.Substring(spanStart, spanEnd - spanStart);
                _log($"manual trigger span=[{spanStart},{spanEnd}] → \"{Preview(span)}\"");

                ResolvedZone resolved;
                try { resolved = _resolveWithContext(span); }
                catch (Exception ex) { _log("manual_engine_error: " + ex.Message); return; }

                int absStart = paragraphAbsStart + spanStart;
                int absEnd = paragraphAbsStart + spanEnd;

                _setLastZoneSource(span);
                _closeEditMode();

                if (string.IsNullOrEmpty(resolved.TopLatex)) return;
                _showPopupAndEnterNavMode(resolved, absStart, absEnd, span.Length, "manuel: " + span);

                // Initialise l'état d'extension itérative.
                _iterativeParagraph = text;
                _iterativeParaAbsStart = paragraphAbsStart;
                _iterativeSpanStart = spanStart;
                _iterativeSpanEnd = spanEnd;
                _iterativeOMaths = paragraph.OMathRegions;
            }
            catch (Exception ex) { _log("manual_trigger_error: " + ex.Message); }
        }

        /// <summary>Reset l'état d'extension itérative.</summary>
        public void Reset()
        {
            if (_iterativeSpanStart < 0) return;
            _iterativeParagraph = null;
            _iterativeParaAbsStart = -1;
            _iterativeSpanStart = -1;
            _iterativeSpanEnd = -1;
            _iterativeOMaths = null;
        }

        /// <summary>
        /// Initialise l'état d'extension itérative depuis une zone détectée
        /// automatiquement par le NER. Permet à Ctrl+Espace suivants d'étendre
        /// cette zone même si la popup vient du polling auto, pas du manual
        /// trigger. Cf. ADR 29-04 iterative-zone-expansion.
        /// </summary>
        public void InitFromAutoZone(string paragraph, int paragraphAbsStart,
            int spanStart, int spanEnd, IReadOnlyList<(int start, int end)> omathRegions)
        {
            _iterativeParagraph = paragraph ?? "";
            _iterativeParaAbsStart = paragraphAbsStart;
            _iterativeSpanStart = spanStart;
            _iterativeSpanEnd = spanEnd;
            _iterativeOMaths = omathRegions;
        }

        // ── Internals ─────────────────────────────────────────────────

        /// <summary>
        /// Étend la span itérative d'un cran à gauche. Passe outre la borne
        /// courante (délim/stopword qui bloquait) et cherche la borne suivante
        /// en amont. Si la borne est dans un OMath → STOP FINAL.
        /// </summary>
        private void ExtendOneStop()
        {
            if (string.IsNullOrEmpty(_iterativeParagraph))
            {
                _log("iterative extend: empty paragraph, no-op");
                return;
            }
            if (_iterativeSpanStart <= 0)
            {
                _log("iterative extend: at paragraph start (spanStart=0), no-op");
                return;
            }

            // Recule d'un cran au-delà de la borne courante.
            int boundary = _iterativeSpanStart - 1;
            while (boundary >= 0 && char.IsWhiteSpace(_iterativeParagraph[boundary])) boundary--;
            if (boundary < 0)
            {
                _log("iterative extend: at paragraph start, no-op");
                return;
            }

            // Borne dans un OMath = stop final.
            if (_iterativeOMaths != null)
            {
                foreach (var (s, e) in _iterativeOMaths)
                {
                    if (s <= boundary && boundary < e)
                    {
                        _log($"iterative extend: blocked by OMath at [{s},{e}], no-op (stop final)");
                        return;
                    }
                }
            }

            int newStart = ComputeSpanStart(_iterativeParagraph, boundary, _iterativeOMaths);
            while (newStart < _iterativeSpanEnd && char.IsWhiteSpace(_iterativeParagraph[newStart])) newStart++;

            if (newStart >= _iterativeSpanStart)
            {
                _log($"iterative extend no-op: spanStart={_iterativeSpanStart} unchanged (newStart={newStart}, boundary={boundary})");
                return;
            }

            _iterativeSpanStart = newStart;
            string span = _iterativeParagraph.Substring(_iterativeSpanStart, _iterativeSpanEnd - _iterativeSpanStart);
            _log($"iterative extend: span=[{_iterativeSpanStart},{_iterativeSpanEnd}] → \"{Preview(span)}\"");

            ResolvedZone resolved;
            try { resolved = _resolveWithContext(span); }
            catch (Exception ex) { _log("iterative_extend_error: " + ex.Message); return; }
            if (string.IsNullOrEmpty(resolved.TopLatex)) return;

            int absStart = _iterativeParaAbsStart + _iterativeSpanStart;
            int absEnd = _iterativeParaAbsStart + _iterativeSpanEnd;
            _setLastZoneSource(span);
            _closeEditMode();
            _showPopupAndEnterNavMode(resolved, absStart, absEnd, span.Length, "iterative: " + span);
        }

        /// <summary>
        /// Trouve le début de la span manuelle en remontant depuis le caret.
        /// Boundary = max(début ¶, fin du dernier OMath avant caret, position
        /// après le dernier délimiteur, position après le dernier stopword).
        /// Détail : `;` et `,` ne sont des délimiteurs QUE hors brackets/parens
        /// (sinon ce sont des séparateurs d'intervalle ou d'args de fonction).
        /// </summary>
        public static int ComputeSpanStart(string text, int caret, IReadOnlyList<(int start, int end)> omathRegions)
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

                if (Array.IndexOf(Delimiters, c) < 0) continue;
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
                if (Stopwords.Contains(w))
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
