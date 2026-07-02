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

using System.Linq;
using System.Xml.Linq;
using MathCursor.Host.Blocks;
using MathCursor.Serialization;
using Xunit;

namespace MathCursor.Tests.Host.Blocks
{
    /// <summary>
    /// M2 multiligne : LatexTopLevelSplit (point d'alignement) +
    /// ChainComposer (structure XML des blocs eqArr) — purs, sans Word.
    /// </summary>
    public sealed class ChainComposerTests
    {
        private static readonly XNamespace M = LatexToOmml.M;

        private static string AllText(XElement e)
            => string.Concat(e.Descendants(M + "t").Select(t => t.Value));

        // ── LatexTopLevelSplit ────────────────────────────────────────────

        [Theory]
        [InlineData("f(x)=\\frac{1}{2}", "f(x)", "=\\frac{1}{2}")]
        [InlineData("x\\leq 5", "x", "\\leq 5")]
        [InlineData("2x+y=5", "2x+y", "=5")]
        [InlineData("x<1", "x", "<1")]
        public void Split_at_top_level_relation(string latex, string lhs, string relRhs)
        {
            var (l, r) = LatexTopLevelSplit.Split(latex);
            Assert.Equal(lhs, l);
            Assert.Equal(relRhs, r);
        }

        [Theory]
        [InlineData("2x+1")]                       // pas de relation
        [InlineData("\\frac{a=b}{2}")]             // relation NICHÉE (accolades)
        [InlineData("(a=b)")]                      // relation parenthésée par l'utilisateur
        [InlineData("\\{1,2\\}")]                  // accolades échappées ≠ profondeur
        public void No_top_level_relation_returns_null_rhs(string latex)
        {
            var (l, r) = LatexTopLevelSplit.Split(latex);
            Assert.Equal(latex, l);
            Assert.Null(r);
        }

        [Fact]
        public void Escaped_braces_do_not_mask_a_real_relation()
        {
            var (l, r) = LatexTopLevelSplit.Split("\\{1,2\\}=S");
            Assert.Equal("\\{1,2\\}", l);
            Assert.Equal("=S", r);
        }

        // ── ChainComposer : chaînes ──────────────────────────────────────

        [Fact]
        public void Chain_with_connector_uses_four_columns_three_amp_marks()
        {
            // Dès qu'un connecteur apparaît : 4 colonnes / 3 marques & par ligne
            // — une colonne PAD vide en tête décale la parité d'alignement de
            // l'eqArr natif Word (alternance droite/gauche) pour que le signe
            // retombe en colonne gauche-alignée (la forme 2-& le mettait en
            // colonne droite = désaligné via le walker, bug 2026-06-22).
            // Ligne 1 = équation simple (scindée au =) ; ligne 2 = marqueur-
            // relation « = 2x » ; ligne 3 = connecteur « ⟺ x=1 ».
            var oMath = ChainComposer.ComposeChain(
                new[] { "f(x) = 2x+2-2", "= 2x", "<=> x=1" },
                new[] { "f(x)=2x+2-2", "2x", "x=1" });

            var eqArr = Assert.Single(oMath.Elements(M + "eqArr"));
            var rows = eqArr.Elements(M + "e").ToList();
            Assert.Equal(3, rows.Count);

            // 3 marques & par ligne, sur TOUTES les lignes.
            foreach (var row in rows)
                Assert.Equal(3, row.Elements(M + "r")
                    .Count(r => r.Element(M + "t")?.Value == "&"));

            // [pad & conn & lhs & =rhs] — pad toujours vide ; conn/lhs vides
            // quand absents.
            Assert.Equal("&&f(x)&=2x+2-2", AllText(rows[0]));   // pas de connecteur
            Assert.Equal("&&&=2x", AllText(rows[1]));            // marqueur-relation
            Assert.Equal("&⇔&x&=1", AllText(rows[2]));           // connecteur ⇔
        }

        [Fact]
        public void Chain_line_without_relation_goes_to_lhs_column()
        {
            var oMath = ChainComposer.ComposeChain(
                new[] { "2x+1", "= 2x" },
                new[] { "2x+1", "2x" });
            var rows = oMath.Element(M + "eqArr").Elements(M + "e").ToList();
            Assert.Equal("2x+1&", AllText(rows[0]));
        }

        [Fact]
        public void Chain_without_connector_keeps_single_amp_per_row()
        {
            // Suite de « = » sans ⟺ : même layout 1-& (forme POC « simple »).
            var oMath = ChainComposer.ComposeChain(
                new[] { "f(x) = 2x+2-2", "= 2x" },
                new[] { "f(x)=2x+2-2", "2x" });

            var rows = oMath.Element(M + "eqArr").Elements(M + "e").ToList();
            Assert.Equal(2, rows.Count);
            foreach (var row in rows)
                Assert.Equal(1, row.Elements(M + "r")
                    .Count(r => r.Element(M + "t")?.Value == "&"));
            Assert.Equal("f(x)&=2x+2-2", AllText(rows[0]));
            Assert.Equal("&=2x", AllText(rows[1]));
        }

        // ── ChainComposer : systèmes ─────────────────────────────────────

        [Fact]
        public void System_is_brace_wrapped_eqArr_with_aligned_relations()
        {
            var oMath = ChainComposer.ComposeSystem(new[] { "2x+y=5", "x-y=1" });

            var d = Assert.Single(oMath.Elements(M + "d"));
            Assert.Equal("{", d.Element(M + "dPr")?.Element(M + "begChr")?.Attribute(M + "val")?.Value);
            Assert.Equal("", d.Element(M + "dPr")?.Element(M + "endChr")?.Attribute(M + "val")?.Value);

            var eqArr = Assert.Single(d.Element(M + "e").Elements(M + "eqArr"));
            var rows = eqArr.Elements(M + "e").ToList();
            Assert.Equal(2, rows.Count);
            // 1 marque & par ligne (2 colonnes : lhs | =rhs).
            Assert.Equal("2x+y&=5", AllText(rows[0]));
            Assert.Equal("x-y&=1", AllText(rows[1]));
        }

        [Fact]
        public void System_with_prefix_grafts_it_before_the_brace()
        {
            // f(x) = { 2x ; x  → préfixe « f(x)= » rendu AVANT l'accolade <m:d>.
            var oMath = ChainComposer.ComposeSystem("f(x)=", new[] { "2x", "x" });
            Assert.Single(oMath.Elements(M + "d"));      // une seule accolade
            // le préfixe précède l'accolade dans le flux de l'oMath.
            Assert.StartsWith("f(x)=", AllText(oMath));
            var rows = oMath.Element(M + "d").Element(M + "e").Element(M + "eqArr").Elements(M + "e").ToList();
            Assert.Equal(2, rows.Count);
            Assert.Equal("2x&", AllText(rows[0]));        // ligne sans relation → lhs seul
        }

        [Fact]
        public void System_latex_preview_uses_cases_for_wpfmath()
        {
            // Aperçu popup : `\begin{cases}` (géré par WpfMathAdapter), PAS `aligned`
            // (non supporté par WpfMath 2.1).
            var latex = ChainComposer.ComposeSystemLatex("f(x)=", new[] { "2x", "x" });
            Assert.StartsWith("f(x)=\\begin{cases}", latex);
            Assert.EndsWith("\\end{cases}", latex);

            var noPrefix = ChainComposer.ComposeSystemLatex("", new[] { "2x+y=5", "x-y=1" });
            Assert.StartsWith("\\begin{cases}", noPrefix);
            Assert.Contains("2x+y=5", noPrefix);
            Assert.Contains(" \\\\ ", noPrefix);          // séparateur de lignes
        }

        // ── Détecteur : accolade ouvreuse générique + préfixe ─────────────

        [Theory]
        [InlineData("f(x) = { 2x ; x", 7)]
        [InlineData("{ a ; b", 0)]
        [InlineData("A {1;2", 2)]
        public void Find_unclosed_brace_returns_position(string text, int pos)
            => Assert.Equal(pos, RelationLineDetector.FindUnclosedBrace(text));

        [Theory]
        [InlineData("{a,b}")]      // accolade fermée = ensemble
        [InlineData("2x+1")]       // pas d'accolade
        [InlineData("")]
        public void Find_unclosed_brace_none(string text)
            => Assert.Equal(-1, RelationLineDetector.FindUnclosedBrace(text));

        [Fact]
        public void Split_trailing_relation_detaches_final_sign()
        {
            var m = RelationLineDetector.SplitTrailingRelation("f(x) =");
            Assert.NotNull(m);
            Assert.Equal("f(x)", m.Value.Lhs);
            Assert.Equal("=", m.Value.MarkerLatex);

            var le = RelationLineDetector.SplitTrailingRelation("x <=");
            Assert.Equal("x", le.Value.Lhs);
            Assert.Equal("\\leq ", le.Value.MarkerLatex);

            Assert.Null(RelationLineDetector.SplitTrailingRelation("A"));        // pas de relation
            Assert.Null(RelationLineDetector.SplitTrailingRelation("xapprox")); // frontière de mot
        }

        // ── Détecteur : ouvreur de système ───────────────────────────────

        [Theory]
        [InlineData("{ 2x+y = 5", "2x+y = 5")]
        [InlineData("{2x=1", "2x=1")]
        [InlineData("  { x", "x")]
        public void System_opener_detected(string line, string rest)
        {
            var m = RelationLineDetector.TryDetectSystemOpener(line);
            Assert.NotNull(m);
            Assert.Equal("{", m.MarkerTyped);
            Assert.Equal(rest, m.Rest);
        }

        [Theory]
        [InlineData("x { y")]
        [InlineData("= 2x")]
        [InlineData("")]
        public void Non_opener_lines_yield_null(string line)
            => Assert.Null(RelationLineDetector.TryDetectSystemOpener(line));

        [Fact]
        public void Connector_kind_is_exposed()
        {
            Assert.True(RelationLineDetector.TryDetect("<=> x").IsConnector);
            Assert.False(RelationLineDetector.TryDetect("= x").IsConnector);
            Assert.False(RelationLineDetector.TryDetect("<= 5").IsConnector);
        }
    }
}
