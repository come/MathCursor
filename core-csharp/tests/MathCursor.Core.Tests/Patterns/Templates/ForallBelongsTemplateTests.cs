using System.Linq;
using Xunit;
using MathCursor.Core.Patterns;
using MathCursor.Core.Patterns.Templates;

namespace MathCursor.Core.Tests.Patterns.Templates
{
    /// <summary>
    /// Tests unitaires <see cref="ForallBelongsTemplate"/> (post-refacto P5R
    /// 2026-05-21 : convention args espace). Couvre : head V/E/∀/∃, parsing
    /// args, classification var vs domain (= dernier arg = ensemble),
    /// rendering, mutation source.
    ///
    /// <para>Tests "openers" (app a / appartient / dans / in / (- / ∈)
    /// retirés au passage P5R — ces tokens ne sont plus reconnus. Cf. ADR
    /// <c>2026-05-21-Refactor-forall-belongs-arglist-convention</c>.</para>
    ///
    /// <para>Pour les tests bout-en-bout compositionnel (V x R, V x [0,1]U[3,4]),
    /// voir <c>ForallBelongsCompositionTests</c>.</para>
    /// </summary>
    public class ForallBelongsTemplateTests
    {
        private static PatternScanContext Ctx(string source, PatternRegistry? registry = null) =>
            new PatternScanContext(
                topAst: null,
                topLatex: source,
                source: source,
                caretOffset: null,
                startPos: 0,
                registry: registry);

        private static ForallBelongsTemplate New() => new ForallBelongsTemplate();

        private static PatternCompletion ExpandAll(string source, PatternRegistry? registry = null)
        {
            var t = New();
            var ctx = Ctx(source, registry);
            var head = t.TryMatchHead(ctx);
            Assert.NotNull(head);
            return t.Expand(head!, ctx).First();
        }

        // ─── TryMatchHead : V/E head detection ────────────────────────

        [Theory]
        [InlineData("V")]
        [InlineData("V x")]
        [InlineData("V x R")]
        public void Matches_V_head(string source)
        {
            var m = New().TryMatchHead(Ctx(source));
            Assert.NotNull(m);
            Assert.Equal("forall-belongs", m!.TemplateId);
            Assert.Equal(0, m.SourceStart);
            Assert.Equal(1, m.SourceEnd);
            Assert.Equal("V", ((FilledSlotAtom)m.Slots["polarity"]).Text);
        }

        [Theory]
        [InlineData("E")]
        [InlineData("E x")]
        public void Matches_E_head(string source)
        {
            var m = New().TryMatchHead(Ctx(source));
            Assert.NotNull(m);
            Assert.Equal("E", ((FilledSlotAtom)m!.Slots["polarity"]).Text);
        }

        [Theory]
        [InlineData("∀ x")]
        [InlineData("∃ x")]
        public void Matches_unicode_heads(string source)
        {
            var m = New().TryMatchHead(Ctx(source));
            Assert.NotNull(m);
        }

        [Fact]
        public void Rejects_V_in_word_no_boundary_right()
        {
            Assert.Null(New().TryMatchHead(Ctx("Vx")));
        }

        [Fact]
        public void Rejects_E_in_word_no_boundary_right()
        {
            Assert.Null(New().TryMatchHead(Ctx("Ex")));
        }

        [Fact]
        public void Rejects_V_after_letter_no_boundary_left()
        {
            Assert.Null(New().TryMatchHead(Ctx("xV")));
        }

        [Fact]
        public void Returns_null_on_empty_source()
        {
            Assert.Null(New().TryMatchHead(Ctx("")));
        }

        // ─── Expand : V seul, V x, V x y (sans Registry = tous = vars) ─

        [Fact]
        public void V_alone_yields_forall_with_var_square_hint()
        {
            var c = ExpandAll("V");
            Assert.Equal(@"\forall", c.PreviewLatex);
            Assert.Equal(@"\forall \square", c.HintLatex);
            Assert.Equal("∀▭", c.Description);
            Assert.True(c.CompletenessScore < 50);
        }

        [Fact]
        public void V_x_yields_forall_x()
        {
            var c = ExpandAll("V x");
            Assert.Equal(@"\forall x", c.PreviewLatex);
            Assert.Equal("∀x", c.Description);
        }

        [Fact]
        public void V_x_y_two_args_space_yields_forall_x_y_when_no_registry()
        {
            // Sans Registry : ClassifyArgs ne peut pas tester si y est un
            // ensemble → tous = vars. "V x y" → ∀x,y.
            var c = ExpandAll("V x y");
            Assert.Equal(@"\forall x,y", c.PreviewLatex);
        }

        [Fact]
        public void V_x_y_z_three_args_space()
        {
            var c = ExpandAll("V x y z");
            Assert.Equal(@"\forall x,y,z", c.PreviewLatex);
        }

        [Fact]
        public void V_csv_inline_x_y_works_as_single_arg()
        {
            // L'user peut aussi taper "V x,y" sans espace → 1 arg "x,y"
            // → décomposé tel quel comme var-list pour le rendu.
            var c = ExpandAll("V x,y");
            Assert.Contains("x,y", c.PreviewLatex);
        }

        // ─── Mutation source ──────────────────────────────────────────

        [Fact]
        public void Mutation_replaces_V_with_forall_in_simple_case()
        {
            var c = ExpandAll("V x");
            Assert.NotNull(c.Mutation);
            Assert.Equal(0, c.Mutation!.Offset);
            Assert.Equal("forall x", c.Mutation.Replacement);
        }

        [Fact]
        public void Mutation_replaces_E_with_exists()
        {
            var c = ExpandAll("E x");
            Assert.Equal("exists x", c.Mutation!.Replacement);
        }

        [Fact]
        public void Mutation_for_V_only_replaces_to_forall()
        {
            var c = ExpandAll("V");
            Assert.Equal("forall", c.Mutation!.Replacement);
            Assert.Equal(0, c.Mutation.Offset);
            Assert.Equal(1, c.Mutation.Length);
        }

        // ─── Multi-completion (mécanisme par poids) ───────────────────
        // Note P5R : avec retrait des openers, plus de multi-completion
        // alias possible. Mais le mécanisme du modèle data-ready est
        // préservé pour P9+ (ex. Σ vs sum vs somme).

        [Fact]
        public void Single_completion_per_source()
        {
            var t = New();
            var ctx = Ctx("V x R");
            var head = t.TryMatchHead(ctx)!;
            var completions = t.Expand(head, ctx);
            Assert.Single(completions);
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
            Assert.Empty(New().Expand(null!, Ctx("V x")));
        }

        [Fact]
        public void Expand_null_ctx_returns_empty()
        {
            var t = New();
            var head = t.TryMatchHead(Ctx("V x"))!;
            Assert.Empty(t.Expand(head, null!));
        }
    }
}
