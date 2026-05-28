using Xunit;
using MathCursor.Engine.Ast;
using MathCursor.Engine.Emit;
using MathCursor.Engine.Parsing;
using MathCursor.Engine.Parsing.List;
using MathCursor.Engine.Tokenization;
using MathCursor.Engine.Vocabulary;

namespace MathCursor.Engine.Tests.Emit
{
    /// <summary>
    /// Tests <see cref="LatexEmitter"/> end-to-end : <c>source → tokens →
    /// AST → LaTeX</c>. Couvre les golden cases plats du brief §5 (= ceux qui
    /// ne nécessitent pas encore le rule loader YAML / les ancres).
    /// </summary>
    public class LatexEmitterTests
    {
        private static string EmitFr(string src)
        {
            var vocab = LocaleVocabulary.LoadEmbedded("fr");
            var tokens = new Tokenizer(vocab).Tokenize(src);
            var ast = new StackParser(vocab).Parse(tokens);
            ast = ListCombinator.Promote(ast);
            return new LatexEmitter().Emit(ast);
        }

        // ─── Golden cases brief §5 (= ceux indépendants des ancres) ───

        [Fact]
        public void Golden_ab_is_product()
        {
            // brief §5 : `(ab) -> ab (produit)`.
            Assert.Equal(@"(ab)", EmitFr("(ab)"));
        }

        [Fact]
        public void Golden_aspaceb_is_product_no_space()
        {
            // brief §5 : `(a b) -> ab (produit — espace cosmétique)`.
            // L'Emitter rend \cdot implicite SANS espace (cosmétique).
            Assert.Equal(@"(ab)", EmitFr("(a b)"));
        }

        [Fact]
        public void Golden_matrix_2x2()
        {
            // brief §5 : `(a b ; c d) -> \begin{pmatrix} a & b \\ c & d \end{pmatrix}`.
            var got = EmitFr("(a b ; c d)");
            Assert.Equal(@"\begin{pmatrix}a & b \\ c & d\end{pmatrix}", got);
        }

        [Fact]
        public void Golden_precedence_fractions_arith()
        {
            // brief §5 : `1/x+1/y -> (1/x)+(1/y) (précédence)`.
            // P13 : pas d'espace autour de + (= conv math compact).
            var got = EmitFr("1/x+1/y");
            Assert.Equal(@"\frac{1}{x}+\frac{1}{y}", got);
        }

        [Fact]
        public void Golden_a_div_b_plus_c_is_aDivB_plus_c()
        {
            // brief §5 : `a/b+c -> (a/b)+c défaut` (le slurp est un candidat alt).
            // P13 : pas d'espace autour de +.
            var got = EmitFr("a/b+c");
            Assert.Equal(@"\frac{a}{b}+c", got);
        }

        [Fact]
        public void Golden_parallel_geometry()
        {
            // P28 : `//` rendu penché via \mathbin{/\!/} (= demande user
            // pour distinguer du \parallel vertical ‖).
            var got = EmitFr("(AB) // (AC)");
            Assert.Equal(@"(AB) \mathbin{/\!/} (AC)", got);
        }

        // ─── Placeholders (= P11.8 spécifique) ────────────────────────

        [Fact]
        public void Placeholder_renders_as_square()
        {
            var ph = PlaceholderNode.Instance;
            Assert.Equal(@"\square", new LatexEmitter().Emit(ph));
        }

        [Fact]
        public void Matrix_with_padding_emits_square()
        {
            // (a b ; c) → 2x2 avec placeholder en (1,1).
            var got = EmitFr("(a b ; c)");
            Assert.Equal(@"\begin{pmatrix}a & b \\ c & \square\end{pmatrix}", got);
        }

        // ─── Précédence multi-tier ────────────────────────────────────

        [Fact]
        public void Implies_lower_than_compare()
        {
            // x = 1 => y = 2 → (x = 1) ⇒ (y = 2)
            var got = EmitFr("x = 1 => y = 2");
            Assert.Equal(@"x = 1 \implies y = 2", got);
        }

        [Fact]
        public void Set_union_inside_paren()
        {
            // (a U b) U-set ⇒ \cup
            var got = EmitFr("(a U b)");
            Assert.Equal(@"(a \cup b)", got);
        }
    }
}
