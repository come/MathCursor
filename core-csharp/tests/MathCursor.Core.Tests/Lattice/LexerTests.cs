using System.Linq;
using MathCursor.Core.Lattice;
using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Core.Tests.Lattice
{
    public sealed class LexerTests
    {
        private readonly ITestOutputHelper _log;
        public LexerTests(ITestOutputHelper log) { _log = log; }

        [Fact]
        public void Empty_input_yields_no_edges()
        {
            Assert.Empty(Lexer.Lex(""));
        }

        [Fact]
        public void Single_letter_emits_one_ident_edge()
        {
            var edges = Lexer.Lex("x");
            Assert.Single(edges);
            Assert.Equal(EdgeType.Ident, edges[0].Type);
            Assert.Equal("x", edges[0].Value);
            Assert.Equal(5, edges[0].Weight);
        }

        [Fact]
        public void Cos_emits_keyword_or_function_plus_split_idents()
        {
            // "cos" : 1 arête fonction (poids 1) + 6 arêtes ident (c, o, s, co, os, cos)
            var edges = Lexer.Lex("cos");
            Assert.Contains(edges, e => e.Type == EdgeType.Function && e.Value == "cos" && e.Weight == 1);
            Assert.Contains(edges, e => e.Type == EdgeType.Ident && e.Value == "c" && e.Weight == 5);
            Assert.Contains(edges, e => e.Type == EdgeType.Ident && e.Value == "co" && e.Weight == 18 + 2 * 3);
            Assert.Contains(edges, e => e.Type == EdgeType.Ident && e.Value == "cos" && e.Weight == 18 + 3 * 3);
        }

        [Fact]
        public void Pi_emits_greek_and_split_idents()
        {
            var edges = Lexer.Lex("pi");
            Assert.Contains(edges, e => e.Type == EdgeType.Greek && e.Value == "pi" && e.Weight == 1);
            Assert.Contains(edges, e => e.Type == EdgeType.Ident && e.Value == "p");
            Assert.Contains(edges, e => e.Type == EdgeType.Ident && e.Value == "i");
        }

        [Theory]
        [InlineData("n+1", true)]
        [InlineData("n + 1", false)]
        [InlineData("n +1", false)]
        [InlineData("n+ 1", false)]
        public void Tight_flag_reflects_adjacent_whitespace(string input, bool expectedTight)
        {
            var edges = Lexer.Lex(input);
            var plus = edges.First(e => e.Type == EdgeType.Op && e.Value == "+");
            Assert.Equal(expectedTight, plus.Tight);
        }

        [Fact]
        public void Multichar_op_recognized()
        {
            var edges = Lexer.Lex("x<=2");
            Assert.Contains(edges, e => e.Type == EdgeType.Op && e.Value == "<=");
        }

        [Fact]
        public void Number_consumes_digits_only_dot_is_op()
        {
            // ADR 30-04 Feat-dot-as-multiplier : `.` n'est plus partie du
            // nombre, c'est un Op de multiplication. `3.14` → 3 tokens
            // (Number=3, Op=., Number=14). L'alt décimal `3{,}14` est exposée
            // via cascade RuleDecimalVsMultiplication.
            var edges = Lexer.Lex("3.14");
            Assert.Contains(edges, e => e.Type == EdgeType.Number && e.Value == "3" && e.Start == 0 && e.End == 1);
            Assert.Contains(edges, e => e.Type == EdgeType.Op && e.Value == "." && e.Start == 1 && e.End == 2);
            Assert.Contains(edges, e => e.Type == EdgeType.Number && e.Value == "14" && e.Start == 2 && e.End == 4);
        }

        [Fact]
        public void Keyword_lim_recognized()
        {
            var edges = Lexer.Lex("lim x 0");
            Assert.Contains(edges, e => e.Type == EdgeType.Keyword && e.Value == "lim" && e.Weight == 0);
        }
    }
}
