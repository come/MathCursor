using System;
using MathCursor.HostContract;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host
{
    /// <summary>
    /// Source UNIQUE de vérité pour la règle "lecture du contexte autour du curseur".
    ///
    /// Règle absolue (cf. briefs/architecture-flow.md §1) : on ne traverse JAMAIS
    /// un saut de ligne. Le contexte est borné par le paragraphe courant via
    /// Selection.Paragraphs[1].Range.
    ///
    /// Utilisé par VstoDocumentHost.ReadContextAroundCaretAsync (pour le pipeline
    /// déclenché par Tab) et par SuggestionService (pour la popup polling).
    /// </summary>
    internal sealed class WordContextReader
    {
        private readonly Word.Application _app;

        public WordContextReader(Word.Application app)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
        }

        /// <summary>Lit jusqu'à <paramref name="charsBefore"/> caractères avant le curseur.</summary>
        public string ReadBefore(int charsBefore)
        {
            var doc = _app.ActiveDocument;
            if (doc == null) return "";
            try
            {
                var sel = _app.Selection;
                int caretPos = sel.Start;
                int paraStart = ParaStart(sel, doc);
                int start = Math.Max(paraStart, caretPos - charsBefore);
                if (start >= caretPos) return "";
                return doc.Range(start, caretPos).Text ?? "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>Lit le contexte autour du curseur (avant + après), borné par le paragraphe.</summary>
        public ContextText ReadAround(int charsBefore, int charsAfter)
        {
            var empty = new ContextText { TextBefore = "", TextAfter = "", CaretOffset = 0 };
            var doc = _app.ActiveDocument;
            if (doc == null) return empty;
            try
            {
                var sel = _app.Selection;
                int caretPos = sel.Start;
                int paraStart = ParaStart(sel, doc);
                int paraEnd = ParaEnd(sel, doc);

                int startOffset = Math.Max(paraStart, caretPos - charsBefore);
                int endOffset = Math.Min(paraEnd, caretPos + charsAfter);

                string textBefore = caretPos > startOffset
                    ? (doc.Range(startOffset, caretPos).Text ?? "")
                    : "";
                string textAfter = endOffset > caretPos
                    ? (doc.Range(caretPos, endOffset).Text ?? "")
                    : "";

                return new ContextText
                {
                    TextBefore = textBefore,
                    TextAfter = textAfter,
                    CaretOffset = textBefore.Length,
                };
            }
            catch
            {
                return empty;
            }
        }

        private static int ParaStart(Word.Selection sel, Word.Document doc)
        {
            try { return sel.Paragraphs[1].Range.Start; }
            catch { return doc.Content.Start; }
        }

        private static int ParaEnd(Word.Selection sel, Word.Document doc)
        {
            try { return sel.Paragraphs[1].Range.End; }
            catch { return doc.Content.End; }
        }
    }
}
