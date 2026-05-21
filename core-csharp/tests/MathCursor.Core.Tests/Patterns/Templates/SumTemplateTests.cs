using System.Linq;
using Xunit;
using MathCursor.Core.Patterns;
using MathCursor.Core.Patterns.Yaml;

namespace MathCursor.Core.Tests.Patterns.Templates
{
    /// <summary>
    /// Tests du pattern <c>sum</c> (P9b + P9e refacto YAML, 2026-05-21) :
    /// défini dans <c>data/patterns/sum.yaml</c> embedded, instancié via
    /// <see cref="YamlArgListPatternTemplate"/>. Comportement identique à
    /// l'ancien <c>SumTemplate.cs</c> (supprimé en P9e).
    /// </summary>
    public class SumTemplateTests
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
            => new YamlArgListPatternTemplate(PatternSpecLoader.LoadEmbedded("sum.yaml"));

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
        [InlineData("Sum", 3)]
        [InlineData("sum", 3)]
        [InlineData("somme", 5)]
        public void Matches_text_heads(string source, int expectedEnd)
        {
            var m = New().TryMatchHead(Ctx(source));
            Assert.NotNull(m);
            Assert.Equal("sum", m!.TemplateId);
            Assert.Equal(0, m.SourceStart);
            Assert.Equal(expectedEnd, m.SourceEnd);
        }

        [Fact]
        public void Matches_unicode_Sigma_head()
        {
            var m = New().TryMatchHead(Ctx("Σ k 0 n k²"));
            Assert.NotNull(m);
        }

        [Fact]
        public void Matches_unicode_n_ary_sum_head()
        {
            var m = New().TryMatchHead(Ctx("∑ k 0 n k²"));
            Assert.NotNull(m);
        }

        [Fact]
        public void Rejects_Sum_in_word()
        {
            // "Sumatra" : Sum suivi de "atra" → rejet
            Assert.Null(New().TryMatchHead(Ctx("Sumatra")));
        }

        // ─── Expand : remplissage progressif des 4 slots ──────────────

        [Fact]
        public void Sum_alone_yields_full_template_hint()
        {
            var c = ExpandAll("Sum");
            Assert.Equal(@"\sum_{=}^{} ", c.PreviewLatex);
            Assert.Equal(@"\sum_{\square=\square}^{\square} \square", c.HintLatex);
            Assert.Equal(20, c.CompletenessScore);
        }

        [Fact]
        public void Sum_k_one_slot_filled()
        {
            var c = ExpandAll("Sum k");
            Assert.Equal(@"\sum_{k=\square}^{\square} \square", c.HintLatex);
            Assert.Equal(40, c.CompletenessScore);
        }

        [Fact]
        public void Sum_k_0_two_slots_filled()
        {
            var c = ExpandAll("Sum k 0");
            Assert.Equal(@"\sum_{k=0}^{\square} \square", c.HintLatex);
            Assert.Equal(60, c.CompletenessScore);
        }

        [Fact]
        public void Sum_k_0_n_three_slots_filled()
        {
            var c = ExpandAll("Sum k 0 n");
            Assert.Equal(@"\sum_{k=0}^{n} \square", c.HintLatex);
            Assert.Equal(80, c.CompletenessScore);
        }

        [Fact]
        public void Sum_k_0_n_k2_complete()
        {
            var c = ExpandAll("Sum k 0 n k²");
            Assert.Equal(@"\sum_{k=0}^{n} k²", c.PreviewLatex);
            Assert.Equal(@"\sum_{k=0}^{n} k²", c.HintLatex);
            Assert.Equal(100, c.CompletenessScore);
            Assert.DoesNotContain(@"\square", c.HintLatex);
        }

        // ─── Conversions infini ───────────────────────────────────────

        [Fact]
        public void Plus_oo_in_to_renders_as_plus_infty()
        {
            var c = ExpandAll("Sum n 1 +oo 1/n²");
            Assert.Equal(@"\sum_{n=1}^{+\infty} 1/n²", c.PreviewLatex);
        }

        [Fact]
        public void Unicode_infinity_in_to()
        {
            var c = ExpandAll("Sum n 0 +∞ 1/n²");
            Assert.Equal(@"\sum_{n=0}^{+\infty} 1/n²", c.PreviewLatex);
        }

        // ─── Expression multi-tokens ──────────────────────────────────

        [Fact]
        public void Expression_with_spaces_concatenated()
        {
            // "Sum k 0 n k * 2" : 5 args, var=k, from=0, to=n, expr="k * 2"
            var c = ExpandAll("Sum k 0 n k * 2");
            Assert.Equal(@"\sum_{k=0}^{n} k * 2", c.PreviewLatex);
        }

        // ─── Description Unicode ──────────────────────────────────────

        [Fact]
        public void Description_uses_Sigma_unicode()
        {
            var c = ExpandAll("Sum k 0 n k²");
            Assert.Equal("Σ_{k=0}^{n} k²", c.Description);
        }

        [Fact]
        public void Description_for_empty_uses_squares()
        {
            var c = ExpandAll("Sum");
            Assert.Contains("Σ_{▭=▭}^{▭}", c.Description);
        }

        // ─── Mutation source ──────────────────────────────────────────

        [Fact]
        public void Mutation_replaces_Sum_with_sum_keyword()
        {
            var c = ExpandAll("Sum k 0 n k²");
            Assert.NotNull(c.Mutation);
            Assert.Equal(0, c.Mutation!.Offset);
            Assert.Equal("sum k 0 n k²", c.Mutation.Replacement);
        }

        // ─── Robustesse ───────────────────────────────────────────────

        [Fact]
        public void TryMatchHead_null_ctx_returns_null()
        {
            Assert.Null(New().TryMatchHead(null!));
        }

        [Fact]
        public void Expand_null_state_returns_empty()
        {
            Assert.Empty(New().Expand(null!, Ctx("Sum k 0 n k²")));
        }
    }
}
