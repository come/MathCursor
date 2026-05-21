using System.Linq;
using Xunit;
using MathCursor.Core.Patterns;
using MathCursor.Core.Patterns.Yaml;

namespace MathCursor.Core.Tests.Patterns.Templates
{
    /// <summary>
    /// Tests du pattern <c>integral</c> (P9c + P9e refacto YAML, 2026-05-21) :
    /// défini dans <c>data/patterns/integral.yaml</c> embedded, instancié via
    /// <see cref="YamlArgListPatternTemplate"/>.
    /// </summary>
    public class IntegralTemplateTests
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
            => new YamlArgListPatternTemplate(PatternSpecLoader.LoadEmbedded("integral.yaml"));

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
        [InlineData("Int")]
        [InlineData("int x 0 1 f(x)")]
        [InlineData("intégrale x 0 1 f(x)")]
        public void Matches_text_heads(string source)
        {
            var m = New().TryMatchHead(Ctx(source));
            Assert.NotNull(m);
        }

        [Fact]
        public void Matches_unicode_integral_head()
        {
            var m = New().TryMatchHead(Ctx("∫ x 0 1 f(x)"));
            Assert.NotNull(m);
        }

        // ─── Expand : 4 slots positionnels ────────────────────────────

        [Fact]
        public void Int_alone_yields_full_template_hint()
        {
            var c = ExpandAll("Int");
            Assert.Equal(@"\int_{}^{}  \, d", c.PreviewLatex);
            Assert.Equal(@"\int_{\square}^{\square} \square \, d\square", c.HintLatex);
            Assert.Equal(20, c.CompletenessScore);
        }

        [Fact]
        public void Int_x_one_slot_filled()
        {
            // var = x, from/to/expr vides
            var c = ExpandAll("Int x");
            Assert.Equal(@"\int_{\square}^{\square} \square \, dx", c.HintLatex);
            Assert.Equal(40, c.CompletenessScore);
        }

        [Fact]
        public void Int_x_0_1_fx_complete()
        {
            var c = ExpandAll("Int x 0 1 f(x)");
            Assert.Equal(@"\int_{0}^{1} f(x) \, dx", c.PreviewLatex);
            Assert.Equal(@"\int_{0}^{1} f(x) \, dx", c.HintLatex);
            Assert.Equal(100, c.CompletenessScore);
        }

        [Fact]
        public void Int_with_infinity_bounds()
        {
            var c = ExpandAll("Int t -oo +oo e^(-t²)");
            Assert.Equal(@"\int_{-\infty}^{+\infty} e^(-t²) \, dt", c.PreviewLatex);
        }

        [Fact]
        public void Description_uses_int_unicode()
        {
            var c = ExpandAll("Int x 0 1 f(x)");
            Assert.Equal("∫_{0}^{1} f(x) dx", c.Description);
        }

        // ─── Mutation source ──────────────────────────────────────────

        [Fact]
        public void Mutation_replaces_Int_with_int_keyword()
        {
            var c = ExpandAll("Int x 0 1 f(x)");
            Assert.Equal("int x 0 1 f(x)", c.Mutation!.Replacement);
        }

        // ─── Robustesse ───────────────────────────────────────────────

        [Fact]
        public void TryMatchHead_null_ctx_returns_null()
        {
            Assert.Null(New().TryMatchHead(null!));
        }
    }
}
