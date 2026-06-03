using System;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.Debug
{
    /// <summary>
    /// Boutons de debug pour l'insertion OMML (pilotés depuis le ruban via
    /// <see cref="MathCursor.RibbonCallback"/>). Deux familles :
    ///
    /// <list type="bullet">
    /// <item><b>Échappement caret</b> (<c>RunEscape_*</c>, <c>RunEscapeTable/List_*</c>) :
    /// insèrent f(x) en OMML, tentent UNE technique de sortie de la zone math,
    /// tapent « ABC » et jugent <c>&lt;w:t&gt;</c> (texte plat ✓) vs
    /// <c>&lt;m:t&gt;</c> (math ✗). A élu <c>MoveRight</c> (câblé en prod dans
    /// <c>InsertOMathAt</c> step 7).</item>
    /// <item><b>Sanity</b> : <see cref="RunPerfProbe"/> (iso-perf : docSize vs
    /// WordOpenXML/timing) et <see cref="RunOmmlBattery"/> (rendu visuel de 15
    /// constructions OMML).</item>
    /// </list>
    ///
    /// Les anciennes variantes d'exploration (E, G, G1-G4, POC inline/full/delete)
    /// ont été retirées — cf. ADR 2026-06-02-Feat-omml-insertion (palier acté).
    /// </summary>
    internal static class OMathInsertVariants
    {
        private const string DefaultSource = "g(x)=1/x";

        // ═══ POC ÉCHAPPEMENT CARET (zone math sticky) ════════════════════
        // Après un insert OMML, le caret posé en om.Range.End reste "dans" la
        // math → la frappe suivante sort en italique math (bug retour-saisie +
        // □-leak adjacent). Chaque bouton tente UNE technique d'échappement,
        // tape "ABC", puis JUGE : ABC en <w:t> (texte plat = ✓) ou <m:t>
        // (math = ✗). Élu : MoveRight (câblé en prod, InsertOMathAt step 7).

        public static string RunEscape_Baseline(Word.Application app)
            => RunCaretEscape(app, "0 BASELINE (aucun échappement — reproduit le bug)",
                (doc, sel, om) => sel.SetRange(om.Range.End, om.Range.End));

        public static string RunEscape_MoveRight(Word.Application app)
            => RunCaretEscape(app, "1 MoveRight 1 char",
                (doc, sel, om) => { sel.SetRange(om.Range.End, om.Range.End); sel.MoveRight(Word.WdUnits.wdCharacter, 1, Word.WdMovementType.wdMove); });

        public static string RunEscape_MoveRightLeft(Word.Application app)
            => RunCaretEscape(app, "2 MoveRight puis MoveLeft (sortie/retour flèche)",
                (doc, sel, om) => { sel.SetRange(om.Range.End, om.Range.End); sel.MoveRight(Word.WdUnits.wdCharacter, 1, Word.WdMovementType.wdMove); sel.MoveLeft(Word.WdUnits.wdCharacter, 1, Word.WdMovementType.wdMove); });

        public static string RunEscape_EndKey(Word.Application app)
            => RunCaretEscape(app, "3 EndKey wdLine",
                (doc, sel, om) => { sel.SetRange(om.Range.End, om.Range.End); sel.EndKey(Word.WdUnits.wdLine, Word.WdMovementType.wdMove); });

        public static string RunEscape_ItalicOff(Word.Application app)
            => RunCaretEscape(app, "4 Font.Italic=0 + Name Calibri au caret",
                (doc, sel, om) => { sel.SetRange(om.Range.End, om.Range.End); try { sel.Font.Italic = 0; sel.Font.Name = "Calibri"; } catch { } });

        public static string RunEscape_TrailingSpace(Word.Application app)
            => RunCaretEscape(app, "5 Type espace après om puis recale le caret après",
                (doc, sel, om) => { sel.SetRange(om.Range.End, om.Range.End); sel.TypeText(" "); int after = sel.Start; sel.SetRange(after, after); });

        public static string RunEscape_RangeAfterPlusOne(Word.Application app)
            => RunCaretEscape(app, "6 doc.Range(om.End+1) collapsed",
                (doc, sel, om) => { int p = System.Math.Min(doc.Content.End, om.Range.End + 1); sel.SetRange(p, p); });

        // ── Échappement EN TABLEAU et EN LISTE (MoveRight vs EndKey) ──────
        // En cellule, MoveRight en fin de cellule peut sauter à la cellule
        // suivante ; en liste la ligne diffère. On vérifie la technique
        // gagnante (MoveRight) dans ces décors + EndKey en comparaison.

        public static string RunEscapeTable_MoveRight(Word.Application app)
            => RunCaretEscape(app, "TABLEAU — MoveRight 1 char",
                (doc, sel, om) => { sel.SetRange(om.Range.End, om.Range.End); sel.MoveRight(Word.WdUnits.wdCharacter, 1, Word.WdMovementType.wdMove); },
                DecorTableCell);

        public static string RunEscapeTable_EndKey(Word.Application app)
            => RunCaretEscape(app, "TABLEAU — EndKey wdLine",
                (doc, sel, om) => { sel.SetRange(om.Range.End, om.Range.End); sel.EndKey(Word.WdUnits.wdLine, Word.WdMovementType.wdMove); },
                DecorTableCell);

        public static string RunEscapeList_MoveRight(Word.Application app)
            => RunCaretEscape(app, "LISTE — MoveRight 1 char",
                (doc, sel, om) => { sel.SetRange(om.Range.End, om.Range.End); sel.MoveRight(Word.WdUnits.wdCharacter, 1, Word.WdMovementType.wdMove); },
                DecorNumberedList);

        public static string RunEscapeList_EndKey(Word.Application app)
            => RunCaretEscape(app, "LISTE — EndKey wdLine",
                (doc, sel, om) => { sel.SetRange(om.Range.End, om.Range.End); sel.EndKey(Word.WdUnits.wdLine, Word.WdMovementType.wdMove); },
                DecorNumberedList);

        // ── PERF PROBE : prouve l'iso-perf (local au ¶, pas O(doc)) ───────
        // Insère f(x) en OMML en fin de doc, mesure docSize / WordOpenXML /
        // timings. À relancer sur doc VIDE puis doc de 50-100 pages : si
        // docSize explose mais WordOpenXML+timing restent ~constants → iso-perf
        // confirmé empiriquement (le chemin ne lit que le ¶ courant).
        public static string RunPerfProbe(Word.Application app)
        {
            var sb = StartTrace("PERF PROBE — insert OMML local : docSize / WordOpenXML / timing");
            try
            {
                var doc = app?.ActiveDocument; var sel = app?.Selection;
                if (doc == null || sel == null) { sb.AppendLine("doc/sel null"); DumpToLog(sb.ToString()); return sb.ToString(); }
                XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

                int docSize = doc.Content.End;
                sel.SetRange(doc.Content.End - 1, doc.Content.End - 1);
                sel.TypeText("\rSoit ");
                int caret = sel.Start;

                var swTotal = System.Diagnostics.Stopwatch.StartNew();
                sel.TypeText("​"); int zwspStart = caret, zwspEnd = sel.Start;
                try { doc.Range(zwspStart, zwspEnd).Font.Hidden = -1; } catch { }
                int phStart = sel.Start; sel.TypeText("□"); int phEnd = sel.Start;
                var phRange = doc.Range(phStart, phEnd);

                var swXml = System.Diagnostics.Stopwatch.StartNew();
                string xml = phRange.WordOpenXML;
                swXml.Stop();
                int xmlLen = (xml ?? "").Length;
                int pCount = -1;
                try { pCount = XDocument.Parse(xml).Descendants(w + "p").Count(); } catch { }

                var xdoc = XDocument.Parse(xml);
                XElement phRun = null;
                foreach (var r in xdoc.Descendants(w + "r")) { var t = r.Element(w + "t"); if (t != null && t.Value == "□") { phRun = r; break; } }
                if (phRun != null) phRun.ReplaceWith(MathCursor.Core.LatexToOmml.Convert(@"f\left(x\right)"));

                var swInsert = System.Diagnostics.Stopwatch.StartNew();
                try { phRange.InsertXML(xdoc.ToString(SaveOptions.DisableFormatting)); } catch (Exception ex) { sb.AppendLine("InsertXML ERR: " + ex.Message); }
                swInsert.Stop();
                swTotal.Stop();

                sb.AppendLine($"PERF: docSize (Content.End)   = {docSize} chars");
                sb.AppendLine($"PERF: phRange.WordOpenXML     = {xmlLen} chars  ({pCount} <w:p>)  lu en {swXml.ElapsedMilliseconds} ms");
                sb.AppendLine($"PERF: InsertXML               = {swInsert.ElapsedMilliseconds} ms");
                sb.AppendLine($"PERF: total insert            = {swTotal.ElapsedMilliseconds} ms");
                sb.AppendLine("→ Relance sur doc VIDE puis doc 50-100 pages : si docSize change mais");
                sb.AppendLine("  WordOpenXML (~1 <w:p> + boilerplate constant) et timing restent stables");
                sb.AppendLine("  → iso-perf confirmé (insertion locale au ¶, pas O(doc)).");
                DumpToLog(sb.ToString()); return sb.ToString();
            }
            catch (Exception ex) { sb.AppendLine("ERR " + ex.Message); DumpToLog(sb.ToString()); return sb.ToString(); }
        }

        // Décor par défaut : « Soit ▮ » sur une ligne neuve (rien après).
        private static int DecorPlain(Word.Document doc, Word.Selection sel, StringBuilder sb)
        {
            sel.SetRange(doc.Content.End - 1, doc.Content.End - 1);
            sel.TypeText("\rSoit ");
            sb.AppendLine("[decor] ligne neuve « Soit ▮ »");
            return sel.Start;
        }

        // Décor tableau : tableau 2×2 en fin de doc, caret cellule(1,1) « Soit ▮ ».
        private static int DecorTableCell(Word.Document doc, Word.Selection sel, StringBuilder sb)
        {
            sel.SetRange(doc.Content.End - 1, doc.Content.End - 1);
            sel.TypeText("\r");
            var tbl = doc.Tables.Add(doc.Range(doc.Content.End - 1, doc.Content.End - 1), 2, 2);
            var cell = tbl.Cell(1, 1);
            sel.SetRange(cell.Range.Start, cell.Range.Start);
            sel.TypeText("Soit ");
            sb.AppendLine("[decor] tableau 2×2, cellule(1,1) « Soit ▮ »");
            return sel.Start;
        }

        // Décor liste : ligne neuve + liste numérotée, caret « Soit ▮ ».
        private static int DecorNumberedList(Word.Document doc, Word.Selection sel, StringBuilder sb)
        {
            sel.SetRange(doc.Content.End - 1, doc.Content.End - 1);
            sel.TypeText("\r");
            try
            {
                var gallery = doc.Application.ListGalleries[Word.WdListGalleryType.wdNumberGallery];
                sel.Range.ListFormat.ApplyListTemplate(gallery.ListTemplates[1], false);
            }
            catch (Exception ex) { sb.AppendLine("[decor] liste ERR: " + ex.Message); }
            sel.TypeText("Soit ");
            sb.AppendLine("[decor] liste numérotée « Soit ▮ »");
            return sel.Start;
        }

        // Insert inline OMath f(x) via OMML (= approche A prod) dans le décor,
        // applique l'escape, tape "ABC", juge math vs plain.
        private static string RunCaretEscape(Word.Application app, string name,
            Action<Word.Document, Word.Selection, Word.OMath> escape,
            Func<Word.Document, Word.Selection, StringBuilder, int> decor = null)
        {
            var sb = StartTrace("ÉCHAPPEMENT CARET — " + name);
            try
            {
                var doc = app?.ActiveDocument; var sel = app?.Selection;
                if (doc == null || sel == null) { sb.AppendLine("doc/sel null"); DumpToLog(sb.ToString()); return sb.ToString(); }
                XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                XNamespace m = "http://schemas.openxmlformats.org/officeDocument/2006/math";

                int caret = (decor ?? DecorPlain)(doc, sel, sb);

                // Insert inline OMath via OMML (approche A).
                sel.TypeText("​"); int zwspStart = caret, zwspEnd = sel.Start;
                try { doc.Range(zwspStart, zwspEnd).Font.Hidden = -1; } catch { }
                int phStart = sel.Start; sel.TypeText("□"); int phEnd = sel.Start;
                var phRange = doc.Range(phStart, phEnd);
                var xdoc = XDocument.Parse(phRange.WordOpenXML);
                XElement phRun = null;
                foreach (var r in xdoc.Descendants(w + "r")) { var t = r.Element(w + "t"); if (t != null && t.Value == "□") { phRun = r; break; } }
                if (phRun == null) { sb.AppendLine("run □ introuvable"); DumpToLog(sb.ToString()); return sb.ToString(); }
                phRun.ReplaceWith(MathCursor.Core.LatexToOmml.Convert(@"f\left(x\right)"));
                try { phRange.InsertXML(xdoc.ToString(SaveOptions.DisableFormatting)); }
                catch (Exception ex) { sb.AppendLine("InsertXML ERR: " + ex.Message); DumpToLog(sb.ToString()); return sb.ToString(); }

                Word.OMath om = null;
                foreach (Word.OMath o in doc.Range(phStart, System.Math.Min(doc.Content.End, phStart + 60)).OMaths) { om = o; break; }
                if (om == null) { sb.AppendLine("om introuvable post-insert"); DumpToLog(sb.ToString()); return sb.ToString(); }
                sb.AppendLine($"om=[{om.Range.Start},{om.Range.End}) text=\"{(om.Range.Text ?? "").Trim()}\"");

                // Technique d'échappement.
                try { escape(doc, sel, om); } catch (Exception ex) { sb.AppendLine("escape ERR: " + ex.Message); }
                int caretBeforeType = app.Selection.Start;

                // Frappe témoin.
                app.Selection.TypeText("ABC");
                sb.AppendLine($"caret avant frappe={caretBeforeType}");

                // Verdict : "ABC" en <w:t> (plain ✓) ou <m:t> (math ✗) ?
                var para = doc.Range(zwspStart, zwspStart).Paragraphs[1].Range;
                var px = XDocument.Parse(para.WordOpenXML);
                bool inMath = px.Descendants(m + "t").Any(t => (t.Value ?? "").Contains("ABC"));
                bool inText = px.Descendants(w + "t").Any(t => (t.Value ?? "").Contains("ABC"));
                if (!inMath && !inText)
                {
                    inMath = px.Descendants(m + "t").Any(t => (t.Value ?? "").Contains("A"));
                    inText = px.Descendants(w + "t").Any(t => { var v = t.Value ?? ""; return v.Contains("A") && !v.Contains("Soit"); });
                }
                string verdict = inText && !inMath ? "✓ PLAIN (échappement OK)" : inMath ? "✗ MATH (absorbée)" : "? introuvable";
                sb.AppendLine($"VERDICT: <m:t>={inMath}  <w:t>={inText}  → {verdict}");
                sb.AppendLine($"¶ text=\"{(para.Text ?? "").Replace("\r", "⏎")}\"");
                DumpToLog(sb.ToString()); return sb.ToString();
            }
            catch (Exception ex) { sb.AppendLine("ERR " + ex.Message); DumpToLog(sb.ToString()); return sb.ToString(); }
        }

        // ─── BATTERIE OMML : insère plein de cas via LatexToOmml ──────────
        // Un seul clic → tous les cas rendus dans le doc (chacun sur sa ligne)
        // + XML loggé. Pour valider visuellement la couverture de l'émetteur.
        public static string RunOmmlBattery(Word.Application app)
        {
            var sb = StartTrace("BATTERIE OMML : LatexToOmml → InsertXML, chaque cas sur sa ligne.");
            var cases = new (string label, string latex)[]
            {
                ("frac",        @"\frac{1}{x+1}"),
                ("lim/frac",    @"\lim_{x \to 0} \frac{1}{x+1}"),
                ("sqrt",        @"\sqrt{x+1}"),
                ("sqrt[n]",     @"\sqrt[3]{8}"),
                ("sup",         @"x^{2}"),
                ("sub",         @"u_{n}"),
                ("subsup",      @"x_{i}^{2}"),
                ("sum",         @"\sum_{k=1}^{n} \frac{1}{k}"),
                ("int",         @"\int_{0}^{1} x^{2}"),
                ("vec",         @"\vec{AB}"),
                ("setminus",    @"\mathbb{R} \setminus \{0\}"),
                ("relation",    @"f(x) = \frac{1}{x}+1"),
                ("interval",    @"[0;1] \cup [2;3]"),
                ("nested",      @"\frac{1}{\sum_{k=1}^{n} \frac{1}{k+1}}"),
                ("greek/sup",   @"\alpha^{2}+\beta"),
                ("delim/frac",  @"f\left(\frac{1}{x}\right)"),
            };
            try
            {
                var doc = app?.ActiveDocument;
                var sel = app?.Selection;
                if (doc == null || sel == null) { sb.AppendLine("doc/sel null"); return sb.ToString(); }

                foreach (var (label, latex) in cases)
                {
                    try
                    {
                        sel.SetRange(doc.Content.End, doc.Content.End);
                        sel.TypeText(label + " :  ");
                        // oMath inline après le label (même ¶).
                        var para = sel.Paragraphs[1].Range;
                        var xdoc = XDocument.Parse(para.WordOpenXML);
                        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                        var pEl = xdoc.Descendants(w + "p").FirstOrDefault();
                        if (pEl == null) { sb.AppendLine($"[{label}] <w:p> introuvable"); continue; }
                        pEl.Add(MathCursor.Core.LatexToOmml.Convert(latex));
                        para.InsertXML(xdoc.ToString(SaveOptions.DisableFormatting));
                        sel.SetRange(doc.Content.End, doc.Content.End);
                        sel.TypeText("\r");
                        sb.AppendLine($"[{label}] OK  latex={latex}");
                    }
                    catch (Exception exC) { sb.AppendLine($"[{label}] ERR: {exC.Message}  latex={latex}"); }
                }
                DumpToLog(sb.ToString());
                return sb.ToString();
            }
            catch (Exception ex) { sb.AppendLine("ERR " + ex.Message); DumpToLog(sb.ToString()); return sb.ToString(); }
        }

        // ─── Helpers ─────────────────────────────────────────────────

        private static StringBuilder StartTrace(string description)
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════════════════");
            sb.AppendLine($" Variant : {description}");
            sb.AppendLine($" Source  : \"{DefaultSource}\"");
            sb.AppendLine("═══════════════════════════════════════════════════════");
            return sb;
        }

        // Écrit la trace dans mathcursor.log (le pane inspecteur reçoit le retour).
        private static void DumpToLog(string trace)
        {
            try
            {
                var dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MathCursor", "logs");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "mathcursor.log"),
                    "\n===== OMML DEBUG TRACE =====\n" + trace + "\n");
            }
            catch { }
        }
    }
}
