// MathCursor: capturing mathematical intent from linear keyboard input.
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

using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace MathCursor.Host.SourceMap
{
    /// <summary>
    /// Whitelist STRICTE des arbres OMML constructibles par le walker
    /// (<c>OmmlToOMathBuilder</c>) via l'object model Word. Pure (XLinq seul,
    /// pas de Word interop) : testable en xUnit — le test de couverture
    /// rejoue TOUT le corpus fixtures à travers <c>LatexToOmml</c> et exige
    /// 100 % de supporté. C'est le VERROU du « pas de fallback » (amendement
    /// 2026-06-12 de l'ADR hash-source-map) : l'insertion n'a pas de repli
    /// InsertXML, un arbre inconstructible est un BUG à corriger ici et dans
    /// le walker, détecté en CI, jamais à l'exécution chez l'élève.
    /// </summary>
    internal static class OmmlWalkerWhitelist
    {
        private static readonly XNamespace M =
            "http://schemas.openxmlformats.org/officeDocument/2006/math";

        /// <summary>Vrai si TOUT l'arbre est constructible via l'OM. Sinon
        /// <paramref name="reason"/> nomme le premier nœud refusé.</summary>
        public static bool IsSupported(XElement oMath, out string reason)
        {
            reason = null;
            if (oMath == null || oMath.Name != M + "oMath") { reason = "racine ≠ m:oMath"; return false; }
            foreach (var child in oMath.Elements())
                if (!CheckItem(child, ref reason)) return false;
            return true;
        }

        private static bool CheckItems(IEnumerable<XElement> items, ref string reason)
        {
            foreach (var it in items)
                if (!CheckItem(it, ref reason)) return false;
            return true;
        }

        private static bool CheckItem(XElement el, ref string reason)
        {
            if (el.Name.Namespace != M) { reason = "ns étranger <" + el.Name + ">"; return false; }
            string n = el.Name.LocalName;
            switch (n)
            {
                case "r":
                    foreach (var c in el.Elements())
                        if (c.Name != M + "t") { reason = "m:r avec <" + c.Name.LocalName + ">"; return false; }
                    return true;
                case "f":
                {
                    var fPr = el.Element(M + "fPr");
                    if (fPr != null)
                    {
                        var type = fPr.Element(M + "type");
                        // seule prop émise : type (noBar pour binom)
                        if (fPr.Elements().Any(p => p.Name != M + "type")
                            || (type != null && (string)type.Attribute(M + "val") != "noBar"))
                        { reason = "m:fPr non supporté"; return false; }
                    }
                    return CheckArg(el, "num", ref reason) && CheckArg(el, "den", ref reason);
                }
                case "sSup": return CheckArg(el, "e", ref reason) && CheckArg(el, "sup", ref reason);
                case "sSub": return CheckArg(el, "e", ref reason) && CheckArg(el, "sub", ref reason);
                case "sSubSup":
                    return CheckArg(el, "e", ref reason) && CheckArg(el, "sub", ref reason)
                        && CheckArg(el, "sup", ref reason);
                case "d":
                {
                    var dPr = el.Element(M + "dPr");
                    if (dPr != null && dPr.Elements().Any(p => p.Name != M + "begChr" && p.Name != M + "endChr"))
                    { reason = "m:dPr non supporté"; return false; }
                    var es = el.Elements(M + "e").ToList();
                    if (es.Count == 0) { reason = "m:d sans m:e"; return false; }
                    foreach (var e in es)
                        if (!CheckItems(e.Elements(), ref reason)) return false;
                    return true;
                }
                case "nary":
                {
                    var pr = el.Element(M + "naryPr");
                    if (pr != null && pr.Elements().Any(p =>
                            p.Name != M + "chr" && p.Name != M + "limLoc"
                            && p.Name != M + "subHide" && p.Name != M + "supHide"))
                    { reason = "m:naryPr non supporté"; return false; }
                    return CheckArg(el, "sub", ref reason) && CheckArg(el, "sup", ref reason)
                        && CheckArg(el, "e", ref reason);
                }
                case "rad":
                {
                    var pr = el.Element(M + "radPr");
                    if (pr != null && pr.Elements().Any(p => p.Name != M + "degHide"))
                    { reason = "m:radPr non supporté"; return false; }
                    return CheckArg(el, "deg", ref reason) && CheckArg(el, "e", ref reason);
                }
                case "func":
                    return CheckArg(el, "fName", ref reason) && CheckArg(el, "e", ref reason);
                case "limLow":
                    return CheckArg(el, "e", ref reason) && CheckArg(el, "lim", ref reason);
                case "acc":
                {
                    var pr = el.Element(M + "accPr");
                    if (pr != null && pr.Elements().Any(p => p.Name != M + "chr"))
                    { reason = "m:accPr non supporté"; return false; }
                    return CheckArg(el, "e", ref reason);
                }
                case "m":
                {
                    var rows = el.Elements(M + "mr").ToList();
                    if (rows.Count == 0) { reason = "m:m sans m:mr"; return false; }
                    int cols = rows[0].Elements(M + "e").Count();
                    foreach (var row in rows)
                    {
                        var cells = row.Elements(M + "e").ToList();
                        if (cells.Count != cols) { reason = "m:m lignes inégales"; return false; }
                        foreach (var cell in cells)
                            if (!CheckItems(cell.Elements(), ref reason)) return false;
                    }
                    return true;
                }
                case "eqArr":
                {
                    var rows = el.Elements(M + "e").ToList();
                    if (rows.Count == 0) { reason = "m:eqArr sans m:e"; return false; }
                    foreach (var row in rows)
                        if (!CheckItems(row.Elements(), ref reason)) return false;
                    return true;
                }
                case "borderBox":
                    // \boxed : on n'émet jamais de borderBoxPr (4 bords par
                    // défaut). Sa présence = arbre non prévu → refus.
                    if (el.Element(M + "borderBoxPr") != null)
                    { reason = "m:borderBoxPr non supporté"; return false; }
                    return CheckArg(el, "e", ref reason);
                default:
                    reason = "<m:" + n + "> hors whitelist";
                    return false;
            }
        }

        private static bool CheckArg(XElement parent, string argName, ref string reason)
        {
            var arg = parent.Element(M + argName);
            if (arg == null) return true; // absent = vide (sub caché, deg caché…)
            return CheckItems(arg.Elements(), ref reason);
        }
    }
}
