using System.Collections.Generic;
using MathCursor.Engine;
using MathCursor.Engine.Resolution;
using MathCursor.Engine.Tokenization;
using Xunit;

namespace MathCursor.Engine.Tests.Resolution
{
    /// <summary>
    /// Tests de la pipeline pre-resolvers (Chantier 3, 2026-05-25). On vérifie
    /// que le main loop <c>MathEngine.Resolve</c> short-circuite correctement
    /// sur les pre-resolvers — comportement observable identique à avant le
    /// refactor.
    /// </summary>
    public class PreResolverPipelineTests
    {
        private static MathEngine BuildEngine() => MathEngine.BuildDefault("fr");

        [Fact]
        public void MultiLine_align_block_resolved_by_preresolver()
        {
            var engine = BuildEngine();
            var result = engine.Resolve("a+b\n= c+d");
            Assert.Equal("multiline-align", result.RuleId);
            Assert.Contains(@"\begin{align", result.TopLatex);
        }

        [Fact]
        public void MultiLine_cases_block_resolved_by_preresolver()
        {
            var engine = BuildEngine();
            var result = engine.Resolve("{ x si x>=0\n{ -x sinon");
            Assert.Equal("multiline-cases", result.RuleId);
            Assert.Contains(@"\begin{cases}", result.TopLatex);
        }

        [Fact]
        public void SingleLine_falls_through_preresolvers_to_main_loop()
        {
            var engine = BuildEngine();
            var result = engine.Resolve("a+b");
            // Ne doit PAS matcher un pre-resolver (= ruleId pas multiline-*, pas prefix-match:*).
            Assert.DoesNotContain("multiline", result.RuleId);
            Assert.DoesNotContain("prefix-match", result.RuleId);
        }

        [Fact]
        public void PrefixMatch_resolved_by_preresolver_when_single_word()
        {
            var engine = BuildEngine();
            // `som` est préfixe de `somme` (anchor) — match unique.
            var result = engine.Resolve("som");
            Assert.StartsWith("prefix-match:", result.RuleId);
        }

        [Fact]
        public void PrefixMatchResolver_IsSingleWordStandalone_only_for_single_Word_token()
        {
            var single = new List<Token> { new Token("som", TokenKind.Word, 0, 3) };
            Assert.True(PrefixMatchResolver.IsSingleWordStandalone(single, out var word));
            Assert.Equal("som", word);

            var twoWords = new List<Token>
            {
                new Token("som", TokenKind.Word, 0, 3),
                new Token("k", TokenKind.Word, 4, 5),
            };
            Assert.False(PrefixMatchResolver.IsSingleWordStandalone(twoWords, out _));

            var notWord = new List<Token>
            {
                new Token("+", TokenKind.Symbol, 0, 1),
            };
            Assert.False(PrefixMatchResolver.IsSingleWordStandalone(notWord, out _));
        }
    }
}
