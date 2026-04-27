using MathCursor.Core.Lattice;
using MathCursor.Core.Lattice.Ast;
using Xunit;

namespace MathCursor.Core.Tests.Lattice
{
    /// <summary>
    /// Tests du renderer LaTeX. On test deux niveaux :
    /// 1) Rendu direct d'AST construit à la main (isole le renderer)
    /// 2) Pipeline complet Lex → TopK → Parse → Render (intégration)
    /// </summary>
    public sealed class LatexRendererTests
    {
        private static string RenderTop(string input)
        {
            var edges = Lexer.Lex(input);
            var paths = LatticePathFinder.TopK(edges, input.Length, 3);
            var ast = new Parser(paths[0].Edges).Parse();
            return LatexRenderer.Render(ast);
        }

        // ------------------ Atomes / Holes / Const ------------------

        [Fact]
        public void Atom_ident()
            => Assert.Equal("x", LatexRenderer.Render(new Atom("ident", "x")));

        [Fact]
        public void Atom_number()
            => Assert.Equal("42", LatexRenderer.Render(new Atom("number", "42")));

        [Fact]
        public void Atom_greek_prefixed_with_backslash()
            => Assert.Equal("\\pi", LatexRenderer.Render(new Atom("greek", "pi")));

        [Fact]
        public void Hole_renders_circled_glyph()
        {
            Assert.Equal("①", LatexRenderer.Render(new Hole(1)));
            Assert.Equal("②", LatexRenderer.Render(new Hole(2)));
            Assert.Equal("④", LatexRenderer.Render(new Hole(4)));
        }

        [Fact]
        public void Const_renders_value_directly()
            => Assert.Equal("\\infty", LatexRenderer.Render(new Const("\\infty")));

        // ------------------ Bin / Unary ------------------

        [Fact]
        public void Bin_plus()
            => Assert.Equal("n+1", RenderTop("n+1"));

        [Fact]
        public void Bin_loose_plus()
            => Assert.Equal("n + 1".Replace(" ", ""), RenderTop("n + 1").Replace(" ", ""));

        [Fact]
        public void Bin_explicit_mult_uses_cdot()
        {
            // 2*x explicite → 2\cdot x
            var ast = new Bin("*", false, false, new Atom("number", "2"), new Atom("ident", "x"));
            Assert.Equal("2\\cdot x", LatexRenderer.Render(ast));
        }

        [Fact]
        public void Bin_implicit_mult_concatenates()
            => Assert.Equal("2x", RenderTop("2x"));

        [Fact]
        public void Unary_minus()
            => Assert.Equal("-x", RenderTop("-x"));

        // ------------------ Sup / Sub ------------------

        [Fact]
        public void Sup_x_pow_2()
            => Assert.Equal("x^{2}", RenderTop("x^2"));

        [Fact]
        public void Sub_u_n()
            => Assert.Equal("u_{n}", RenderTop("u_n"));

        [Fact]
        public void Sup_unwraps_group()
        {
            // x^(n+1) : le Group autour de n+1 doit être unwrappé dans les {}
            Assert.Equal("x^{n+1}", RenderTop("x^(n+1)"));
        }

        // ------------------ Group ------------------

        [Fact]
        public void Group_uses_left_right_parens()
            => Assert.Equal("\\left(x+y\\right)", RenderTop("(x+y)"));

        // ------------------ Func ------------------

        [Fact]
        public void Func_atom_arg_uses_space()
            => Assert.Equal("\\cos x", RenderTop("cos x"));

        [Fact]
        public void Func_implicit_tight_arg_uses_space()
            => Assert.Equal("\\cos 2x", RenderTop("cos2x"));

        [Fact]
        public void Func_group_arg_no_extra_parens()
            => Assert.Equal("\\cos\\left(x+y\\right)", RenderTop("cos(x+y)"));

        // ------------------ Frac / Sqrt / Vec ------------------

        [Fact]
        public void Frac_unwraps_args()
            => Assert.Equal("\\frac{a}{b}", RenderTop("frac a b"));

        [Fact]
        public void Frac_with_hole_for_den()
            => Assert.Equal("\\frac{a}{②}", RenderTop("frac a"));

        [Fact]
        public void Sqrt_unwraps_arg()
            => Assert.Equal("\\sqrt{x}", RenderTop("racine x"));

        [Fact]
        public void Sqrt_with_hole()
            => Assert.Equal("\\sqrt{①}", RenderTop("racine"));

        [Fact]
        public void Vec_with_name()
            => Assert.Equal("\\vec{AB}", RenderTop("vec AB"));

        [Fact]
        public void Vec_alone_renders_hole()
            => Assert.Equal("\\vec{①}", RenderTop("vec"));

        // ------------------ Sum / Lim / Int ------------------

        [Fact]
        public void Sum_complete()
            => Assert.Equal(
                "\\sum_{k=1}^{n+1} \\cos 2x",
                RenderTop("sum k=1 n+1 cos2x"));

        [Fact]
        public void Sum_partial_with_holes()
            => Assert.Equal("\\sum_{k=②}^{③} ④", RenderTop("sum k"));

        [Fact]
        public void Lim_with_arrow()
            => Assert.Equal("\\lim_{x \\to 0} f\\left(x\\right)", RenderTop("lim x -> 0 f(x)"));

        [Fact]
        public void Int_simple()
            => Assert.Equal("\\int_{0}^{1} x", RenderTop("int 0 1 x"));

        // ------------------ Composabilité ------------------

        [Fact]
        public void Sum_with_nested_racine()
            => Assert.Equal("\\sum_{k=1}^{n} \\sqrt{k}", RenderTop("sum k=1 n racine k"));

        [Fact]
        public void Lim_with_nested_frac_sin()
            => Assert.Equal(
                "\\lim_{x \\to 0} \\frac{\\sin x}{x}",
                RenderTop("lim x 0 frac sin x x"));

        [Fact]
        public void Nested_sum_within_sum()
            => Assert.Equal(
                "\\sum_{i=1}^{n} \\sum_{j=1}^{i} ij",
                RenderTop("sum i=1 n sum j=1 i ij"));

        [Fact]
        public void Sum_body_stops_at_loose_plus()
            // body = f(k), puis Sum + g(k) au top-level
            => Assert.Equal(
                "\\sum_{k=1}^{n} f\\left(k\\right)+g\\left(k\\right)",
                RenderTop("sum k 1 n f(k) + g(k)"));

        [Fact]
        public void Sum_body_consumes_tight_plus()
            => Assert.Equal(
                "\\sum_{k=1}^{n} f\\left(k\\right)+1",
                RenderTop("sum k 1 n f(k)+1"));

        // ------------------ Cas non-triviaux du brief ------------------

        [Fact]
        public void Racine_of_racine()
            => Assert.Equal("\\sqrt{\\sqrt{x}}", RenderTop("racine racine x"));

        [Fact]
        public void Int_with_frac_body()
            => Assert.Equal("\\int_{0}^{1} \\frac{x}{x+1}", RenderTop("int 0 1 frac x x+1"));
    }
}
