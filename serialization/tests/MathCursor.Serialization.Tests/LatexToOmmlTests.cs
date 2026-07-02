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

using System.Linq;
using System.Xml.Linq;
using MathCursor.Serialization;
using Xunit;

namespace MathCursor.Serialization.Tests
{
    /// <summary>
    /// Tests unitaires STRUCTURELS de <see cref="LatexToOmml"/> : une
    /// assertion XML précise par construction (l'audit OmmlCoverageTests
    /// reste le filet négatif global — pas d'exception/fuite/base vide sur
    /// tous les candidats moteur ; ici on fige la FORME attendue).
    ///
    /// Les 8 tests délimiteurs sont portés de DocMath
    /// (LatexToOmmlDelimiterTests, perdus au prune de Phase 1 — cf. ADR
    /// 2026-06-02-Feat-omml-insertion qui les référence).
    /// </summary>
    public class LatexToOmmlTests
    {
        private static readonly XNamespace M = LatexToOmml.M;

        private static string AllText(XElement omath)
            => string.Concat(omath.Descendants(M + "t").Select(t => t.Value));

        private static string Attr(XElement e, string child, string attr)
            => e.Element(M + child)?.Attribute(M + attr)?.Value;

        private static XElement OnlyDelim(XElement omath)
            => Assert.Single(omath.Descendants(M + "d"));

        // ── Délimiteurs \left … \right (portés de DocMath) ───────────────

        [Fact]
        public void Paren_delim_emits_m_d_not_literal_text()
        {
            var x = LatexToOmml.Convert(@"f\left(x\right)");
            var d = OnlyDelim(x);
            Assert.Equal("(", Attr(d.Element(M + "dPr"), "begChr", "val"));
            Assert.Equal(")", Attr(d.Element(M + "dPr"), "endChr", "val"));

            string text = AllText(x);
            Assert.DoesNotContain("left", text);
            Assert.DoesNotContain("right", text);
            Assert.Contains("f", text);
            Assert.Contains("x", text);
        }

        [Fact]
        public void Bracket_delim()
        {
            var d = OnlyDelim(LatexToOmml.Convert(@"\left[a;b\right]"));
            Assert.Equal("[", Attr(d.Element(M + "dPr"), "begChr", "val"));
            Assert.Equal("]", Attr(d.Element(M + "dPr"), "endChr", "val"));
        }

        [Fact]
        public void Brace_delim_via_backslash()
        {
            var d = OnlyDelim(LatexToOmml.Convert(@"\left\{x\right\}"));
            Assert.Equal("{", Attr(d.Element(M + "dPr"), "begChr", "val"));
            Assert.Equal("}", Attr(d.Element(M + "dPr"), "endChr", "val"));
        }

        [Fact]
        public void Abs_delim_pipe()
        {
            var d = OnlyDelim(LatexToOmml.Convert(@"\left|x\right|"));
            Assert.Equal("|", Attr(d.Element(M + "dPr"), "begChr", "val"));
            Assert.Equal("|", Attr(d.Element(M + "dPr"), "endChr", "val"));
        }

        [Fact]
        public void Norm_delim_double_pipe()
        {
            var d = OnlyDelim(LatexToOmml.Convert(@"\left\|x\right\|"));
            Assert.Equal("‖", Attr(d.Element(M + "dPr"), "begChr", "val"));
            Assert.Equal("‖", Attr(d.Element(M + "dPr"), "endChr", "val"));
        }

        [Fact]
        public void Dot_delim_is_invisible()
        {
            var d = OnlyDelim(LatexToOmml.Convert(@"\left.x\right)"));
            Assert.Equal("", Attr(d.Element(M + "dPr"), "begChr", "val"));
            Assert.Equal(")", Attr(d.Element(M + "dPr"), "endChr", "val"));
        }

        [Fact]
        public void Fraction_inside_delim_is_nested()
        {
            var x = LatexToOmml.Convert(@"\left(\frac{1}{x}\right)");
            var d = OnlyDelim(x);
            Assert.Single(d.Descendants(M + "f"));
        }

        [Fact]
        public void Nested_delims_match_by_depth()
        {
            var x = LatexToOmml.Convert(@"\left(\left[x\right]\right)");
            Assert.Equal(2, x.Descendants(M + "d").Count());
            var outer = x.Elements(M + "d").Single();
            Assert.Equal("(", Attr(outer.Element(M + "dPr"), "begChr", "val"));
            var inner = Assert.Single(outer.Descendants(M + "d"));
            Assert.Equal("[", Attr(inner.Element(M + "dPr"), "begChr", "val"));
        }

        // ── Lettres grecques phi / varphi ────────────────────────────────
        // Convention LaTeX (= celle de l'aperçu WpfMath) : \phi = forme
        // fermée/droite (U+03D5 ϕ), \varphi = forme cursive/ouverte (U+03C6 φ).
        // Régression 2026-06-18 : les deux étaient inversés → @p → \varphi
        // s'insérait en ϕ droit alors que l'aperçu montrait le φ cursif.

        [Fact]
        public void Phi_maps_to_closed_form_U03D5()
        {
            Assert.Equal("ϕ", AllText(LatexToOmml.Convert(@"\phi"))); // ϕ GREEK PHI SYMBOL
        }

        [Fact]
        public void Varphi_maps_to_cursive_form_U03C6()
        {
            Assert.Equal("φ", AllText(LatexToOmml.Convert(@"\varphi"))); // φ GREEK SMALL LETTER PHI
        }

        // ── Fractions & racines ──────────────────────────────────────────

        [Fact]
        public void Frac_has_num_and_den()
        {
            var x = LatexToOmml.Convert(@"\frac{1}{x+1}");
            var f = Assert.Single(x.Elements(M + "f"));
            Assert.Equal("1", AllText(f.Element(M + "num")));
            Assert.Equal("x+1", AllText(f.Element(M + "den")));
        }

        [Fact]
        public void Sqrt_without_degree_hides_deg()
        {
            var x = LatexToOmml.Convert(@"\sqrt{x}");
            var rad = Assert.Single(x.Elements(M + "rad"));
            Assert.Equal("1", Attr(rad.Element(M + "radPr"), "degHide", "val"));
            Assert.Equal("x", AllText(rad.Element(M + "e")));
        }

        [Fact]
        public void Sqrt_with_degree()
        {
            var x = LatexToOmml.Convert(@"\sqrt[3]{8}");
            var rad = Assert.Single(x.Elements(M + "rad"));
            Assert.Equal("3", AllText(rad.Element(M + "deg")));
            Assert.Equal("8", AllText(rad.Element(M + "e")));
        }

        // ── Cadre \boxed (encadrer un résultat) ──────────────────────────

        [Fact]
        public void Boxed_emits_borderBox_with_content()
        {
            var x = LatexToOmml.Convert(@"\boxed{x=2}");
            var bb = Assert.Single(x.Elements(M + "borderBox"));
            // Pas de borderBoxPr → défaut Word = 4 bords visibles, aucune barre.
            Assert.Null(bb.Element(M + "borderBoxPr"));
            Assert.Equal("x=2", AllText(bb.Element(M + "e")));
        }

        [Fact]
        public void Boxed_nests_structures()
        {
            var x = LatexToOmml.Convert(@"\boxed{\frac{1}{x}}");
            var bb = Assert.Single(x.Elements(M + "borderBox"));
            Assert.Single(bb.Element(M + "e").Descendants(M + "f"));
        }

        // ── Scripts (exposant / indice) ──────────────────────────────────

        [Fact]
        public void Superscript_takes_last_atom_as_base()
        {
            var x = LatexToOmml.Convert(@"x^{2}");
            var sup = Assert.Single(x.Elements(M + "sSup"));
            Assert.Equal("x", AllText(sup.Element(M + "e")));
            Assert.Equal("2", AllText(sup.Element(M + "sup")));
        }

        [Fact]
        public void Subscript()
        {
            var x = LatexToOmml.Convert(@"U_{n+1}");
            var sub = Assert.Single(x.Elements(M + "sSub"));
            Assert.Equal("U", AllText(sub.Element(M + "e")));
            Assert.Equal("n+1", AllText(sub.Element(M + "sub")));
        }

        [Fact]
        public void Combined_sub_sup()
        {
            var x = LatexToOmml.Convert(@"a_{i}^{2}");
            var ss = Assert.Single(x.Elements(M + "sSubSup"));
            Assert.Equal("a", AllText(ss.Element(M + "e")));
            Assert.Equal("i", AllText(ss.Element(M + "sub")));
            Assert.Equal("2", AllText(ss.Element(M + "sup")));
        }

        [Fact]
        public void Script_base_is_previous_structure_when_text_buffer_empty()
        {
            // Régression « carrés » 2026-06-10 : \mathrm{cm}^{3} — la base du
            // sSup doit être le run « cm » émis par \mathrm, jamais vide.
            var x = LatexToOmml.Convert(@"1\mathrm{cm}^{3}");
            var sup = Assert.Single(x.Descendants(M + "sSup"));
            Assert.Equal("cm", AllText(sup.Element(M + "e")));
            Assert.Equal("3", AllText(sup.Element(M + "sup")));
        }

        // ── N-aires (somme, intégrale) & lim ─────────────────────────────

        [Fact]
        public void Sum_with_bounds()
        {
            var x = LatexToOmml.Convert(@"\sum_{k=1}^{n} f(k)");
            var nary = Assert.Single(x.Elements(M + "nary"));
            Assert.Equal("∑", Attr(nary.Element(M + "naryPr"), "chr", "val"));
            // Somme : bornes empilées au-dessus/dessous.
            Assert.Equal("undOvr", Attr(nary.Element(M + "naryPr"), "limLoc", "val"));
            Assert.Equal("0", Attr(nary.Element(M + "naryPr"), "subHide", "val"));
            Assert.Equal("0", Attr(nary.Element(M + "naryPr"), "supHide", "val"));
            Assert.Equal("k=1", AllText(nary.Element(M + "sub")));
            Assert.Equal("n", AllText(nary.Element(M + "sup")));
        }

        [Fact]
        public void Integral_without_bounds_hides_them()
        {
            var x = LatexToOmml.Convert(@"\int f");
            var nary = Assert.Single(x.Elements(M + "nary"));
            Assert.Equal("∫", Attr(nary.Element(M + "naryPr"), "chr", "val"));
            Assert.Equal("1", Attr(nary.Element(M + "naryPr"), "subHide", "val"));
            Assert.Equal("1", Attr(nary.Element(M + "naryPr"), "supHide", "val"));
        }

        [Fact]
        public void Integral_bounds_are_placed_to_the_right_subSup()
        {
            // Bornes d'intégrale en indice/exposant À DROITE (∫_a^b), pas
            // empilées — convention typo + Word natif.
            var x = LatexToOmml.Convert(@"\int_{-T/2}^{T/2} f");
            var nary = Assert.Single(x.Elements(M + "nary"));
            Assert.Equal("∫", Attr(nary.Element(M + "naryPr"), "chr", "val"));
            Assert.Equal("subSup", Attr(nary.Element(M + "naryPr"), "limLoc", "val"));
        }

        [Fact]
        public void Integral_body_collapses_redundant_spaces()
        {
            // « \int f \, dx » : espace littéral + \,→fine (U+2009) + espace =
            // 3 espaces avant dx dans Word. On les collapse en UN seul
            // (retour user 2026-06-22).
            var x = LatexToOmml.Convert(@"\int_{a}^{b} f \, dx");
            var nary = Assert.Single(x.Elements(M + "nary"));
            var body = AllText(nary.Element(M + "e"));
            // Tout blanc (espace, fine U+2009) normalisé → vérifie zéro doublon.
            var norm = System.Text.RegularExpressions.Regex.Replace(body, @"\s", " ");
            Assert.DoesNotContain("  ", norm);
            // Pas d'espace de TÊTE collé à l'opérateur (corps = « f dx »).
            Assert.False(char.IsWhiteSpace(norm[0]), "corps d'intégrale ne doit pas démarrer par un espace");
        }

        [Fact]
        public void Lim_builds_func_with_limLow()
        {
            // LE bug fondateur de l'insertion OMML (ADR 2026-06-02) : le lim
            // doit ENGLOBER la fraction (limLow avant m:f), jamais l'inverse.
            var x = LatexToOmml.Convert(@"\lim_{x\to 0} \frac{1}{x+1}");
            var func = Assert.Single(x.Elements(M + "func"));
            Assert.Single(func.Descendants(M + "limLow"));
            Assert.Single(func.Element(M + "e").Descendants(M + "f"));
        }

        // ── Accents (vecteurs, chapeaux, barres) ─────────────────────────

        [Theory]
        [InlineData(@"\vec{u}", "⃗", "u")]
        [InlineData(@"\overrightarrow{AB}", "⃗", "AB")]
        [InlineData(@"\hat{a}", "̂", "a")]
        [InlineData(@"\widehat{ABC}", "̂", "ABC")]
        [InlineData(@"\bar{z}", "̅", "z")]
        [InlineData(@"\overline{AB}", "̅", "AB")]
        public void Accents(string latex, string combining, string content)
        {
            var x = LatexToOmml.Convert(latex);
            var acc = Assert.Single(x.Elements(M + "acc"));
            Assert.Equal(combining, Attr(acc.Element(M + "accPr"), "chr", "val"));
            Assert.Equal(content, AllText(acc.Element(M + "e")));
        }

        // ── Matrices & binôme ────────────────────────────────────────────

        [Fact]
        public void Pmatrix_2x2_structure()
        {
            var x = LatexToOmml.Convert("\\begin{pmatrix}1 & 2 \\\\ 3 & 4\\end{pmatrix}");
            var d = OnlyDelim(x);
            Assert.Equal("(", Attr(d.Element(M + "dPr"), "begChr", "val"));
            var m = Assert.Single(d.Descendants(M + "m"));
            var rows = m.Elements(M + "mr").ToList();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, r => Assert.Equal(2, r.Elements(M + "e").Count()));
            Assert.Equal("1", AllText(rows[0].Elements(M + "e").First()));
            Assert.Equal("4", AllText(rows[1].Elements(M + "e").Last()));
        }

        [Fact]
        public void Binom_is_noBar_fraction_in_parens()
        {
            var x = LatexToOmml.Convert(@"\binom{n}{k}");
            var d = OnlyDelim(x);
            var f = Assert.Single(d.Descendants(M + "f"));
            Assert.Equal("noBar", Attr(f.Element(M + "fPr"), "type", "val"));
            Assert.Equal("n", AllText(f.Element(M + "num")));
            Assert.Equal("k", AllText(f.Element(M + "den")));
        }

        // ── Symboles, ensembles, négations ───────────────────────────────

        // L'espace après une commande-NOM (`\leq y`) est un délimiteur LaTeX,
        // consommé — pas un espace visuel. Celui après une accolade fermante
        // (`\mathbb{R} `) est en revanche du contenu : conservé.
        [Theory]
        [InlineData(@"\alpha ", "α")]
        [InlineData(@"\infty ", "∞")]
        [InlineData(@"x\leq y", "x≤y")]
        [InlineData(@"\mathbb{R} ", "ℝ ")]
        [InlineData(@"x\notin A", "x∉A")]
        [InlineData(@"A\not\subset B", "A⊄B")]
        [InlineData(@"A\cup B", "A∪B")]
        [InlineData(@"x\mapsto x", "x↦x")]
        [InlineData(@"1+2+\ldots +n", "1+2+…+n")]
        [InlineData(@"\cdots ", "⋯")]
        [InlineData(@"\vdots ", "⋮")]
        [InlineData(@"\ddots ", "⋱")]
        public void Symbols_map_to_unicode(string latex, string expectedText)
            => Assert.Equal(expectedText, AllText(LatexToOmml.Convert(latex)));

        // ── Culture & espacement ─────────────────────────────────────────

        [Fact]
        public void French_decimal_braces_render_as_comma()
            => Assert.Equal("1,5", AllText(LatexToOmml.Convert("1{,}5")));

        [Fact]
        public void Thin_space_survives_to_omml()
        {
            // « 1 cm » tapé avec espace → 1\,\mathrm{cm} → l'espace fine
            // U+2009 doit être dans le texte (bug « 1cm collé » 2026-06-10).
            Assert.Equal("1 cm", AllText(LatexToOmml.Convert(@"1\,\mathrm{cm}")));
        }

        [Fact]
        public void Negative_space_is_ignored()
            => Assert.Equal("ab", AllText(LatexToOmml.Convert(@"a\!b")));

        [Fact]
        public void Mathrm_content_is_parsed_as_math()
        {
            // Unités composées : \mathrm{m\cdot s^{-1}} — le \cdot et
            // l'exposant doivent être interprétés, pas dumpés en texte brut.
            var x = LatexToOmml.Convert(@"\mathrm{m\cdot s^{-1}}");
            Assert.Contains("⋅", AllText(x));
            var sup = Assert.Single(x.Descendants(M + "sSup"));
            Assert.Equal("s", AllText(sup.Element(M + "e")));
            Assert.Equal("-1", AllText(sup.Element(M + "sup")));
        }

        [Fact]
        public void Unknown_command_degrades_to_raw_name()
            => Assert.Contains("zzunknown", AllText(LatexToOmml.Convert(@"\zzunknown x")));
    }
}
