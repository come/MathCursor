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
                // L'oMath eqArr : une <m:e> par ligne, « & » = point d'alignement
                // (juste AVANT le signe → tous les signes s'alignent).
                var eqArr = new XElement(M + "eqArr",
                    Line("f(x)", "=2x+2-2"),
                    Line(null, "=2x"),
                    Line(null, "=2\\cdot x"));
                var oMath = new XElement(M + "oMath", eqArr);

                // ¶ frais + insertion chirurgicale (même technique placeholder
                // qu'OMathInserter.BuildOMathViaOmml).
                sel.TypeParagraph();
                sel.TypeText("¤");
                int phEnd = sel.Start;
                int phStart = phEnd - 1;
                var phRange = doc.Range(phStart, phEnd);

                XDocument xdoc;
                try { xdoc = XDocument.Parse(phRange.WordOpenXML); }
                catch (Exception ex) { log("poc-eqarr: parse WordOpenXML KO: " + ex.Message); return; }

                XElement phRun = null;
                foreach (var r in xdoc.Descendants(W + "r"))
                {
                    var t = r.Element(W + "t");
                    if (t != null && t.Value == "¤") { phRun = r; break; }
                }
                if (phRun == null) { log("poc-eqarr: placeholder introuvable"); phRange.Delete(); return; }
                phRun.ReplaceWith(oMath);

                try { phRange.InsertXML(xdoc.ToString(SaveOptions.DisableFormatting)); }
                catch (Exception ex) { log("poc-eqarr: InsertXML KO: " + ex.Message); phRange.Delete(); return; }
                log("poc-eqarr: eqArr 3 lignes inséré");

                // Caret après le bloc.
                try
                {
                    Word.OMath om = null;
                    foreach (Word.OMath o in doc.Range(phStart, Math.Min(doc.Content.End, phStart + 200)).OMaths)
                    { om = o; break; }
                    if (om != null)
                    {
                        sel.SetRange(om.Range.End, om.Range.End);
                        sel.MoveRight(Word.WdUnits.wdCharacter, 1, Word.WdMovementType.wdMove);
                    }
                }
                catch { }
                log("poc-eqarr: DONE");
            }
            try { app.StatusBar = "MathCursor : POC eqArr inséré — comparer le feel avec le POC tableau"; } catch { }
        }

        /// <summary>Une ligne de l'array : [lhs] &amp; [marqueur+rhs]. Le
        /// « &amp; » est la marque d'alignement native d'eqArr.</summary>
        private static XElement Line(string lhsLatex, string rhsLatex)
        {
            var e = new XElement(M + "e");
            if (!string.IsNullOrEmpty(lhsLatex))
                foreach (var el in LatexToOmml.Convert(lhsLatex).Elements())
                    e.Add(el);
            e.Add(new XElement(M + "r",
                new XElement(M + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), "&")));
            foreach (var el in LatexToOmml.Convert(rhsLatex).Elements())
                e.Add(el);
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
