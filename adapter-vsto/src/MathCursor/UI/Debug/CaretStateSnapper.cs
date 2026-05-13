using System;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.UI.Debug
{
    /// <summary>
    /// Lit la Selection courante de Word et produit un <see cref="CaretStateInfo"/>
    /// inerte. Best-effort : <b>chaque accès Word interop est isolé dans son
    /// propre try/catch</b> — les collections Word (OMaths, Cells, Paragraphs)
    /// sont paresseuses et leurs accesseurs par index peuvent jeter même
    /// quand Count &gt; 0. Une COMException sur une section ne doit pas
    /// invalider les sections précédemment lues.
    /// </summary>
    internal static class CaretStateSnapper
    {
        public static CaretStateInfo Snapshot(Word.Application app)
        {
            var info = new CaretStateInfo();
            if (app == null) { info.ErrorMessage = "app null"; return info; }

            Word.Selection sel = TryGet(() => app.Selection);
            if (sel == null) { info.ErrorMessage = "selection null"; return info; }

            // Selection : start, end, OMaths count — chaque accès séparé.
            int? selStart = TryGet(() => (int?)sel.Start);
            if (selStart.HasValue) info.SelStart = selStart.Value;
            int? selEnd = TryGet(() => (int?)sel.End);
            if (selEnd.HasValue) info.SelEnd = selEnd.Value;
            int? selOMaths = TryGet(() => (int?)(sel.OMaths?.Count ?? 0));
            if (selOMaths.HasValue) info.SelOMathsCount = selOMaths.Value;

            // ¶ parent : Paragraphs[1] peut jeter, Range et ses propriétés aussi.
            Word.Paragraph para = TryGet(() =>
            {
                var paras = sel.Paragraphs;
                if (paras == null || paras.Count <= 0) return null;
                return paras[1];
            });
            if (para != null)
            {
                Word.Range paraRng = TryGet(() => para.Range);
                if (paraRng != null)
                {
                    int? ps = TryGet(() => (int?)paraRng.Start);
                    int? pe = TryGet(() => (int?)paraRng.End);
                    if (ps.HasValue) info.ParaStart = ps.Value;
                    if (pe.HasValue) info.ParaEnd = pe.Value;
                    string t = TryGet(() => paraRng.Text);
                    if (!string.IsNullOrEmpty(t))
                        info.ParaTextPreview = Truncate(
                            t.Replace('\r', '↵').Replace('\a', '⌐').Replace('\v', '↧'), 60);
                }
            }

            // Tableau ? — Information[wdWithInTable] peut jeter, idem rownum/colnum.
            bool? inTable = TryGet(() => (bool?)(bool)sel.Information[Word.WdInformation.wdWithInTable]);
            if (inTable.HasValue) info.InTable = inTable.Value;

            if (info.InTable)
            {
                int? row = TryGet(() => (int?)(int)sel.Information[Word.WdInformation.wdStartOfRangeRowNumber]);
                if (row.HasValue) info.TableRow = row.Value;
                int? col = TryGet(() => (int?)(int)sel.Information[Word.WdInformation.wdStartOfRangeColumnNumber]);
                if (col.HasValue) info.TableCol = col.Value;

                // Cells[1].Range : COLLECTION PARESSEUSE — Count peut mentir,
                // accès [1] peut jeter « Le membre de la collection requis
                // n'existe pas ». Chaque étape isolée.
                Word.Cell cell = TryGet(() =>
                {
                    var cells = sel.Cells;
                    if (cells == null) return null;
                    int count = 0;
                    try { count = cells.Count; } catch { return null; }
                    if (count <= 0) return null;
                    try { return cells[1]; } catch { return null; }
                });
                if (cell != null)
                {
                    Word.Range cellRng = TryGet(() => cell.Range);
                    if (cellRng != null)
                    {
                        int? cs = TryGet(() => (int?)cellRng.Start);
                        int? ce = TryGet(() => (int?)cellRng.End);
                        if (cs.HasValue) info.CellStart = cs.Value;
                        if (ce.HasValue) info.CellEnd = ce.Value;
                    }
                }
            }

            // OMath englobante : même pattern (Count peut mentir, [1] peut jeter).
            Word.OMath om = TryGet(() =>
            {
                var omaths = sel.OMaths;
                if (omaths == null) return null;
                int count = 0;
                try { count = omaths.Count; } catch { return null; }
                if (count <= 0) return null;
                try { return omaths[1]; } catch { return null; }
            });
            if (om != null)
            {
                Word.Range omRng = TryGet(() => om.Range);
                if (omRng != null)
                {
                    int? os = TryGet(() => (int?)omRng.Start);
                    int? oe = TryGet(() => (int?)omRng.End);
                    if (os.HasValue) info.OMathStart = os.Value;
                    if (oe.HasValue) info.OMathEnd = oe.Value;
                }
            }

            return info;
        }

        /// <summary>Wrap minimal pour un accès Word qui peut jeter. Retourne
        /// <c>default</c> sur exception, ne propage rien — l'inspecteur est
        /// best-effort, jamais bloquant.</summary>
        private static T TryGet<T>(Func<T> getter)
        {
            try { return getter(); }
            catch { return default; }
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length <= max) return s;
            return s.Substring(0, max - 1) + "…";
        }
    }
}
