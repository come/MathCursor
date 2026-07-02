// MathCursor — capture d'intention mathématique depuis une saisie clavier linéaire.
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
    /// whitelist pure (testée xUnit : TOUT le corpus fixtures doit être
    /// constructible — verrou du « pas de fallback », amendement 2026-06-12).
    /// Inconstructible = échec franc : l'appelant restaure la sténo, jamais
    /// de demi-équation ni d'InsertXML.</para>
    ///
    /// <para>Fidélité prouvée par le conformance runner in-Word
    /// (<c>OmathWalkerConformance</c>) : build → relecture WordOpenXML →
    /// comparaison normalisée à l'OMML attendu.</para>
    /// </summary>
    internal static class OmmlToOMathBuilder
    {
        private static readonly XNamespace M =
            "http://schemas.openxmlformats.org/officeDocument/2006/math";

        // ── Pré-validation : déléguée à la whitelist PURE (testée xUnit,
        // verrou de couverture corpus → « pas de fallback » prouvé en CI) ──

        /// <summary>Vrai si TOUT l'arbre est constructible via l'OM. Sinon
        /// <paramref name="reason"/> nomme le premier nœud refusé.</summary>
        public static bool IsSupported(XElement oMath, out string reason)
            => SourceMap.OmmlWalkerWhitelist.IsSupported(oMath, out reason);

        // ── Construction ─────────────────────────────────────────────────

        /// <summary>
        /// Construit l'OMath à <paramref name="position"/> (doc user) et la
        /// renvoie. Null si échec — l'OMath partielle est alors SUPPRIMÉE
        /// (jamais de demi-équation dans le doc), l'appelant restaure la
        /// sténo (pas de repli InsertXML).
        /// </summary>
        public static Word.OMath Build(Word.Document doc, int position,
            XElement oMathEl, Action<string> log)
        {
            Word.OMath om = null;
            try
            {
                // JAMAIS d'Add à VIDE : Word y insère un w:sdt placeholder
                // « Tapez une équation ici. » (temporary/showingPlcHdr, XML
                // taper.docx 2026-06-12) qui est INVISIBLE pour
                // om.Range.ContentControls (count=0 mesuré) → insupprimable
                // proprement. Recette DocMath : Add SUR du texte seed.
                //
                // DEUX seeds ¤¤ (mesuré 2026-06-12, « soit f(x)=1/x ») : une
                // écriture à om.Range.Start est AMBIGUË quand de la prose
                // précède (frontière prose/math : le run atterrissait côté
                // prose, l'équation ne contenait que la fraction). Tout le
                // contenu s'insère ENTRE les deux seeds — positions
                // INTÉRIEURES garanties — puis les seeds sont retirés.
                doc.Range(position, position).Text = "¤¤";
                var omRange = doc.OMaths.Add(doc.Range(position, position + 2));
                om = omRange.OMaths[1];
                BuildSequence(doc, om, om.Range.Start + 1, oMathEl.Elements());
                try
                {
                    var seeds = new List<Word.Range>();
                    foreach (Word.Range ch in om.Range.Characters)
                        if (ch.Text == "¤") seeds.Add(ch);
                    if (seeds.Count != 2) log?.Invoke($"walker_seeds: {seeds.Count}/2 retrouvés");
                    // Seuls le PREMIER et le DERNIER sont nos seeds — un ¤
                    // au milieu serait du contenu (jamais émis aujourd'hui,
                    // mais on ne mange pas le contenu par principe).
                    if (seeds.Count >= 2) { seeds[seeds.Count - 1].Delete(); seeds[0].Delete(); }
                    else if (seeds.Count == 1) seeds[0].Delete();
                }
                catch (Exception exS) { log?.Invoke("walker_seed_delete_error: " + exS.Message); }
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
                    // Functions.Add(Mat) : NumArgs DOIT égaler NumColumns,
                    // sinon Word jette « Le nombre doit être compris entre
                    // 1 et 2 » dès l'Add (POC 2026-06-12 : Add(2,3) jette,
                    // Add(3,3) passe et crée UNE ligne × 3 colonnes). On
                    // crée donc 1×cols puis on ajoute les lignes — schéma
                    // validé POC sur 2×3, 3×2, 3×1, 1×3 et 4×4.
                    fn = om.Functions.Add(at, Word.WdOMathFunctionType.wdOMathFunctionMat,
                        cols, cols);
                    var mat = fn.Mat;
                    while (mat.Rows.Count < rows.Count) mat.Rows.Add();
                    while (mat.Cols.Count < cols) mat.Cols.Add(); // filet (no-op mesuré)
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
                case "borderBox":
                    // \boxed{x} : cadre 4 bords par défaut (aucun borderBoxPr
                    // émis ni géré — la whitelist le refuse). fn.BorderBox.E
                    // = le contenu encadré, rempli récursivement.
                    fn = om.Functions.Add(at, Word.WdOMathFunctionType.wdOMathFunctionBorderBox);
                    FillArg(doc, fn.BorderBox.E, el.Element(M + "e"));
                    break;
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
