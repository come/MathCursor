using System.Linq;
using Xunit;
using MathCursor.Core.Patterns;
using MathCursor.Core.Patterns.Templates;

namespace MathCursor.Core.Tests.Patterns.Templates
{
    /// <summary>
    /// Tests <see cref="LimTemplate"/> (P9a, 2026-05-21) : head Lim/lim,
    /// 3 slots positionnels var/limit/expression, rendu \lim_{var \to limit}
    /// expression, conversion +oo / -oo / ∞ vers \infty, hints carrés pour
    /// slots vides. Cf. ADR <c>2026-05-21-Feat-lim-pattern</c>.
    /// </summary>
    public class LimTemplateTests
    {
        private static PatternScanContext Ctx(string source) =>
            new PatternScanContext(
                topAst: null,
                topLatex: source,
                source: source,
                caretOffset: null,
                startPos: 0,
                registry: null);

        private static LimTemplate New() => new LimTemplate();

        private static PatternCompletion ExpandAll(string source)
        {
            var t = New();
            var ctx = Ctx(source);
            var head = t.TryMatchHead(ctx);
            Assert.NotNull(head);
            return t.Expand(head!, ctx).First();
        }

        // ─── Heads Lim / lim ──────────────────────────────────────────

        [Theory]
        [InlineData("Lim")]
        [InlineData("Lim x")]
        [InlineData("Lim x 0 f(x)")]
        public void Matches_Lim_uppercase_head(string source)
        {
            var m = New().TryMatchHead(Ctx(source));
            Assert.NotNull(m);
            Assert.Equal("lim", m!.TemplateId);
            Assert.Equal(0, m.SourceStart);
            Assert.Equal(3, m.SourceEnd);
        }

        [Fact]
        public void Matches_lim_lowercase_head()
        {
            var m = New().TryMatchHead(Ctx("lim x 0 f(x)"));
            Assert.NotNull(m);
            Assert.Equal("lim", ((FilledSlotAtom)m!.Slots["polarity"]).Text);
        }

        [Fact]
        public void Rejects_Lim_in_word_no_boundary()
        {
            // "Limit" : Lim suivi de "it" (lettres) → rejet
            Assert.Null(New().TryMatchHead(Ctx("Limit")));
        }

        [Fact]
        public void Rejects_Lim_after_letter()
        {
            // "xLim" : Lim précédé de x (lettre) → rejet
            Assert.Null(New().TryMatchHead(Ctx("xLim")));
        }

        // ─── Expand : remplissage progressif des slots ────────────────

        [Fact]
        public void Lim_alone_yields_full_template_hint()
        {
            var c = ExpandAll("Lim");
            Assert.Equal(@"\lim_{ \to } ", c.PreviewLatex);
            Assert.Equal(@"\lim_{\square \to \square} \square", c.HintLatex);
            Assert.Equal("lim_▭→▭ ▭", c.Description);
            Assert.Equal(25, c.CompletenessScore);
        }

        [Fact]
        public void Lim_x_one_slot_filled()
        {
            var c = ExpandAll("Lim x");
            Assert.Equal(@"\lim_{x \to } ", c.PreviewLatex);
            Assert.Equal(@"\lim_{x \to \square} \square", c.HintLatex);
            Assert.Equal(50, c.CompletenessScore);
        }

        [Fact]
        public void Lim_x_0_two_slots_filled()
        {
            var c = ExpandAll("Lim x 0");
            Assert.Equal(@"\lim_{x \to 0} ", c.PreviewLatex);
            Assert.Equal(@"\lim_{x \to 0} \square", c.HintLatex);
            Assert.Equal(75, c.CompletenessScore);
        }

        [Fact]
        public void Lim_x_0_fx_complete()
        {
            var c = ExpandAll("Lim x 0 f(x)");
            Assert.Equal(@"\lim_{x \to 0} f(x)", c.PreviewLatex);
            Assert.Equal(@"\lim_{x \to 0} f(x)", c.HintLatex);
            Assert.Equal(100, c.CompletenessScore);
            Assert.DoesNotContain(@"\square", c.HintLatex);
        }

        // ─── Conversions infini ───────────────────────────────────────

        [Fact]
        public void Plus_oo_renders_as_plus_infty()
        {
            var c = ExpandAll("Lim x +oo 1/x");
            Assert.Equal(@"\lim_{x \to +\infty} 1/x", c.PreviewLatex);
            Assert.Equal("lim_x→+∞ 1/x", c.Description);
        }

        [Fact]
        public void Minus_oo_renders_as_minus_infty()
        {
            var c = ExpandAll("Lim x -oo g(x)");
            Assert.Equal(@"\lim_{x \to -\infty} g(x)", c.PreviewLatex);
        }

        [Fact]
        public void Unicode_infinity_supported()
        {
            var c = ExpandAll("Lim x +∞ f(x)");
            Assert.Equal(@"\lim_{x \to +\infty} f(x)", c.PreviewLatex);
        }

        [Fact]
        public void Infini_keyword_supported()
        {
            var c = ExpandAll("Lim x +infini f(x)");
            Assert.Equal(@"\lim_{x \to +\infty} f(x)", c.PreviewLatex);
        }

        // ─── Expression multi-tokens ──────────────────────────────────

        [Fact]
        public void Expression_with_spaces_concatenated()
        {
            // "Lim x 0 f x" : args = [x, 0, f, x]. var=x, limit=0,
            // expression = "f x" (concat depuis arg[2])
            var c = ExpandAll("Lim x 0 f x");
            Assert.Equal(@"\lim_{x \to 0} f x", c.PreviewLatex);
        }

        // ─── Mutation source ──────────────────────────────────────────

        [Fact]
        public void Mutation_replaces_Lim_with_lim_keyword()
        {
            // "Lim x 0 f(x)" → "lim x 0 f(x)" (= head muté vers keyword
            // canonique, le reste préservé)
            var c = ExpandAll("Lim x 0 f(x)");
            Assert.NotNull(c.Mutation);
            Assert.Equal(0, c.Mutation!.Offset);
            Assert.Equal("lim x 0 f(x)", c.Mutation.Replacement);
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
            Assert.Empty(New().Expand(null!, Ctx("Lim x 0 f(x)")));
        }
    }
}
