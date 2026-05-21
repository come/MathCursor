using System.Linq;
using Xunit;
using MathCursor.Core.Patterns;
using MathCursor.Core.Patterns.Yaml;

namespace MathCursor.Core.Tests.Patterns.Templates
{
    /// <summary>
    /// Tests du pattern <c>derivative</c> (P9d + P9e refacto YAML, 2026-05-21) :
    /// défini dans <c>data/patterns/derivative.yaml</c> embedded.
    /// </summary>
    public class DerivativeTemplateTests
    {
        private static PatternScanContext Ctx(string source) =>
            new PatternScanContext(
                topAst: null,
                topLatex: source,
                source: source,
                caretOffset: null,
                startPos: 0,
                registry: null);

        private static YamlArgListPatternTemplate New()
            => new YamlArgListPatternTemplate(PatternSpecLoader.LoadEmbedded("derivative.yaml"));

        private static PatternCompletion ExpandAll(string source)
        {
            var t = New();
            var ctx = Ctx(source);
            var head = t.TryMatchHead(ctx);
            Assert.NotNull(head);
            return t.Expand(head!, ctx).First();
        }

        // ─── Heads ────────────────────────────────────────────────────

        [Theory]
        [InlineData("Derive")]
        [InlineData("derive x f(x)")]
        [InlineData("dérivée x f(x)")]
        [InlineData("dérive t e^t")]
        public void Matches_text_heads(string source)
        {
            var m = New().TryMatchHead(Ctx(source));
            Assert.NotNull(m);
        }

        [Fact]
        public void Rejects_Derive_in_word()
        {
            // "Derivative" : Derive suivi de "ative" lettres → rejet
            Assert.Null(New().TryMatchHead(Ctx("Derivative")));
        }

        // ─── Expand : 2 slots positionnels ────────────────────────────

        [Fact]
        public void Derive_alone_yields_full_template_hint()
        {
            var c = ExpandAll("Derive");
            Assert.Equal(@"\frac{d}{d\square} \square", c.HintLatex);
            Assert.Equal(33, c.CompletenessScore);
        }

        [Fact]
        public void Derive_x_one_slot_filled()
        {
            var c = ExpandAll("Derive x");
            Assert.Equal(@"\frac{d}{dx} \square", c.HintLatex);
            Assert.Equal(66, c.CompletenessScore);
        }

        [Fact]
        public void Derive_x_fx_complete()
        {
            var c = ExpandAll("Derive x f(x)");
            Assert.Equal(@"\frac{d}{dx} f(x)", c.PreviewLatex);
            Assert.Equal(@"\frac{d}{dx} f(x)", c.HintLatex);
            Assert.Equal(100, c.CompletenessScore);
        }

        [Fact]
        public void Derive_with_multi_token_expression()
        {
            // "Derive x x²+1" : var=x, expr="x²+1" via ConcatArgsFrom
            var c = ExpandAll("Derive x x²+1");
            Assert.Equal(@"\frac{d}{dx} x²+1", c.PreviewLatex);
        }

        [Fact]
        public void Description_uses_d_dx_notation()
        {
            var c = ExpandAll("Derive x f(x)");
            Assert.Equal("d/dx f(x)", c.Description);
        }

        // ─── Mutation source ──────────────────────────────────────────

        [Fact]
        public void Mutation_replaces_Derive_with_derive_keyword()
        {
            var c = ExpandAll("Derive x f(x)");
            Assert.Equal("derive x f(x)", c.Mutation!.Replacement);
        }

        // ─── Robustesse ───────────────────────────────────────────────

        [Fact]
        public void TryMatchHead_null_ctx_returns_null()
        {
            Assert.Null(New().TryMatchHead(null!));
        }
    }
}
