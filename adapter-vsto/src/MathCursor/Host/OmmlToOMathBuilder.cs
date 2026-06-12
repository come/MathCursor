using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host
{
    /// <summary>
    /// Walker OMML→OMath (ADR 2026-06-10-Feat-undo-contract-omath-walker) :
    /// consomme l'arbre <c>&lt;m:oMath&gt;</c> émis par
    /// <c>MathCursor.Serialization.LatexToOmml</c> (qui RESTE la source de
    /// vérité) et le construit dans le doc user via l'object model natif —
    /// <c>OMaths.Add</c> + <c>Functions.Add</c> + runs texte. Aucun
    /// <c>InsertXML</c> : le custom record undo reste intact (1 Ctrl+Z =
    /// tout le commit), et Word ne re-parse rien (la structure est posée
    /// nœud par nœud, l'acquis de l'ADR 2026-06-02 est conservé).
    ///
    /// <para><see cref="IsSupported"/> pré-valide l'arbre ENTIER contre la
    /// whitelist (éléments + propriétés). Tout nœud inconnu → l'appelant
    /// retombe sur InsertXML pour l'équation entière (rendu correct, undo
    /// dégradé, loggé) — jamais de demi-équation.</para>
    ///
    /// <para>Fidélité prouvée par le conformance runner in-Word
    /// (<c>OmathWalkerConformance</c>) : build → relecture WordOpenXML →
    /// comparaison normalisée à l'OMML attendu.</para>
    /// </summary>
    internal static class OmmlToOMathBuilder
    {
        private static readonly XNamespace M =
            "http://schemas.openxmlformats.org/officeDocument/2006/math";

        // ── Pré-validation (whitelist stricte) ───────────────────────────

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

        // ── Construction ─────────────────────────────────────────────────

        /// <summary>
        /// Construit l'OMath à <paramref name="position"/> (doc user) et la
        /// renvoie. Null si échec — l'OMath partielle est alors SUPPRIMÉE
        /// (jamais de demi-équation dans le doc), l'appelant retombe sur
        /// InsertXML.
        /// </summary>
        public static Word.OMath Build(Word.Document doc, int position,
            XElement oMathEl, Action<string> log)
        {
            Word.OMath om = null;
            try
            {
                var omRange = doc.OMaths.Add(doc.Range(position, position));
                om = omRange.OMaths[1];
                // OMaths.Add peut insérer le prompt « Tapez une équation
                // ici. » comme VRAI texte dans l'équation (diag 2026-06-12).
                // NE PAS vider la zone avant de construire (zone vide =
                // frame fantôme + premier write éjecté hors math, mesuré) :
                // on construit DEVANT le prompt — validé — puis on coupe le
                // prompt en queue, par sa longueur (locale-indépendant).
                int promptLen = 0;
                try { promptLen = (om.Range.Text ?? "").Length; } catch { }
                BuildSequence(doc, om, om.Range.Start, oMathEl.Elements());
                if (promptLen > 0)
                {
                    try { doc.Range(om.Range.End - promptLen, om.Range.End).Delete(); }
                    catch (Exception exP) { log?.Invoke("walker_prompt_trim_error: " + exP.Message); }
                }
                return om;
            }
            catch (Exception ex)
            {
                log?.Invoke("walker_build_error: " + ex.Message);
                if (om != null)
                {
                    try { om.Range.Delete(); }
                    catch (Exception exD) { log?.Invoke("walker_cleanup_error: " + exD.Message); }
                    // L'OMath créée par OMaths.Add peut survivre au Delete en
                    // squelette vide (« Tapez une équation ici », vu
                    // 2026-06-11) — re-probe local et suppression du résidu.
                    try
                    {
                        int probeEnd = Math.Min(doc.Content.End, position + 4);
                        foreach (Word.OMath rest in doc.Range(Math.Max(0, position - 1), probeEnd).OMaths)
                        { rest.Range.Delete(); break; }
                    }
                    catch (Exception exR) { log?.Invoke("walker_residual_cleanup_error: " + exR.Message); }
                }
                return null;
            }
        }

        /// <summary>Pose une séquence d'items (runs + fonctions) à partir de
        /// <paramref name="pos"/>. Renvoie la position de fin. Gauche→droite,
        /// profondeur d'abord — les ranges COM sont relus paresseusement
        /// (les positions shiftent à chaque insertion).</summary>
        private static int BuildSequence(Word.Document doc, Word.OMath om,
            int pos, IEnumerable<XElement> items)
        {
            foreach (var el in items)
            {
                if (el.Name == M + "r")
                {
                    string txt = string.Concat(el.Elements(M + "t").Select(t => t.Value));
                    if (txt.Length == 0) continue;
                    var r = doc.Range(pos, pos);
                    r.Text = txt;
                    pos = r.End;
                    continue;
                }
                pos = BuildFunction(doc, om, pos, el);
            }
            return pos;
        }

        private static int BuildFunction(Word.Document doc, Word.OMath om,
            int pos, XElement el)
        {
            string n = el.Name.LocalName;
            var at = doc.Range(pos, pos);
            Word.OMathFunction fn;
            switch (n)
            {
                case "f":
                {
                    fn = om.Functions.Add(at, Word.WdOMathFunctionType.wdOMathFunctionFrac);
                    var type = el.Element(M + "fPr")?.Element(M + "type");
                    if ((string)type?.Attribute(M + "val") == "noBar")
                        fn.Frac.Type = Word.WdOMathFracType.wdOMathFracNoBar;
                    FillArg(doc, fn.Frac.Num, el.Element(M + "num"));
                    FillArg(doc, fn.Frac.Den, el.Element(M + "den"));
                    break;
                }
                case "sSup":
                    fn = om.Functions.Add(at, Word.WdOMathFunctionType.wdOMathFunctionScrSup);
                    FillArg(doc, fn.ScrSup.E, el.Element(M + "e"));
                    FillArg(doc, fn.ScrSup.Sup, el.Element(M + "sup"));
                    break;
                case "sSub":
                    fn = om.Functions.Add(at, Word.WdOMathFunctionType.wdOMathFunctionScrSub);
                    FillArg(doc, fn.ScrSub.E, el.Element(M + "e"));
                    FillArg(doc, fn.ScrSub.Sub, el.Element(M + "sub"));
                    break;
                case "sSubSup":
                    fn = om.Functions.Add(at, Word.WdOMathFunctionType.wdOMathFunctionScrSubSup);
                    FillArg(doc, fn.ScrSubSup.E, el.Element(M + "e"));
                    FillArg(doc, fn.ScrSubSup.Sub, el.Element(M + "sub"));
                    FillArg(doc, fn.ScrSubSup.Sup, el.Element(M + "sup"));
                    break;
                case "d":
                {
                    var es = el.Elements(M + "e").ToList();
                    fn = om.Functions.Add(at, Word.WdOMathFunctionType.wdOMathFunctionDelim, es.Count);
                    var dPr = el.Element(M + "dPr");
                    string beg = (string)dPr?.Element(M + "begChr")?.Attribute(M + "val") ?? "(";
                    string end = (string)dPr?.Element(M + "endChr")?.Attribute(M + "val") ?? ")";
                    var delim = fn.Delim;
                    if (beg.Length == 0) delim.NoLeftChar = true;
                    else delim.BegChar = unchecked((short)char.ConvertToUtf32(beg, 0));
                    if (end.Length == 0) delim.NoRightChar = true;
                    else delim.EndChar = unchecked((short)char.ConvertToUtf32(end, 0));
                    for (int k = 0; k < es.Count; k++)
                        FillArg(doc, delim.E.Item(k + 1), es[k]);
                    break;
                }
                case "nary":
                {
                    fn = om.Functions.Add(at, Word.WdOMathFunctionType.wdOMathFunctionNary);
                    var pr = el.Element(M + "naryPr");
                    var nary = fn.Nary;
                    string chr = (string)pr?.Element(M + "chr")?.Attribute(M + "val");
                    if (!string.IsNullOrEmpty(chr)) nary.Char = unchecked((short)char.ConvertToUtf32(chr, 0));
                    nary.SubSupLim = (string)pr?.Element(M + "limLoc")?.Attribute(M + "val") == "subSup";
                    bool hideSub = (string)pr?.Element(M + "subHide")?.Attribute(M + "val") == "1";
                    bool hideSup = (string)pr?.Element(M + "supHide")?.Attribute(M + "val") == "1";
                    nary.HideSub = hideSub;
                    nary.HideSup = hideSup;
                    if (!hideSub) FillArg(doc, nary.Sub, el.Element(M + "sub"));
                    if (!hideSup) FillArg(doc, nary.Sup, el.Element(M + "sup"));
                    FillArg(doc, nary.E, el.Element(M + "e"));
                    break;
                }
                case "rad":
                {
                    fn = om.Functions.Add(at, Word.WdOMathFunctionType.wdOMathFunctionRad);
                    bool hideDeg = (string)el.Element(M + "radPr")?.Element(M + "degHide")?.Attribute(M + "val") == "1";
                    fn.Rad.HideDeg = hideDeg;
                    if (!hideDeg) FillArg(doc, fn.Rad.Deg, el.Element(M + "deg"));
                    FillArg(doc, fn.Rad.E, el.Element(M + "e"));
                    break;
                }
                case "func":
                    fn = om.Functions.Add(at, Word.WdOMathFunctionType.wdOMathFunctionFunc);
                    FillArg(doc, fn.Func.FName, el.Element(M + "fName"));
                    FillArg(doc, fn.Func.E, el.Element(M + "e"));
                    break;
                case "limLow":
                    fn = om.Functions.Add(at, Word.WdOMathFunctionType.wdOMathFunctionLimLow);
                    FillArg(doc, fn.LimLow.E, el.Element(M + "e"));
                    FillArg(doc, fn.LimLow.Lim, el.Element(M + "lim"));
                    break;
                case "acc":
                {
                    fn = om.Functions.Add(at, Word.WdOMathFunctionType.wdOMathFunctionAcc);
                    string chr = (string)el.Element(M + "accPr")?.Element(M + "chr")?.Attribute(M + "val");
                    if (!string.IsNullOrEmpty(chr)) fn.Acc.Char = unchecked((short)char.ConvertToUtf32(chr, 0));
                    FillArg(doc, fn.Acc.E, el.Element(M + "e"));
                    break;
                }
                case "m":
                {
                    var rows = el.Elements(M + "mr").ToList();
                    int cols = rows[0].Elements(M + "e").Count();
                    fn = om.Functions.Add(at, Word.WdOMathFunctionType.wdOMathFunctionMat,
                        rows.Count, cols);
                    // Word peut ignorer NumArgs/NumCols à l'Add (mesuré
                    // 2026-06-11 : « le membre de la collection requis
                    // n'existe pas » au Cell[2,…]) — compléter à la main.
                    var mat = fn.Mat;
                    while (mat.Rows.Count < rows.Count) mat.Rows.Add();
                    while (mat.Cols.Count < cols) mat.Cols.Add();
                    for (int ri = 0; ri < rows.Count; ri++)
                    {
                        var cells = rows[ri].Elements(M + "e").ToList();
                        for (int ci = 0; ci < cells.Count; ci++)
                            FillArg(doc, mat.Cell[ri + 1, ci + 1], cells[ci]);
                    }
                    break;
                }
                case "eqArr":
                {
                    var rows = el.Elements(M + "e").ToList();
                    fn = om.Functions.Add(at, Word.WdOMathFunctionType.wdOMathFunctionEqArray, rows.Count);
                    for (int k = 0; k < rows.Count; k++)
                        FillArg(doc, fn.EqArray.E.Item(k + 1), rows[k]);
                    break;
                }
                default:
                    // IsSupported a whitelisté l'arbre — ne doit jamais arriver.
                    throw new NotSupportedException("walker: <m:" + n + "> inattendu");
            }
            return fn.Range.End;
        }

        /// <summary>Remplit un argument de fonction (récursif). Les args sont
        /// des sous-OMath imbriquées dans le PIA — leurs propres Functions
        /// servent aux imbrications. Arg absent ou vide = no-op (placeholder
        /// Word conservé / arg caché).</summary>
        private static void FillArg(Word.Document doc, Word.OMath arg, XElement argEl)
        {
            if (argEl == null || !argEl.Elements().Any()) return;
            BuildSequence(doc, arg, arg.Range.Start, argEl.Elements());
        }
    }
}
