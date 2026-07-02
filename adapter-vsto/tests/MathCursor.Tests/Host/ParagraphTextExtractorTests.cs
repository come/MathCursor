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

using MathCursor.Host;
using Xunit;

namespace MathCursor.Tests.Host
{
    /// <summary>
    /// Tests pour <see cref="ParagraphTextExtractor"/> (cf. ADR
    /// <c>2026-05-11-Refactor-paragraph-reader-via-xml</c>). Logique
    /// pure : on alimente avec des fragments XML synthétiques + des
    /// longueurs d'OMath simulées, on vérifie le texte reconstruit et
    /// les régions OMath.
    /// </summary>
    public sealed class ParagraphTextExtractorTests
    {
        // Wrapper minimal avec les namespaces requis pour parser.
        private static string Pkg(string bodyInner)
            => "<w:document"
            + " xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\""
            + " xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\">"
            + "<w:body>" + bodyInner + "</w:body>"
            + "</w:document>";

        [Fact]
        public void Extracts_plain_text_from_single_run()
        {
            string xml = Pkg("<w:p><w:r><w:t>Soit f de x</w:t></w:r></w:p>");

            var (text, regions) = ParagraphTextExtractor.Extract(xml, null);

            Assert.Equal("Soit f de x", text);
            Assert.Empty(regions);
        }

        [Fact]
        public void Extracts_text_with_xml_space_preserve_intact()
        {
            // xml:space="preserve" garde les espaces — le parseur retourne
            // la value telle quelle. Régression : aucun strip de
            // whitespace.
            string xml = Pkg(
                "<w:p><w:r><w:t xml:space=\"preserve\">  Soit f  </w:t></w:r></w:p>");

            var (text, _) = ParagraphTextExtractor.Extract(xml, null);

            Assert.Equal("  Soit f  ", text);
        }

        [Fact]
        public void Concatenates_multiple_runs_in_order()
        {
            // Word peut splitter le texte en plusieurs <w:r> (changement
            // de formatting, autocorrect, etc.).
            string xml = Pkg(
                "<w:p>"
                + "<w:r><w:t xml:space=\"preserve\">Soit </w:t></w:r>"
                + "<w:r><w:t>f de x</w:t></w:r>"
                + "</w:p>");

            var (text, _) = ParagraphTextExtractor.Extract(xml, null);

            Assert.Equal("Soit f de x", text);
        }

        [Fact]
        public void Injects_spaces_for_oMath_using_provided_length()
        {
            // 1 OMath au milieu, longueur Word fournie = 3 chars
            // (= ce que Range.Text aurait donné).
            string xml = Pkg(
                "<w:p>"
                + "<w:r><w:t xml:space=\"preserve\">Soit </w:t></w:r>"
                + "<m:oMath><m:r><m:t>f_2</m:t></m:r></m:oMath>"
                + "<w:r><w:t xml:space=\"preserve\"> et g</w:t></w:r>"
                + "</w:p>");

            var (text, regions) = ParagraphTextExtractor.Extract(xml, new[] { 3 });

            // "Soit " (5) + "   " (3 espaces de l'OMath) + " et g" (5) = 13
            Assert.Equal(13, text.Length);
            Assert.Equal("Soit ", text.Substring(0, 5));
            Assert.Equal("   ", text.Substring(5, 3)); // 3 espaces de l'OMath
            Assert.Equal(" et g", text.Substring(8, 5));
            // Espaces injectés à la position 5..8.
            Assert.Single(regions);
            Assert.Equal((5, 8), regions[0]);
        }

        [Fact]
        public void Injects_multiple_omath_regions_in_order()
        {
            // Deux OMaths dans le même <w:p>, longueurs différentes.
            string xml = Pkg(
                "<w:p>"
                + "<w:r><w:t xml:space=\"preserve\">x</w:t></w:r>"
                + "<m:oMath><m:r><m:t>A</m:t></m:r></m:oMath>"
                + "<w:r><w:t xml:space=\"preserve\"> et </w:t></w:r>"
                + "<m:oMath><m:r><m:t>BC</m:t></m:r></m:oMath>"
                + "<w:r><w:t xml:space=\"preserve\">y</w:t></w:r>"
                + "</w:p>");

            // OMaths de longueurs Word 1 (A) et 2 (BC).
            var (text, regions) = ParagraphTextExtractor.Extract(xml, new[] { 1, 2 });

            // x(1) + A(1) + " et "(4) + BC(2) + y(1) = 9 chars
            Assert.Equal(9, text.Length);
            Assert.Equal("x", text.Substring(0, 1));
            Assert.Equal(" et ", text.Substring(2, 4));
            Assert.Equal("y", text.Substring(8, 1));
            Assert.Equal(2, regions.Count);
            Assert.Equal((1, 2), regions[0]);
            Assert.Equal((6, 8), regions[1]);
        }

        [Fact]
        public void Skips_bookmarks_and_other_zero_width_elements()
        {
            // <w:bookmarkStart>/<w:bookmarkEnd>, <w:proofErr>,
            // <w:pPr>/<w:rPr> n'occupent aucun char dans Range.Text.
            // L'extracteur doit les ignorer.
            string xml = Pkg(
                "<w:p>"
                + "<w:pPr><w:jc w:val=\"left\"/></w:pPr>"
                + "<w:bookmarkStart w:id=\"1\" w:name=\"foo\"/>"
                + "<w:r><w:rPr><w:b/></w:rPr><w:t>Soit f</w:t></w:r>"
                + "<w:bookmarkEnd w:id=\"1\"/>"
                + "</w:p>");

            var (text, regions) = ParagraphTextExtractor.Extract(xml, null);

            Assert.Equal("Soit f", text);
            Assert.Empty(regions);
        }

        [Fact]
        public void Works_for_paragraph_in_table_cell()
        {
            // Le <w:p> est enveloppé dans <w:tc>/<w:tr>/<w:tbl> — comme
            // dans une cellule de tableau. L'extracteur prend le seul
            // <w:p> du fragment local, peu importe sa profondeur.
            string xml = Pkg(
                "<w:tbl><w:tr><w:tc>"
                + "<w:p><w:r><w:t>texte en cellule</w:t></w:r></w:p>"
                + "</w:tc></w:tr></w:tbl>");

            var (text, regions) = ParagraphTextExtractor.Extract(xml, null);

            Assert.Equal("texte en cellule", text);
            Assert.Empty(regions);
        }

        [Fact]
        public void Tab_element_yields_tab_char()
        {
            string xml = Pkg(
                "<w:p>"
                + "<w:r><w:t>col1</w:t><w:tab/><w:t>col2</w:t></w:r>"
                + "</w:p>");

            var (text, _) = ParagraphTextExtractor.Extract(xml, null);

            Assert.Equal("col1\tcol2", text);
        }

        [Fact]
        public void Br_element_yields_space_char_one_to_one()
        {
            // <w:br/> dans Range.Text donne \v (line break Word). On
            // normalise en espace pour ne pas polluer le NER. Invariant
            // 1:1 préservé.
            string xml = Pkg(
                "<w:p>"
                + "<w:r><w:t>line1</w:t><w:br/><w:t>line2</w:t></w:r>"
                + "</w:p>");

            var (text, _) = ParagraphTextExtractor.Extract(xml, null);

            Assert.Equal("line1 line2", text);
        }

        [Fact]
        public void Returns_null_for_empty_or_invalid_input()
        {
            Assert.Equal((null, null), ParagraphTextExtractor.Extract(null, null));
            Assert.Equal((null, null), ParagraphTextExtractor.Extract("", null));
            Assert.Equal((null, null), ParagraphTextExtractor.Extract("not xml", null));
        }

        [Fact]
        public void Returns_null_when_no_paragraph_element()
        {
            string xml = Pkg("<w:r><w:t>orphan run</w:t></w:r>");

            var (text, regions) = ParagraphTextExtractor.Extract(xml, null);

            Assert.Null(text);
            Assert.Null(regions);
        }

        [Fact]
        public void Picks_last_paragraph_when_fragment_has_multiple()
        {
            // Improbable mais défensif : si le fragment a plusieurs <w:p>,
            // prendre le dernier (= celui où est le curseur en pratique).
            string xml = Pkg(
                "<w:p><w:r><w:t>premier</w:t></w:r></w:p>"
                + "<w:p><w:r><w:t>dernier</w:t></w:r></w:p>");

            var (text, _) = ParagraphTextExtractor.Extract(xml, null);

            Assert.Equal("dernier", text);
        }

        [Fact]
        public void Empty_paragraph_returns_empty_string()
        {
            string xml = Pkg("<w:p></w:p>");

            var (text, regions) = ParagraphTextExtractor.Extract(xml, null);

            Assert.Equal("", text);
            Assert.Empty(regions);
        }

        [Fact]
        public void OMathPara_treated_same_as_oMath_for_length()
        {
            // <m:oMathPara> wrapper display math : même traitement que
            // <m:oMath> inline du point de vue caller (longueur fournie).
            string xml = Pkg(
                "<w:p>"
                + "<m:oMathPara><m:oMathParaPr><m:jc m:val=\"left\"/></m:oMathParaPr>"
                + "<m:oMath><m:r><m:t>x</m:t></m:r></m:oMath>"
                + "</m:oMathPara>"
                + "</w:p>");

            // Longueur Word fournie : 5 (peu importe valeur, on vérifie
            // juste qu'elle est appliquée).
            var (text, regions) = ParagraphTextExtractor.Extract(xml, new[] { 5 });

            Assert.Equal("     ", text);
            Assert.Single(regions);
            Assert.Equal((0, 5), regions[0]);
        }
    }
}
