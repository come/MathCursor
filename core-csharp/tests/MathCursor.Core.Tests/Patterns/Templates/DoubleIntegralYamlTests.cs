using System.Linq;
using Xunit;
using MathCursor.Core.Patterns;
using MathCursor.Core.Patterns.Yaml;

namespace MathCursor.Core.Tests.Patterns.Templates
{
    /// <summary>
    /// Tests du pattern <c>double_integral</c> (P9h, 2026-05-21) — défini
    /// uniquement via <c>data/patterns/double_integral.yaml</c> embedded.
    /// Validation que le DSL YAML supporte ce nouveau cas (heads iint/∬,
    /// 3 slots var1/var2/expression, rendu \iint expr \, dvar1 \, dvar2).
    /// </summary>
    public class DoubleIntegralYamlTests
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
            => new YamlArgListPatternTemplate(PatternSpecLoader.LoadEmbedded("double_integral.yaml"));

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
        [InlineData("iint")]
        [InlineData("Iint")]
        [InlineData("intint")]
        public void Matches_text_heads(string source)
        {
            var m = New().TryMatchHead(Ctx(source));
            Assert.NotNull(m);
            Assert.Equal("double_integral", m!.TemplateId);
        }

        [Fact]
        public void Matches_unicode_double_integral_head()
        {
            var m = New().TryMatchHead(Ctx("∬ x y f(x,y)"));
            Assert.NotNull(m);
        }

        // ─── Expand : 3 slots positionnels ────────────────────────────

        [Fact]
        public void Iint_alone_yields_full_template_hint()
        {
            var c = ExpandAll("iint");
            Assert.Contains(@"\square", c.HintLatex);
            Assert.Equal(25, c.CompletenessScore);
        }

        [Fact]
        public void Iint_complete_with_all_slots()
        {
            var c = ExpandAll("iint x y f(x,y)");
            Assert.Equal(@"\iint f(x,y) \, dx \, dy", c.PreviewLatex);
            Assert.Equal(100, c.CompletenessScore);
        }

        [Fact]
        public void Iint_with_multi_token_expression()
        {
            var c = ExpandAll("iint x y f(x,y)+g(x,y)");
            Assert.Equal(@"\iint f(x,y)+g(x,y) \, dx \, dy", c.PreviewLatex);
        }

        [Fact]
        public void Iint_partial_with_one_var()
        {
            var c = ExpandAll("iint x");
            Assert.Contains(@"\square", c.HintLatex);
            Assert.Equal(50, c.CompletenessScore);
        }

        [Fact]
        public void Iint_with_two_vars_no_expression()
        {
            var c = ExpandAll("iint x y");
            Assert.Contains(@"\square", c.HintLatex);
            Assert.Contains(@"dx", c.HintLatex);
            Assert.Contains(@"dy", c.HintLatex);
            Assert.Equal(75, c.CompletenessScore);
        }

        [Fact]
        public void Description_uses_unicode_double_integral()
        {
            var c = ExpandAll("iint x y f(x,y)");
            Assert.Equal("∬ f(x,y) dx dy", c.Description);
        }

        // ─── Mutation source ──────────────────────────────────────────

        [Fact]
        public void Mutation_replaces_iint_head()
        {
            var c = ExpandAll("Iint x y f(x,y)");
            Assert.Equal("iint x y f(x,y)", c.Mutation!.Replacement);
        }

        // ─── Validation YAML chargé ────────────────────────────────────

        [Fact]
        public void Loaded_spec_has_correct_structure()
        {
            var spec = PatternSpecLoader.LoadEmbedded("double_integral.yaml");
            Assert.Equal("double_integral", spec.TemplateId);
            Assert.Equal(4, spec.Heads.Count);
            Assert.Equal(3, spec.Slots.Count);
            Assert.True(spec.Slots[2].MultiToken);
        }
    }
}
