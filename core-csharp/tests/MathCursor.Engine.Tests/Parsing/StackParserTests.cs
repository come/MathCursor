using Xunit;
using MathCursor.Engine.Ast;
using MathCursor.Engine.Parsing;
using MathCursor.Engine.Tokenization;
using MathCursor.Engine.Vocabulary;

namespace MathCursor.Engine.Tests.Parsing
{
    /// <summary>
    /// Tests passe-pile P11.4-6 sur cas plats : atoms, infixes, précédence,
    /// délimiteurs. Les ancres seront testées en P11.10 quand le RuleLoader
    /// est branché.
    /// </summary>
    public class StackParserTests
    {
        private static AstNode? Parse(string src)
        {
            var vocab = LocaleVocabulary.LoadEmbedded("fr");
            var tokens = new Tokenizer(vocab).Tokenize(src);
            return new StackParser(vocab).Parse(tokens);
        }

        // ─── Atomes ───────────────────────────────────────────────────

        [Fact]
        public void Single_word_yields_atom()
        {
            var ast = Parse("x");
            var atom = Assert.IsType<AtomNode>(ast);
            Assert.Equal("x", atom.Text);
            Assert.Equal("word", atom.AtomKind);
        }

        [Fact]
        public void Single_number_yields_atom()
        {
            var ast = Parse("3,14");
            var atom = Assert.IsType<AtomNode>(ast);
            Assert.Equal("3,14", atom.Text);
            Assert.Equal("number", atom.AtomKind);
        }

        // ─── Infixe simple ────────────────────────────────────────────

        [Fact]
        public void Plus_yields_infix()
        {
            var ast = Parse("a+b");
            var infix = Assert.IsType<InfixNode>(ast);
            Assert.Equal("+", infix.Op);
        }

        [Fact]
        public void Mul_div_higher_than_add()
        {
            // a + b * c → a + (b * c)
            var ast = Parse("a+b*c");
            var root = Assert.IsType<InfixNode>(ast);
            Assert.Equal("+", root.Op);
            Assert.IsType<AtomNode>(root.Left);
            var rightInfix = Assert.IsType<InfixNode>(root.Right);
            Assert.Equal(@"\cdot", rightInfix.Op);
        }

        [Fact]
        public void Comparison_lower_than_arithmetic()
        {
            // a + b = c → (a + b) = c
            var ast = Parse("a+b=c");
            var root = Assert.IsType<InfixNode>(ast);
            Assert.Equal("=", root.Op);
            var leftInfix = Assert.IsType<InfixNode>(root.Left);
            Assert.Equal("+", leftInfix.Op);
        }

        [Fact]
        public void Implies_lower_than_comparison()
        {
            // x = 1 => y = 2 → (x = 1) => (y = 2)
            var ast = Parse("x = 1 => y = 2");
            var root = Assert.IsType<InfixNode>(ast);
            Assert.Equal(@"\implies", root.Op);
        }

        // ─── Délimiteurs ──────────────────────────────────────────────

        [Fact]
        public void Simple_parens_yield_group()
        {
            var ast = Parse("(x+1)");
            var grp = Assert.IsType<GroupNode>(ast);
            Assert.Equal("(", grp.Open);
            Assert.Equal(")", grp.Close);
            Assert.IsType<InfixNode>(grp.Body);
        }

        [Fact]
        public void Brackets_yield_group()
        {
            var ast = Parse("[a]");
            var grp = Assert.IsType<GroupNode>(ast);
            Assert.Equal("[", grp.Open);
            Assert.Equal("]", grp.Close);
        }

        [Fact]
        public void Couple_with_comma_is_list()
        {
            var ast = Parse("(a,b)");
            var grp = Assert.IsType<GroupNode>(ast);
            var list = Assert.IsType<ListNode>(grp.Body);
            Assert.Equal(",", list.Sep);
            Assert.Equal(2, list.Items.Count);
        }

        [Fact]
        public void Triple_with_semicolons_is_list()
        {
            var ast = Parse("(a ; b ; c)");
            var grp = Assert.IsType<GroupNode>(ast);
            var list = Assert.IsType<ListNode>(grp.Body);
            Assert.Equal(";", list.Sep);
            Assert.Equal(3, list.Items.Count);
        }

        [Fact]
        public void Matrix_shape_2x2_is_list_of_rows()
        {
            // (a b ; c d) au stade StackParser brut (avant ListCombinator) :
            // GroupNode → ListNode(';', items=[?, ?]).
            // P11.7 ListCombinator promouvra en MatrixNode.
            var ast = Parse("(a b ; c d)");
            var grp = Assert.IsType<GroupNode>(ast);
            var list = Assert.IsType<ListNode>(grp.Body);
            Assert.Equal(";", list.Sep);
            Assert.Equal(2, list.Items.Count);
        }

        // ─── Robustesse ───────────────────────────────────────────────

        [Fact]
        public void Empty_yields_null()
        {
            Assert.Null(Parse(""));
        }

        [Fact]
        public void Trailing_operator_does_not_crash()
        {
            // "a +" : on ne consomme pas l'op final faute d'opérande droit.
            var ast = Parse("a +");
            Assert.IsType<AtomNode>(ast);
        }
    }
}
