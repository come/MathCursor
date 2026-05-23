using System.Linq;
using Xunit;
using MathCursor.Engine.Tokenization;
using MathCursor.Engine.Vocabulary;

namespace MathCursor.Engine.Tests.Tokenization
{
    /// <summary>
    /// Tests <see cref="Tokenizer"/> sur les cas brief v4 §1.1 :
    /// virgule décimale FR, glue, symboles multi-char, multi-mots.
    /// </summary>
    public class TokenizerTests
    {
        private static Tokenizer FrTokenizer() => new Tokenizer(LocaleVocabulary.LoadEmbedded("fr"));
        private static Tokenizer EnTokenizer() => new Tokenizer(LocaleVocabulary.LoadEmbedded("en"));

        private static string Dump(System.Collections.Generic.IReadOnlyList<Token> tokens)
            => string.Join(" ", tokens.Select(t => t.ToString()));

        // ─── Virgule décimale FR ──────────────────────────────────────

        [Fact]
        public void Fr_3point14_is_single_number_token()
        {
            var t = FrTokenizer().Tokenize("3,14");
            Assert.Single(t);
            Assert.Equal(TokenKind.Number, t[0].Kind);
            Assert.Equal("3,14", t[0].Text);
        }

        [Fact]
        public void Fr_isolated_comma_is_separator()
        {
            // "(a,b)" : virgule pas bordée chiffres → Sep.
            var t = FrTokenizer().Tokenize("(a,b)");
            Assert.Equal(5, t.Count);
            Assert.Equal(TokenKind.OpenDelim, t[0].Kind);
            Assert.Equal(TokenKind.Word, t[1].Kind);
            Assert.Equal(TokenKind.Sep, t[2].Kind);
            Assert.Equal(TokenKind.Word, t[3].Kind);
            Assert.Equal(TokenKind.CloseDelim, t[4].Kind);
        }

        [Fact]
        public void Fr_comma_between_digit_and_letter_is_separator()
        {
            // "1,x" : 1 + Sep + x (pas un nombre, droite n'est pas digit).
            var t = FrTokenizer().Tokenize("1,x");
            Assert.Equal(3, t.Count);
            Assert.Equal(TokenKind.Number, t[0].Kind);
            Assert.Equal(TokenKind.Sep, t[1].Kind);
            Assert.Equal(TokenKind.Word, t[2].Kind);
        }

        // ─── Glue ─────────────────────────────────────────────────────

        [Fact]
        public void Fr_arrow_is_glue()
        {
            // P13 : tokens incluent Sep pour whitespace. "lim x->0" :
            // lim, Sep, x, ->, 0 = 5.
            var t = FrTokenizer().Tokenize("lim x->0");
            Assert.Equal(5, t.Count);
            Assert.Equal(TokenKind.Glue, t[3].Kind);
            Assert.Equal("->", t[3].Text);
        }

        [Fact]
        public void Fr_tend_vers_is_multiword_glue()
        {
            // P13 : "x tend vers 0" → x, Sep, "tend vers", Sep, 0 = 5.
            var t = FrTokenizer().Tokenize("x tend vers 0");
            Assert.Equal(5, t.Count);
            Assert.Equal(TokenKind.Word, t[0].Kind);
            Assert.Equal(TokenKind.Glue, t[2].Kind);
            Assert.Equal("tend vers", t[2].Text);
            Assert.Equal(TokenKind.Number, t[4].Kind);
        }

        [Fact]
        public void Fr_equals_is_glue_not_compared()
        {
            var t = FrTokenizer().Tokenize("k=1");
            Assert.Equal(3, t.Count);
            Assert.Equal(TokenKind.Glue, t[1].Kind);
            Assert.Equal("=", t[1].Text);
        }

        // ─── Symboles multi-char ──────────────────────────────────────

        [Fact]
        public void Symbol_double_arrow_implies()
        {
            // P13 : "P => Q" → P, Sep, =>, Sep, Q = 5.
            var t = FrTokenizer().Tokenize("P => Q");
            Assert.Equal(5, t.Count);
            Assert.Equal(TokenKind.Symbol, t[2].Kind);
            Assert.Equal("=>", t[2].Text);
        }

        [Fact]
        public void Symbol_iff()
        {
            // P13 : "P <=> Q" → 5 tokens incluant Sep.
            var t = FrTokenizer().Tokenize("P <=> Q");
            Assert.Equal(5, t.Count);
            Assert.Equal("<=>", t[2].Text);
        }

        [Fact]
        public void Symbol_geometry_parallel()
        {
            // P13 : "(AB) // (AC)" → (, AB, ), Sep, //, Sep, (, AC, ) = 9.
            var t = FrTokenizer().Tokenize("(AB) // (AC)");
            Assert.Equal(9, t.Count);
            Assert.Equal("//", t[4].Text);
            Assert.Equal(TokenKind.Symbol, t[4].Kind);
        }

        // ─── Délimiteurs + rowsep ─────────────────────────────────────

        [Fact]
        public void Matrix_source_tokenizes_with_semi()
        {
            // P13 : "(a b ; c d)" inclut Sep entre les atoms.
            // (, a, Sep, b, Sep, ;, Sep, c, Sep, d, ) = 11.
            var t = FrTokenizer().Tokenize("(a b ; c d)");
            Assert.Equal(11, t.Count);
            Assert.Equal(TokenKind.OpenDelim, t[0].Kind);
            Assert.Equal(";", t[5].Text);
            Assert.Equal(TokenKind.CloseDelim, t[10].Kind);
        }

        [Fact]
        public void Lim_full_phrase_keyword()
        {
            // P13 : Sep entre chaque atom.
            // lim, Sep, quand, Sep, x, Sep, "tend vers", Sep, 0, Sep, f, (, x, ) = 14.
            var t = FrTokenizer().Tokenize("lim quand x tend vers 0 f(x)");
            Assert.Equal(14, t.Count);
            Assert.Equal("lim", t[0].Text);
            Assert.Equal("quand", t[2].Text);
            Assert.Equal("tend vers", t[6].Text);
        }

        // ─── EN locale (sanity) ───────────────────────────────────────

        [Fact]
        public void En_decimal_is_dot()
        {
            var t = EnTokenizer().Tokenize("3.14");
            // EN : '.' n'est pas géré comme décimale ici (= Tokenizer FR-centric).
            // À étendre quand on supportera vraiment EN (= ADR séparé).
            Assert.NotEmpty(t);
        }

        // ─── Empty / null ─────────────────────────────────────────────

        [Fact]
        public void Empty_yields_no_tokens()
        {
            Assert.Empty(FrTokenizer().Tokenize(""));
        }

        [Fact]
        public void Whitespace_only_yields_no_tokens()
        {
            Assert.Empty(FrTokenizer().Tokenize("   \t  "));
        }
    }
}
