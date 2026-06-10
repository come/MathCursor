using System;
using System.Xml.Linq;
using MathCursor.Serialization;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.Blocks
{
    /// <summary>
    /// POC M0 du chantier multiligne — option A-discipliné : UN SEUL OMath
    /// contenant un <c>&lt;m:eqArr&gt;</c> (equation array natif Word),
    /// lignes alignées au signe via les marques « &amp; ». Même chaîne que
    /// le POC tableau, pour comparer LE FEEL DE MODIFICATION côte à côte :
    ///
    /// <code>
    ///   f(x) = 2x+2-2
    ///        = 2x
    ///        = 2·x
    /// </code>
    ///
    /// À torturer : Backspace (le bloc = un objet → suppression atomique),
    /// clic dedans (Word permet l'édition native de chaque ligne), Ctrl+Z,
    /// frappe avant/après. À retirer une fois A/B tranché.
    /// </summary>
    internal static class ChainEqArrPoc
    {
        private static readonly XNamespace M = LatexToOmml.M;
        private static readonly XNamespace W =
            "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        public static void Run(Word.Application app, Action<string> log = null)
        {
            log = log ?? LogDiag;
            var doc = app.ActiveDocument;
            if (doc == null) { log("poc-eqarr: pas de document actif"); return; }
            var sel = app.Selection;
            log("poc-eqarr: START");

            using (new UndoRecordScope(app, "MathCursor : POC chaîne (eqArr)"))
            {
                // Bloc 1 — chaîne simple : alignement au « = » (1 marque & par ligne).
                var simple = new XElement(M + "eqArr",
                    Line("f(x)", "=2x+2-2"),
                    Line(null, "=2x"),
                    Line(null, "=2\\cdot x"));
                InsertEqArr(app, doc, sel, simple, "simple", log);

                // Bloc 2 — chaîne d'ÉQUIVALENCES : DOUBLE alignement (2 marques
                // & par ligne) — les ⟺ s'alignent entre eux ET les = entre eux.
                var equiv = new XElement(M + "eqArr",
                    Line(null, "x+2", "=5"),
                    Line("\\Leftrightarrow ", "x", "=3"),
                    Line("\\Leftrightarrow ", "2x", "=6"));
                InsertEqArr(app, doc, sel, equiv, "équivalences (double alignement)", log);

                log("poc-eqarr: DONE");
            }
            try { app.StatusBar = "MathCursor : POC eqArr inséré (simple + double alignement) — comparer avec le POC tableau"; } catch { }
        }

        /// <summary>Insère UN bloc eqArr dans un ¶ frais (technique placeholder
        /// chirurgicale, comme OMathInserter.BuildOMathViaOmml).</summary>
        private static void InsertEqArr(Word.Application app, Word.Document doc, Word.Selection sel,
            XElement eqArr, string label, Action<string> log)
        {
            var oMath = new XElement(M + "oMath", eqArr);

            sel.TypeParagraph();
            sel.TypeText("¤");
            int phEnd = sel.Start;
            int phStart = phEnd - 1;
            var phRange = doc.Range(phStart, phEnd);

            XDocument xdoc;
            try { xdoc = XDocument.Parse(phRange.WordOpenXML); }
            catch (Exception ex) { log($"poc-eqarr: [{label}] parse WordOpenXML KO: " + ex.Message); return; }

            XElement phRun = null;
            foreach (var r in xdoc.Descendants(W + "r"))
            {
                var t = r.Element(W + "t");
                if (t != null && t.Value == "¤") { phRun = r; break; }
            }
            if (phRun == null) { log($"poc-eqarr: [{label}] placeholder introuvable"); phRange.Delete(); return; }
            phRun.ReplaceWith(oMath);

            try { phRange.InsertXML(xdoc.ToString(SaveOptions.DisableFormatting)); }
            catch (Exception ex) { log($"poc-eqarr: [{label}] InsertXML KO: " + ex.Message); phRange.Delete(); return; }
            log($"poc-eqarr: [{label}] inséré");

            // Caret après le bloc (sortie de la zone math).
            try
            {
                Word.OMath om = null;
                foreach (Word.OMath o in doc.Range(phStart, Math.Min(doc.Content.End, phStart + 300)).OMaths)
                { om = o; break; }
                if (om != null)
                {
                    sel.SetRange(om.Range.End, om.Range.End);
                    sel.MoveRight(Word.WdUnits.wdCharacter, 1, Word.WdMovementType.wdMove);
                }
            }
            catch { }
        }

        /// <summary>Une ligne de l'array : segments séparés par les marques
        /// d'alignement « &amp; » natives d'eqArr. Un segment null/vide est
        /// permis (colonne vide, ex. pas de connecteur sur la 1ʳᵉ ligne) —
        /// la marque &amp; est émise quand même pour garder les colonnes.</summary>
        private static XElement Line(params string[] segmentsLatex)
        {
            var e = new XElement(M + "e");
            for (int i = 0; i < segmentsLatex.Length; i++)
            {
                if (i > 0)
                    e.Add(new XElement(M + "r",
                        new XElement(M + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), "&")));
                if (!string.IsNullOrEmpty(segmentsLatex[i]))
                    foreach (var el in LatexToOmml.Convert(segmentsLatex[i]).Elements())
                        e.Add(el);
            }
            return e;
        }

        private static void LogDiag(string message)
        {
            try
            {
                var dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MathCursor", "logs");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "mathcursor.log"),
                    $"{DateTime.UtcNow:o} poc {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
