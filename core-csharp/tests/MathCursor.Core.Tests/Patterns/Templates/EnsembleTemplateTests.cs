using Xunit;
using MathCursor.Core.Lattice.Ast;
using MathCursor.Core.Patterns;
using MathCursor.Core.Patterns.Templates;

namespace MathCursor.Core.Tests.Patterns.Templates
{
    /// <summary>
    /// Tests <see cref="EnsembleTemplate"/> : lettres canoniques R/N/Z/Q/C
    /// avec modifiers optionnels (* + -), word boundary, délim terminal,
    /// rejet des contextes math (pi*R, 2R+1). Étape P3 (ADR
    /// <c>2026-05-21-Meta-pattern-templates-vs-ambig-closed</c>).
    /// </summary>
    public class EnsembleTemplateTests
    {
        private static PatternScanContext Ctx(string source, int? caret = null) =>
            new PatternScanContext(
                topAst: new Atom("Ident", source.Length > 0 ? source[0].ToString() : "x"),
                topLatex: source,
                source: source,
                caretOffset: caret);

        private static EnsembleTemplate New() => new EnsembleTemplate();

        // ─── TryMatchHead : détection ─────────────────────────────────

        [Theory]
        [InlineData("R")]
        [InlineData("N")]
        [InlineData("Z")]
        [InlineData("Q")]
        [InlineData("C")]
        public void Matches_single_canonical_letter(string source)
        {
            var m = New().TryMatchHead(Ctx(source));
            Assert.NotNull(m);
            Assert.Equal("ensemble", m!.TemplateId);
            Assert.Equal(0, m.SourceStart);
            Assert.Equal(1, m.SourceEnd);
            Assert.True(m.IsComplete);
            Assert.Empty(m.Slots);
        }

        [Theory]
        [InlineData("R*", 2)]
        [InlineData("R+", 2)]
        [InlineData("R-", 2)]
        [InlineData("R+*", 3)]
        [InlineData("R-*", 3)]
        [InlineData("R*+", 3)]
        [InlineData("N*", 2)]
        [InlineData("Z*", 2)]
        public void Matches_canonical_with_modifiers(string source, int expectedEnd)
        {
            var m = New().TryMatchHead(Ctx(source));
            Assert.NotNull(m);
            Assert.Equal(0, m!.SourceStart);
            Assert.Equal(expectedEnd, m.SourceEnd);
        }

        [Fact]
        public void Returns_null_on_empty_source()
        {
            Assert.Null(New().TryMatchHead(Ctx("")));
        }

        [Fact]
        public void Returns_null_on_no_canonical_letter()
        {
            Assert.Null(New().TryMatchHead(Ctx("xyz")));
        }

        [Fact]
        public void Returns_null_when_letter_followed_by_other_letter_no_boundary()
        {
            // "Roman" : R suivi de o (lettre) → pas de match (mot général).
            Assert.Null(New().TryMatchHead(Ctx("Roman")));
        }

        [Fact]
        public void Rejects_R_inside_word()
        {
            // "ARN" : R en milieu de mot, pas word-boundary gauche.
            // (Premier match candidat = A (pas canonique), puis R (boundary
            // gauche échoue car A est lettre), puis N (boundary gauche échoue
            // car R est lettre)).
            Assert.Null(New().TryMatchHead(Ctx("ARN")));
        }

        [Fact]
        public void Matches_at_offset_after_space()
        {
            // "x app a R" : R commence à offset 8 (word boundary à gauche).
            var m = New().TryMatchHead(Ctx("x app a R"));
            Assert.NotNull(m);
            Assert.Equal(8, m!.SourceStart);
            Assert.Equal(9, m.SourceEnd);
        }

        [Fact]
        public void Matches_R_followed_by_space_then_more()
        {
            // R suivi de espace (terminal délimiter) → match.
            var m = New().TryMatchHead(Ctx("R blabla"));
            Assert.NotNull(m);
            Assert.Equal(0, m!.SourceStart);
            Assert.Equal(1, m.SourceEnd);
        }

        [Fact]
        public void Matches_R_followed_by_comma_or_punctuation()
        {
            Assert.NotNull(New().TryMatchHead(Ctx("R,")));
            Assert.NotNull(New().TryMatchHead(Ctx("R;")));
            Assert.NotNull(New().TryMatchHead(Ctx("R)")));
            Assert.NotNull(New().TryMatchHead(Ctx("R]")));
        }

        [Fact]
        public void Rejects_R_followed_by_digit_or_letter_after_modifier()
        {
            // "R*x" : R* tight puis x → pas délimité terminalement, rejet.
            // (Le préprocesseur legacy rejette aussi car x est lettre.)
            Assert.Null(New().TryMatchHead(Ctx("R*x")));
        }

        [Fact]
        public void Caps_modifiers_at_2()
        {
            // "R+*-" : 3 modifiers consécutifs. Notre règle = max 2. Le 3ème
            // n'est ni capturé ni délimiteur valide → rejet du match.
            // (Le préprocesseur legacy a la même limite.)
            Assert.Null(New().TryMatchHead(Ctx("R+*-")));
        }

        // ─── Expand : production de la complétion ─────────────────────

        [Fact]
        public void Expand_R_yields_mathbb_R_with_bbR_mutation()
        {
            var t = New();
            var ctx = Ctx("R");
            var m = t.TryMatchHead(ctx)!;
            var completions = t.Expand(m, ctx);
            Assert.Single(completions);
            var c = completions[0];
            Assert.Equal("ℝ", c.Description);
            Assert.Equal(@"\mathbb{R}", c.PreviewLatex);
            Assert.Equal(c.PreviewLatex, c.HintLatex);
            Assert.NotNull(c.Mutation);
            Assert.Equal(0, c.Mutation!.Offset);
            Assert.Equal(1, c.Mutation.Length);
            Assert.Equal("bbR", c.Mutation.Replacement);
            Assert.Equal(100, c.CompletenessScore);
        }

        [Fact]
        public void Expand_R_star_yields_mathbb_R_sup_star()
        {
            var t = New();
            var ctx = Ctx("R*");
            var m = t.TryMatchHead(ctx)!;
            var c = t.Expand(m, ctx)[0];
            Assert.Equal("ℝ*", c.Description);
            Assert.Equal(@"\mathbb{R}^*", c.PreviewLatex);
            Assert.Equal("bbR*", c.Mutation!.Replacement);
            Assert.Equal(2, c.Mutation.Length);
        }

        [Fact]
        public void Expand_R_plus_star_uses_braces_for_two_modifiers()
        {
            var t = New();
            var ctx = Ctx("R+*");
            var m = t.TryMatchHead(ctx)!;
            var c = t.Expand(m, ctx)[0];
            Assert.Equal("ℝ+*", c.Description);
            Assert.Equal(@"\mathbb{R}^{+*}", c.PreviewLatex);
            Assert.Equal("bbR+*", c.Mutation!.Replacement);
            Assert.Equal(3, c.Mutation.Length);
        }

        [Theory]
        [InlineData('N', "ℕ", @"\mathbb{N}")]
        [InlineData('Z', "ℤ", @"\mathbb{Z}")]
        [InlineData('Q', "ℚ", @"\mathbb{Q}")]
        [InlineData('C', "ℂ", @"\mathbb{C}")]
        public void Expand_each_canonical_letter_has_unicode_description(
            char letter, string expectedDesc, string expectedLatex)
        {
            var t = New();
            var src = letter.ToString();
            var ctx = Ctx(src);
            var m = t.TryMatchHead(ctx)!;
            var c = t.Expand(m, ctx)[0];
            Assert.Equal(expectedDesc, c.Description);
            Assert.Equal(expectedLatex, c.PreviewLatex);
            Assert.Equal("bb" + letter, c.Mutation!.Replacement);
        }

        [Fact]
        public void Expand_with_offset_emits_mutation_at_correct_source_position()
        {
            // "V x app a R" : R à offset 10.
            var t = New();
            var ctx = Ctx("V x app a R");
            var m = t.TryMatchHead(ctx)!;
            Assert.Equal(10, m.SourceStart);
            var c = t.Expand(m, ctx)[0];
            Assert.Equal(10, c.Mutation!.Offset);
            Assert.Equal(1, c.Mutation.Length);
            Assert.Equal("bbR", c.Mutation.Replacement);
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
            var ctx = Ctx("R");
            Assert.Empty(New().Expand(null!, ctx));
        }

        [Fact]
        public void Expand_null_ctx_returns_empty()
        {
            var pm = new PatternMatch("ensemble", 0, 1,
                EmptySlotsForTest.Instance, isComplete: true);
            Assert.Empty(New().Expand(pm, null!));
        }

        // Helper pour le test d'expand-null-ctx (EmptySlots est internal).
        private static class EmptySlotsForTest
        {
            public static readonly System.Collections.Generic.IReadOnlyDictionary<string, SlotValue> Instance
                = new System.Collections.Generic.Dictionary<string, SlotValue>(0);
        }
    }
}
