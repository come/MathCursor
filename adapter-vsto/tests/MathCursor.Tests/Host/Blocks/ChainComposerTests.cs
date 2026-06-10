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
        public void Chain_with_connector_uses_three_columns_two_amp_marks()
        {
            // Forme V4/V5 prouvée par le docx de variantes (2026-06-10) :
            // dès qu'un connecteur apparaît, 3 colonnes / 2 marques & par
            // ligne — la forme single-& désalignait les lignes à ⟺ (V1-V3).
            // Ligne 1 = équation simple (scindée au =) ; ligne 2 = marqueur-
            // relation « = 2x » ; ligne 3 = connecteur « ⟺ x=1 ».
            var oMath = ChainComposer.ComposeChain(
                new[] { "f(x) = 2x+2-2", "= 2x", "<=> x=1" },
                new[] { "f(x)=2x+2-2", "2x", "x=1" });

            var eqArr = Assert.Single(oMath.Elements(M + "eqArr"));
            var rows = eqArr.Elements(M + "e").ToList();
            Assert.Equal(3, rows.Count);

            // 2 marques & par ligne, sur TOUTES les lignes.
            foreach (var row in rows)
                Assert.Equal(2, row.Elements(M + "r")
                    .Count(r => r.Element(M + "t")?.Value == "&"));

            Assert.Equal("&f(x)&=2x+2-2", AllText(rows[0]));
            // Ligne 2 (marqueur-relation) : colonnes 1 et 2 vides.
            Assert.Equal("&&=2x", AllText(rows[1]));
            // Ligne 3 (connecteur) : ⇔ en colonne 1.
            Assert.Equal("⇔&x&=1", AllText(rows[2]));
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
