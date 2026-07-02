// MathCursor — capture d'intention mathématique depuis une saisie clavier linéaire.
// Copyright (C) 2026  Côme Percin
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
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host
{
    /// <summary>
    /// Conformance runner in-Word du walker OMML→OMath (ADR 2026-06-10-Feat-
    /// undo-contract-omath-walker §3). Pour chaque LaTeX de la batterie :
    /// <c>LatexToOmml.Convert</c> → <c>OmmlToOMathBuilder.Build</c> en fin de
    /// doc → relecture <c>om.Range.WordOpenXML</c> → comparaison NORMALISÉE
    /// à l'OMML attendu (drop ctrlPr/rPr/w:*, fusion des runs adjacents,
    /// attribut m:val seul) → suppression. Rapport PASS/DIFF/FALLBACK/ERR
    /// dans le log + StatusBar.
    ///
    /// C'est lui qui prouve les mappings incertains de l'object model
    /// (ordre des Args du nary, marques d'alignement &amp; des eqArr,
    /// begChr/endChr vides, espaces fines U+2009…).
    /// </summary>
    internal static class OmathWalkerConformance
    {
        private static readonly XNamespace M =
            "http://schemas.openxmlformats.org/officeDocument/2006/math";

        private static readonly string[] Battery =
        {
            "x=\\frac{1}{2}",
            "\\frac{x+1}{y-2}",
            "x^{2}",
            "u_{n}",
            "x_{i}^{2}",
            "\\sqrt{x}",
            "\\sqrt[3]{x+1}",
            "\\left(x+1\\right)^{2}",
            "f\\left(x\\right)=x^{2}",
            "\\left|z-1\\right|",
            "\\sum_{i=1}^{n}i^{2}",
            "\\int_{0}^{1}x\\,dx",
            "\\int x\\,dx",
            "\\lim_{x\\to 0}\\frac{1}{x+1}", // le bug historique BuildUp
            "\\vec{AB}",
            "\\bar{z}",
            "\\widehat{ABC}",
            "\\binom{n}{k}",
            "\\begin{pmatrix}a & b \\\\ c & d\\end{pmatrix}",
            "x\\leq y",
            "\\pi r^{2}",
            "\\frac{\\sqrt{x^{2}+1}}{2}",
            "1\\,\\mathrm{cm}^{3}",
        };

        public static void Run(Word.Application app)
        {
            var doc = app?.ActiveDocument;
            if (doc == null) return;

            int pass = 0, diff = 0, fallback = 0, err = 0;
            var report = new StringBuilder();
            report.AppendLine("=== CONFORMANCE WALKER ===");

            foreach (var latex in AllCases())
            {
                string label = latex.Label;
                try
                {
                    var expected = latex.OMath;
                    if (!OmmlToOMathBuilder.IsSupported(expected, out string why))
                    {
                        fallback++;
                        report.AppendLine($"FALLBACK {label} — {why}");
                        continue;
                    }

                    // Build en toute fin de doc (¶ final), puis cleanup.
                    int pos = doc.Content.End - 1;
                    var om = OmmlToOMathBuilder.Build(doc, pos, expected, m => report.AppendLine("  " + m));
                    if (om == null) { err++; report.AppendLine($"ERR      {label} — Build null"); continue; }

                    string roundTrip;
                    int omStart = om.Range.Start, omEnd = om.Range.End;
                    try { roundTrip = om.Range.WordOpenXML; }
                    finally
                    {
                        try { doc.Range(omStart, omEnd).Delete(); } catch { }
                    }

                    var got = ExtractFirstOMath(roundTrip);
                    if (got == null) { err++; report.AppendLine($"ERR      {label} — pas d'oMath au round-trip"); continue; }

                    string canonExpected = Canon(expected);
                    string canonGot = Canon(got);
                    if (canonExpected == canonGot) { pass++; report.AppendLine($"PASS     {label}"); }
                    else
                    {
                        diff++;
                        report.AppendLine($"DIFF     {label}");
                        report.AppendLine($"  attendu : {Truncate(canonExpected)}");
                        report.AppendLine($"  obtenu  : {Truncate(canonGot)}");
                    }
                }
                catch (Exception ex)
                {
                    err++;
                    report.AppendLine($"ERR      {label} — {ex.Message}");
                }
            }

            string summary = $"walker conformance : {pass} PASS, {diff} DIFF, {fallback} FALLBACK, {err} ERR";
            report.AppendLine(summary);
            Log(report.ToString());
            try { app.StatusBar = summary + " (détail au log)"; } catch { }
        }

        private static IEnumerable<(string Label, XElement OMath)> AllCases()
        {
            foreach (var latex in Battery)
            {
                XElement el = null;
                try { el = MathCursor.Serialization.LatexToOmml.Convert(latex); }
                catch (Exception ex) { Log($"convert ERR \"{latex}\" : {ex.Message}"); }
                if (el != null) yield return (latex, el);
            }
            // eqArr 2 lignes avec marques d'alignement & (forme ChainComposer :
            // colonne connecteur & lhs & relRhs) — vérifie que poser « & » via
            // Range.Text donne bien des alignment marks, pas des ampersands.
            yield return ("eqArr[&x=1 / ⟺&x=2]", new XElement(M + "oMath",
                new XElement(M + "eqArr",
                    new XElement(M + "e", Run("&x"), Run("&=1")),
                    new XElement(M + "e", Run("⟺&x"), Run("&=2")))));
        }

        private static XElement Run(string t) =>
            new XElement(M + "r", new XElement(M + "t",
                new XAttribute(XNamespace.Xml + "space", "preserve"), t));

        private static XElement ExtractFirstOMath(string wordOpenXml)
        {
            try
            {
                var xdoc = XDocument.Parse(wordOpenXml);
                return xdoc.Descendants(M + "oMath").FirstOrDefault();
            }
            catch { return null; }
        }

        // ── Normalisation ────────────────────────────────────────────────
        // Word décore l'OMML au stockage (m:ctrlPr, m:rPr avec m:sty, w:rPr
        // fonts…) et peut splitter les runs. Canon : éléments m:* seuls,
        // attribut m:val seul, drop ctrlPr/rPr, fusion des m:r adjacents,
        // drop des éléments vides sans attributs (m:sub caché…).

        private static string Canon(XElement oMath)
        {
            var c = CanonEl(oMath);
            return c?.ToString(SaveOptions.DisableFormatting) ?? "";
        }

        // Propriétés à VALEUR PAR DÉFAUT de la spec OMML : Word les OMET au
        // stockage (mesuré 2026-06-11, 7 faux DIFF) — émises ou omises, c'est
        // la même équation. Repli des deux côtés de la comparaison.
        private static bool IsDefaultProp(XElement el)
        {
            string parent = el.Parent?.Name.LocalName ?? "";
            string val = (string)el.Attribute(M + "val");
            switch (el.Name.LocalName)
            {
                case "begChr": return parent == "dPr" && val == "(";
                case "endChr": return parent == "dPr" && val == ")";
                case "chr":
                    return (parent == "naryPr" && val == "∫")   // ∫ = défaut nary
                        || (parent == "accPr" && val == "̂");   // ̂ = défaut acc
                case "subHide":
                case "supHide": return parent == "naryPr" && (val == "0" || val == "off");
                case "degHide": return parent == "radPr" && (val == "0" || val == "off");
                case "limLoc": return parent == "naryPr" && val == "undOvr";
                case "type": return parent == "fPr" && val == "bar";
                // Matrices : Word AJOUTE m:mPr/mcs au stockage (« N colonnes,
                // centrées ») — count est dérivable des lignes (comparées),
                // center est le défaut. Les chaînes mcPr/mc/mcs/mPr vidées
                // tombent ensuite par la règle « élément vide sans attribut ».
                case "count": return parent == "mcPr";
                case "mcJc": return parent == "mcPr" && val == "center";
                default: return false;
            }
        }

        private static XElement CanonEl(XElement el)
        {
            if (el.Name.Namespace != M) return null;
            string n = el.Name.LocalName;
            if (n == "ctrlPr" || n == "rPr") return null;
            if (IsDefaultProp(el)) return null;

            var result = new XElement(el.Name);
            var val = el.Attribute(M + "val");
            if (val != null) result.SetAttributeValue(M + "val", val.Value);

            if (n == "t") { result.Value = el.Value; return result; }

            foreach (var child in el.Elements())
            {
                var cc = CanonEl(child);
                if (cc == null) continue;
                // fusion m:r adjacents (Word splitte les runs librement)
                if (cc.Name == M + "r" && result.LastNode is XElement prev && prev.Name == M + "r")
                {
                    string merged = string.Concat(prev.Elements(M + "t").Select(t => t.Value))
                                  + string.Concat(cc.Elements(M + "t").Select(t => t.Value));
                    prev.ReplaceNodes(new XElement(M + "t", merged));
                    continue;
                }
                if (cc.Name == M + "r")
                {
                    string txt = string.Concat(cc.Elements(M + "t").Select(t => t.Value));
                    if (txt.Length == 0) continue;
                    cc = new XElement(M + "r", new XElement(M + "t", txt));
                }
                result.Add(cc);
            }

            // élément vide sans attribut = absent (m:sub caché, m:deg vide…)
            if (!result.HasAttributes && !result.Elements().Any() && n != "t") return null;
            return result;
        }

        private static string Truncate(string s) =>
            s.Length > 500 ? s.Substring(0, 500) + "…" : s;

        private static void Log(string message)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MathCursor", "logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "mathcursor.log"),
                    $"{DateTime.UtcNow:o} conformance {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
