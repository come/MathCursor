using System.Linq;
using Xunit;
using MathCursor.Core.Patterns;
using MathCursor.Core.Patterns.Templates;

namespace MathCursor.Core.Tests.Patterns.Templates
{
    /// <summary>
    /// Tests <see cref="MatrixTemplate"/> (P9f, 2026-05-21). 3 modes :
    /// auto-detect (multi-completion via diviseurs), explicit sep
    /// (virgule/`;`), head paramétré (mat3x4). Cf. ADR
    /// <c>2026-05-21-Feat-matrix-pattern</c>.
    /// </summary>
    public class MatrixTemplateTests
    {
        private static PatternScanContext Ctx(string source) =>
            new PatternScanContext(
                topAst: null,
                topLatex: source,
                source: source,
                caretOffset: null,
                startPos: 0,
                registry: null);

        private static MatrixTemplate New() => new MatrixTemplate();

        // Helper : récupère LE delimiter actif (pmatrix en culture FR par défaut).
        private static string Delim => MathCursor.Core.Lattice.LatexRenderer.GlobalOptions.MatrixDelim;

        // ─── Heads ────────────────────────────────────────────────────

        [Theory]
        [InlineData("mat")]
        [InlineData("Mat a b c d")]
        [InlineData("matrice 1 2 3")]
        [InlineData("matrix x y z")]
        public void Matches_all_text_heads(string source)
        {
            var m = New().TryMatchHead(Ctx(source));
            Assert.NotNull(m);
            Assert.Equal("matrix", m!.TemplateId);
        }

        [Fact]
        public void Rejects_mat_in_word()
        {
            // "matérial" : mat suivi de "érial" lettres → rejet
            Assert.Null(New().TryMatchHead(Ctx("matérial")));
        }

        // ─── Mode 3 : head paramétré mat<n>x<m> ──────────────────────

        [Fact]
        public void Mat3x4_head_captures_explicit_dim()
        {
            var m = New().TryMatchHead(Ctx("mat3x4 a b c d"));
            Assert.NotNull(m);
            Assert.True(m!.Slots.ContainsKey("explicit_rows"));
            Assert.True(m.Slots.ContainsKey("explicit_cols"));
            Assert.Equal("3", ((FilledSlotAtom)m.Slots["explicit_rows"]).Text);
            Assert.Equal("4", ((FilledSlotAtom)m.Slots["explicit_cols"]).Text);
            Assert.Equal(6, m.SourceEnd); // "mat3x4" = 6 chars (index 0..5)
        }

        [Fact]
        public void Mat2x2_with_4_args_complete()
        {
            var t = New();
            var ctx = Ctx("mat2x2 a b c d");
            var head = t.TryMatchHead(ctx)!;
            var completions = t.Expand(head, ctx);
            Assert.Single(completions);
            var c = completions[0];
            Assert.Contains("a & b", c.PreviewLatex);
            Assert.Contains("c & d", c.PreviewLatex);
            Assert.Equal(100, c.CompletenessScore);
        }

        [Fact]
        public void Mat3x4_with_fewer_args_shows_squares()
        {
            var t = New();
            var ctx = Ctx("mat3x4 a b c"); // 3 args, mais 12 attendus
            var head = t.TryMatchHead(ctx)!;
            var completions = t.Expand(head, ctx);
            Assert.Single(completions);
            var c = completions[0];
            Assert.Contains(@"\square", c.HintLatex);
            Assert.True(c.CompletenessScore < 100);
        }

        [Fact]
        public void Mat_without_dim_not_captured_as_explicit_dim()
        {
            // "mat 3 4 5" → pas de dim explicite (pas de "3x4" tight)
            var m = New().TryMatchHead(Ctx("mat 3 4 5"));
            Assert.NotNull(m);
            Assert.False(m!.Slots.ContainsKey("explicit_rows"));
        }

        // ─── Mode 1 : auto-detect ─────────────────────────────────────

        [Fact]
        public void Mat_alone_yields_1x1_template()
        {
            var t = New();
            var ctx = Ctx("mat");
            var head = t.TryMatchHead(ctx)!;
            var completions = t.Expand(head, ctx);
            Assert.Single(completions);
            Assert.Contains(@"\begin{" + Delim + "}", completions[0].HintLatex);
        }

        [Fact]
        public void Mat_4_args_yields_3_layouts()
        {
            // 4 args → divisors [1×4, 4×1, 2×2]. Tri "proche du carré"
            // donne 2×2 en premier (|2-2|=0), puis 1×4 et 4×1 (|1-4|=3).
            var t = New();
            var ctx = Ctx("mat a b c d");
            var head = t.TryMatchHead(ctx)!;
            var completions = t.Expand(head, ctx);
            Assert.Equal(3, completions.Count);
            // 2×2 doit être en premier
            Assert.Contains("matrice 2×2", completions[0].Description);
        }

        [Fact]
        public void Mat_6_args_yields_4_layouts()
        {
            // 6 → [2×3, 3×2, 1×6, 6×1] (carrés proches en premier)
            var t = New();
            var ctx = Ctx("mat 1 2 3 4 5 6");
            var head = t.TryMatchHead(ctx)!;
            var completions = t.Expand(head, ctx);
            Assert.Equal(4, completions.Count);
        }

        [Fact]
        public void Mat_5_args_yields_2_layouts_prime()
        {
            // 5 (premier) → [1×5, 5×1]
            var t = New();
            var ctx = Ctx("mat a b c d e");
            var head = t.TryMatchHead(ctx)!;
            var completions = t.Expand(head, ctx);
            Assert.Equal(2, completions.Count);
        }

        [Fact]
        public void Mat_9_args_yields_3_layouts_with_3x3_first()
        {
            // 9 → [3×3, 1×9, 9×1] (3×3 carré exact en premier)
            var t = New();
            var ctx = Ctx("mat a b c d e f g h i");
            var head = t.TryMatchHead(ctx)!;
            var completions = t.Expand(head, ctx);
            Assert.Equal(3, completions.Count);
            Assert.Contains("3×3", completions[0].Description);
        }

        // ─── Mode 2 : séparateurs explicites ──────────────────────────

        [Fact]
        public void Mat_with_explicit_comma_and_semi()
        {
            // "mat 1, 2 ; 3, 4" = 2×2 explicite
            var t = New();
            var ctx = Ctx("mat 1, 2 ; 3, 4");
            var head = t.TryMatchHead(ctx)!;
            var completions = t.Expand(head, ctx);
            Assert.Single(completions);
            var c = completions[0];
            Assert.Contains("1 & 2", c.PreviewLatex);
            Assert.Contains("3 & 4", c.PreviewLatex);
        }

        [Fact]
        public void Mat_explicit_sep_uneven_rows_padded()
        {
            // "mat 1, 2 ; 3" = 2 lignes mais ligne 2 incomplète
            // → 2×2 avec cell[1][1] = null (= square dans hint)
            var t = New();
            var ctx = Ctx("mat 1, 2 ; 3");
            var head = t.TryMatchHead(ctx)!;
            var c = t.Expand(head, ctx)[0];
            Assert.Contains(@"\square", c.HintLatex);
        }

        [Fact]
        public void Mat_explicit_sep_allows_expression_with_spaces()
        {
            // L'intérêt du mode explicit-sep : permettre expressions complexes
            // dans les cells qui contiennent des espaces.
            var t = New();
            var ctx = Ctx("mat sin x, cos x ; tan x, cot x");
            var head = t.TryMatchHead(ctx)!;
            var c = t.Expand(head, ctx)[0];
            Assert.Contains("sin x & cos x", c.PreviewLatex);
            Assert.Contains("tan x & cot x", c.PreviewLatex);
        }

        // ─── Délimiteur culture-aware ─────────────────────────────────

        [Fact]
        public void Uses_culture_aware_delimiter()
        {
            // FR par défaut → pmatrix. Si l'env est différent, le test
            // valide juste que le délimiteur de RenderOptions est utilisé.
            var t = New();
            var ctx = Ctx("mat2x2 a b c d");
            var head = t.TryMatchHead(ctx)!;
            var c = t.Expand(head, ctx)[0];
            Assert.Contains($"\\begin{{{Delim}}}", c.PreviewLatex);
            Assert.Contains($"\\end{{{Delim}}}", c.PreviewLatex);
        }

        // ─── Mutation source ──────────────────────────────────────────

        [Fact]
        public void Mutation_normalizes_to_canonical_form()
        {
            // "mat a b c d" (4 args, auto-detect) → 1ère completion = 2×2
            // Mutation : "mat2x2 a b c d" (= forme canonique normalisée)
            var t = New();
            var ctx = Ctx("mat a b c d");
            var head = t.TryMatchHead(ctx)!;
            var c = t.Expand(head, ctx)[0];
            Assert.Equal("mat2x2 a b c d", c.Mutation!.Replacement);
        }

        // ─── Robustesse ───────────────────────────────────────────────

        [Fact]
        public void TryMatchHead_null_ctx_returns_null()
        {
            Assert.Null(New().TryMatchHead(null!));
        }

        [Fact]
        public void Expand_null_state_returns_empty()
        {
            Assert.Empty(New().Expand(null!, Ctx("mat a b")));
        }
    }
}
