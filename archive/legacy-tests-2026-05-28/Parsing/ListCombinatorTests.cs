using Xunit;
using MathCursor.Engine.Ast;
using MathCursor.Engine.Parsing;
using MathCursor.Engine.Parsing.List;
using MathCursor.Engine.Tokenization;
using MathCursor.Engine.Vocabulary;

namespace MathCursor.Engine.Tests.Parsing
{
    /// <summary>
    /// Tests <see cref="ListCombinator"/> : promotion ListNode → MatrixNode
    /// 2D quand rowsep <c>;</c> est présent. Brief §1.3.
    /// </summary>
    public class ListCombinatorTests
    {
        private static AstNode? ParseAndPromote(string src)
        {
            var vocab = LocaleVocabulary.LoadEmbedded("fr");
            var tokens = new Tokenizer(vocab).Tokenize(src);
            var ast = new StackParser(vocab).Parse(tokens);
            return ListCombinator.Promote(ast);
        }

        // ─── Cas matrix 2x2 brief ─────────────────────────────────────

        [Fact]
        public void Matrix_2x2_promoted_from_semi_listing()
        {
            // (a b ; c d) → MatrixNode 2x2
            var ast = ParseAndPromote("(a b ; c d)");
            var grp = Assert.IsType<GroupNode>(ast);
            var mat = Assert.IsType<MatrixNode>(grp.Body);
            Assert.Equal(2, mat.RowCount);
            Assert.Equal(2, mat.ColCount);
            Assert.Equal("a", ((AtomNode)mat.Rows[0][0]).Text);
            Assert.Equal("b", ((AtomNode)mat.Rows[0][1]).Text);
            Assert.Equal("c", ((AtomNode)mat.Rows[1][0]).Text);
            Assert.Equal("d", ((AtomNode)mat.Rows[1][1]).Text);
        }

        [Fact]
        public void Matrix_2x3()
        {
            var ast = ParseAndPromote("(a b c ; d e f)");
            var grp = Assert.IsType<GroupNode>(ast);
            var mat = Assert.IsType<MatrixNode>(grp.Body);
            Assert.Equal(2, mat.RowCount);
            Assert.Equal(3, mat.ColCount);
        }

        [Fact]
        public void Matrix_uneven_padded_with_placeholders()
        {
            // (a b ; c) → 2x2 avec placeholder en (1,1).
            var ast = ParseAndPromote("(a b ; c)");
            var grp = Assert.IsType<GroupNode>(ast);
            var mat = Assert.IsType<MatrixNode>(grp.Body);
            Assert.Equal(2, mat.RowCount);
            Assert.Equal(2, mat.ColCount);
            Assert.IsType<PlaceholderNode>(mat.Rows[1][1]);
        }

        // ─── No semi → pas de promotion matrix ────────────────────────

        [Fact]
        public void Comma_list_stays_list_not_matrix()
        {
            // (a,b,c) reste ListNode "," (= couple/triple), pas matrix.
            var ast = ParseAndPromote("(a,b,c)");
            var grp = Assert.IsType<GroupNode>(ast);
            var list = Assert.IsType<ListNode>(grp.Body);
            Assert.Equal(",", list.Sep);
        }

        [Fact]
        public void Single_expression_no_sep_stays_group()
        {
            var ast = ParseAndPromote("(a+1)");
            var grp = Assert.IsType<GroupNode>(ast);
            Assert.IsType<InfixNode>(grp.Body);
        }

        // ─── (a b) = produit (brief §4) ───────────────────────────────

        [Fact]
        public void AspaceB_is_product_not_row_brief_4()
        {
            // Brief §4 : `(a b) = PRODUIT` (espace cosmétique).
            // Sans rowsep → reste produit infixe \cdotIM. Pas matrix.
            var ast = ParseAndPromote("(a b)");
            var grp = Assert.IsType<GroupNode>(ast);
            var prod = Assert.IsType<InfixNode>(grp.Body);
            Assert.Equal(@"\cdotIM", prod.Op);
        }

        // ─── Promotion préserve les nœuds non-list ────────────────────

        [Fact]
        public void Promote_passes_through_atoms()
        {
            var vocab = LocaleVocabulary.LoadEmbedded("fr");
            var tokens = new Tokenizer(vocab).Tokenize("x");
            var ast = new StackParser(vocab).Parse(tokens);
            var promoted = ListCombinator.Promote(ast);
            Assert.IsType<AtomNode>(promoted);
        }

        [Fact]
        public void Promote_null_returns_null()
        {
            Assert.Null(ListCombinator.Promote(null));
        }
    }
}
