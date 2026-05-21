using Xunit;
using MathCursor.Core.Lattice.Ast;
using MathCursor.Core.Patterns;
using MathCursor.Core.Patterns.Templates;

namespace MathCursor.Core.Tests.Patterns.Templates
{
    /// <summary>
    /// Tests <see cref="IntervalUnionTemplate"/> : single intervals
    /// (closed/open/semi-open), chaînes union/intersection, infini, partial
    /// states (slots vides), boundary function-call vs interval. Étape P4
    /// (ADR <c>2026-05-21-Meta-pattern-templates-vs-ambig-closed</c>).
    /// </summary>
    public class IntervalUnionTemplateTests
    {
        private static PatternScanContext Ctx(string source) =>
            new PatternScanContext(
                topAst: new Atom("Ident", "x"),
                topLatex: source,
                source: source,
                caretOffset: null);

        private static IntervalUnionTemplate New() => new IntervalUnionTemplate();

        private static PatternCompletion ExpandAll(string source)
        {
            var t = New();
            var ctx = Ctx(source);
            var head = t.TryMatchHead(ctx)!;
            return t.Expand(head, ctx)[0];
        }

        // ─── TryMatchHead : détection ─────────────────────────────────

        [Theory]
        [InlineData("[")]
        [InlineData("[0,1]")]
        [InlineData("(0,1)")]
        public void Matches_left_bracket_or_paren_at_start(string source)
        {
            var m = New().TryMatchHead(Ctx(source));
            Assert.NotNull(m);
            Assert.Equal("interval-union", m!.TemplateId);
            Assert.Equal(0, m.SourceStart);
        }

        [Fact]
        public void Rejects_paren_after_letter_function_call()
        {
            // "f(0,1)" : ( précédé par f → function call, pas interval.
            Assert.Null(New().TryMatchHead(Ctx("f(0,1)")));
        }

        [Fact]
        public void Rejects_paren_after_digit_indexing()
        {
            // "2(0,1)" : ( précédé par digit → indice, pas interval.
            Assert.Null(New().TryMatchHead(Ctx("2(0,1)")));
        }

        [Fact]
        public void Accepts_bracket_after_letter_for_left_bracket()
        {
            // "[" est OK même après lettre (pas d'ambig courante).
            var m = New().TryMatchHead(Ctx("x[0,1]"));
            Assert.NotNull(m);
            Assert.Equal(1, m!.SourceStart);
        }

        [Fact]
        public void Returns_null_on_empty_source()
        {
            Assert.Null(New().TryMatchHead(Ctx("")));
        }

        [Fact]
        public void Returns_null_when_no_bracket_in_source()
        {
            Assert.Null(New().TryMatchHead(Ctx("abc 123")));
        }

        // ─── Expand : intervals simples ───────────────────────────────

        [Fact]
        public void Closed_interval_yields_complete_latex()
        {
            var c = ExpandAll("[0,1]");
            Assert.Equal(@"\left[0,1\right]", c.PreviewLatex);
            Assert.Equal(c.PreviewLatex, c.HintLatex);
            Assert.Equal("[0,1]", c.Description);
            Assert.Equal(100, c.CompletenessScore);
            Assert.Null(c.Mutation); // P4 : pas de SourceMutation
        }

        [Fact]
        public void Open_interval_yields_left_paren_right_paren()
        {
            var c = ExpandAll("(0,1)");
            Assert.Equal(@"\left(0,1\right)", c.PreviewLatex);
            Assert.Equal("(0,1)", c.Description);
        }

        [Fact]
        public void Semi_open_right_yields_left_bracket_right_paren()
        {
            var c = ExpandAll("[0,1)");
            Assert.Equal(@"\left[0,1\right)", c.PreviewLatex);
        }

        [Fact]
        public void Semi_open_left_yields_left_paren_right_bracket()
        {
            var c = ExpandAll("(0,1]");
            Assert.Equal(@"\left(0,1\right]", c.PreviewLatex);
        }

        [Fact]
        public void Identifier_bounds_supported()
        {
            var c = ExpandAll("[a,b]");
            Assert.Equal(@"\left[a,b\right]", c.PreviewLatex);
        }

        [Fact]
        public void Decimal_bounds_supported()
        {
            var c = ExpandAll("[0.5,1.5]");
            Assert.Equal(@"\left[0.5,1.5\right]", c.PreviewLatex);
        }

        [Fact]
        public void Spaces_inside_interval_are_tolerated()
        {
            var c = ExpandAll("[ 0 , 1 ]");
            Assert.Equal(@"\left[0,1\right]", c.PreviewLatex);
            Assert.Equal(100, c.CompletenessScore);
        }

        // ─── Bornes infinies ──────────────────────────────────────────

        [Fact]
        public void Plus_oo_as_high_bound()
        {
            var c = ExpandAll("[0,+oo)");
            Assert.Equal(@"\left[0,+oo\right)", c.PreviewLatex);
        }

        [Fact]
        public void Minus_oo_as_low_bound()
        {
            var c = ExpandAll("(-oo,0]");
            Assert.Equal(@"\left(-oo,0\right]", c.PreviewLatex);
        }

        [Fact]
        public void Unicode_infinity_unsigned()
        {
            var c = ExpandAll("[0,∞)");
            Assert.Equal(@"\left[0,∞\right)", c.PreviewLatex);
        }

        // ─── Chaînes union / intersection ─────────────────────────────

        [Fact]
        public void Union_two_intervals_with_U_operator()
        {
            var c = ExpandAll("[0,1]U[3,4]");
            Assert.Equal(@"\left[0,1\right] \cup \left[3,4\right]", c.PreviewLatex);
            Assert.Equal("[0,1]∪[3,4]", c.Description);
        }

        [Fact]
        public void Union_two_intervals_with_unicode_cup()
        {
            var c = ExpandAll("[0,1]∪[3,4]");
            Assert.Equal(@"\left[0,1\right] \cup \left[3,4\right]", c.PreviewLatex);
        }

        [Fact]
        public void Union_two_intervals_with_keyword_union()
        {
            var c = ExpandAll("[0,1] union [3,4]");
            Assert.Equal(@"\left[0,1\right] \cup \left[3,4\right]", c.PreviewLatex);
        }

        [Fact]
        public void Intersection_with_keyword_inter()
        {
            var c = ExpandAll("[0,1] inter [3,4]");
            Assert.Equal(@"\left[0,1\right] \cap \left[3,4\right]", c.PreviewLatex);
            Assert.Equal("[0,1]∩[3,4]", c.Description);
        }

        [Fact]
        public void Intersection_with_unicode_cap()
        {
            var c = ExpandAll("[0,1]∩[3,4]");
            Assert.Equal(@"\left[0,1\right] \cap \left[3,4\right]", c.PreviewLatex);
        }

        [Fact]
        public void Chain_three_intervals()
        {
            var c = ExpandAll("[0,1]U[3,4]U[5,6]");
            Assert.Equal(
                @"\left[0,1\right] \cup \left[3,4\right] \cup \left[5,6\right]",
                c.PreviewLatex);
            Assert.Equal("[0,1]∪[3,4]∪[5,6]", c.Description);
        }

        // ─── États partiels (slots vides) ─────────────────────────────

        [Fact]
        public void Just_left_bracket_yields_hint_with_two_squares()
        {
            var c = ExpandAll("[");
            // PreviewLatex : pas de slots remplis → "\left[,\right]"
            // (chaîne intermédiaire car lo et hi vides sont "" en preview).
            Assert.Equal(@"\left[,\right]", c.PreviewLatex);
            Assert.Equal(@"\left[\square,\square\right]", c.HintLatex);
            Assert.Equal("[▭,▭]", c.Description);
            Assert.True(c.CompletenessScore < 50);
        }

        [Fact]
        public void Lo_filled_hi_empty_yields_hint_with_one_square()
        {
            var c = ExpandAll("[0,");
            Assert.Equal(@"\left[0,\square\right]", c.HintLatex);
            Assert.True(c.CompletenessScore < 100);
        }

        [Fact]
        public void Missing_right_bracket_yields_hint_with_default_mirror()
        {
            // "[0,1" sans bracket fermant → hint suppose "]" (miroir de "[").
            var c = ExpandAll("[0,1");
            Assert.Equal(@"\left[0,1\right]", c.HintLatex);
        }

        [Fact]
        public void Operator_without_following_interval_yields_hint_placeholder()
        {
            // "[0,1]U" sans interval derrière → hint montre `[▭,▭]` pour la suite.
            var c = ExpandAll("[0,1]U");
            Assert.Contains(@"\square", c.HintLatex);
            Assert.Contains(@"\cup", c.HintLatex);
            // Preview cache les squares
            Assert.Equal(@"\left[0,1\right]", c.PreviewLatex);
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
            Assert.Empty(New().Expand(null!, Ctx("[0,1]")));
        }

        [Fact]
        public void Expand_null_ctx_returns_empty()
        {
            var t = New();
            var head = t.TryMatchHead(Ctx("[0,1]"))!;
            Assert.Empty(t.Expand(head, null!));
        }

        // ─── Slots internes ───────────────────────────────────────────

        [Fact]
        public void TryMatchHead_returns_eager_parsed_state_with_filled_slots()
        {
            // P5 : TryMatchHead fait un eager parse complet (state.SourceEnd
            // étendu sur toute la chaîne). Les slots fixes sont remplis si
            // la source est complète.
            var t = New();
            var ctx = Ctx("[0,1]");
            var head = t.TryMatchHead(ctx)!;
            Assert.Equal(0, head.SourceStart);
            Assert.Equal(5, head.SourceEnd); // [0,1] entier consommé
            Assert.False(head.Slots["lo"].IsEmpty);
            Assert.False(head.Slots["hi"].IsEmpty);
            Assert.False(head.Slots["rightBracket"].IsEmpty);
            Assert.True(head.IsComplete);
            var c = t.Expand(head, ctx)[0];
            Assert.Equal(100, c.CompletenessScore);
        }
    }
}
