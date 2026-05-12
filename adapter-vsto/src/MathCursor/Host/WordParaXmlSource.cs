using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host
{
    /// <summary>
    /// Implémentation Word interop de <see cref="IParaXmlSource"/>. Toute
    /// la dépendance COM est ici, pour que <see cref="ParaXmlPrefetcher"/>
    /// reste pur et testable. Cf. P2.5 du refactor archi (ADR
    /// <c>2026-05-12-Refactor-pure-merger-atomic-insert</c>).
    /// </summary>
    internal sealed class WordParaXmlSource : IParaXmlSource
    {
        private readonly Word.Application _app;

        public WordParaXmlSource(Word.Application app)
        {
            _app = app;
        }

        public bool TryReadCurrentParagraph(out int paraStart, out string paraText)
        {
            paraStart = -1;
            paraText = null;
            try
            {
                if (_app.Documents.Count == 0) return false;
                var sel = _app.Selection;
                if (sel == null) return false;
                var range = sel.Range;
                if (range == null) return false;
                var para = range.Paragraphs[1];
                if (para == null) return false;
                paraStart = para.Range.Start;
                paraText = para.Range.Text ?? "";
                return true;
            }
            catch { return false; }
        }

        public string ReadCurrentParaXml()
        {
            try
            {
                if (_app.Documents.Count == 0) return null;
                var sel = _app.Selection;
                if (sel == null) return null;
                var para = sel.Range?.Paragraphs[1];
                return para?.Range.WordOpenXML;
            }
            catch { return null; }
        }
    }
}
