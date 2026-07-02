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
using System.Text;
using System.Xml.Linq;

namespace MathCursor.Host
{
    /// <summary>
    /// Logique pure (sans dépendance Word) de reconstruction du texte
    /// d'un paragraphe Word à partir de son fragment OOXML. Utilisée par
    /// <see cref="WordContextReader"/> pour ne plus dépendre de
    /// <c>Range.Text</c> (qui injecte des chars de contrôle Word — <c>\a</c>
    /// cell-end, <c>\v</c> line break, etc. — qui polluent le NER en
    /// cellule de tableau).
    ///
    /// <para>Cf. ADR <c>2026-05-11-Refactor-paragraph-reader-via-xml</c>.
    /// Cohérent avec ADR
    /// <c>2026-05-11-Fix-omath-splice-content-based-navigation</c> : on
    /// raisonne en XML local autour du curseur, pas en rendu Range.Text
    /// aplati.</para>
    /// </summary>
    internal static class ParagraphTextExtractor
    {
        private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private static readonly XNamespace M = "http://schemas.openxmlformats.org/officeDocument/2006/math";

        /// <summary>
        /// Extraction du texte propre d'un <c>&lt;w:p&gt;</c> à partir du
        /// fragment OOXML <paramref name="paragraphXml"/> (= sortie de
        /// <c>paraRange.WordOpenXML</c> côté Word interop).
        ///
        /// <para>Pour chaque <c>&lt;m:oMath&gt;</c> enfant direct du
        /// <c>&lt;w:p&gt;</c>, injecte <paramref name="omathLengths"/>[i]
        /// espaces (i = ordre d'apparition de l'OMath dans le <c>&lt;w:p&gt;</c>).
        /// Ces longueurs viennent du caller, calculées via
        /// <c>Word.OMath.Range.End - Range.Start</c> pour préserver
        /// l'invariant 1:1 char entre <c>text</c> et positions absolues
        /// Word (essentiel pour <c>paragraphAbsStart + relativeIdx</c> en
        /// aval).</para>
        ///
        /// <para>Si le fragment contient plusieurs <c>&lt;w:p&gt;</c>
        /// (improbable pour un <c>Paragraph.Range</c>), prend le DERNIER
        /// dans l'ordre document (= celui où est le curseur en pratique).</para>
        ///
        /// <para>Retourne <c>(null, null)</c> si le fragment est invalide
        /// ou ne contient aucun <c>&lt;w:p&gt;</c>.</para>
        /// </summary>
        public static (string text, IReadOnlyList<(int start, int end)> regions) Extract(
            string paragraphXml, IReadOnlyList<int> omathLengths)
        {
            if (string.IsNullOrEmpty(paragraphXml)) return (null, null);

            XDocument xdoc;
            try { xdoc = XDocument.Parse(paragraphXml); }
            catch { return (null, null); }

            var paras = xdoc.Descendants(W + "p").ToList();
            if (paras.Count == 0) return (null, null);
            var target = paras[paras.Count - 1];

            var sb = new StringBuilder();
            var regions = new List<(int, int)>();
            int omathIdx = 0;

            foreach (var child in target.Elements())
            {
                if (child.Name == W + "r")
                {
                    // Concat les <w:t> enfants direct. Ignore les autres
                    // (rPr, br, tab — gérés séparément si besoin).
                    foreach (var leaf in child.Elements())
                    {
                        if (leaf.Name == W + "t")
                        {
                            sb.Append(leaf.Value);
                        }
                        else if (leaf.Name == W + "tab")
                        {
                            // <w:tab/> rend "\t" dans Range.Text.
                            sb.Append('\t');
                        }
                        else if (leaf.Name == W + "br")
                        {
                            // <w:br/> rend "\v" (vertical tab) dans Range.Text
                            // mais on normalise en espace : invariant 1:1 OK
                            // (1 char in XML rendering → 1 char ici).
                            sb.Append(' ');
                        }
                        // Autres (rPr, etc.) : 0 char dans le rendu Word.
                    }
                }
                else if (child.Name == M + "oMath" || child.Name == M + "oMathPara")
                {
                    // Injecte N espaces pour l'OMath. N vient du caller.
                    int len = (omathLengths != null && omathIdx < omathLengths.Count)
                        ? omathLengths[omathIdx]
                        : 0;
                    int start = sb.Length;
                    for (int i = 0; i < len; i++) sb.Append(' ');
                    int end = sb.Length;
                    if (len > 0) regions.Add((start, end));
                    omathIdx++;
                }
                // Autres (<w:bookmarkStart>, <w:proofErr>,
                // <w:commentRangeStart>, <w:pPr>, …) : 0 char dans Range.Text.
            }

            return (sb.ToString(), regions);
        }
    }
}
