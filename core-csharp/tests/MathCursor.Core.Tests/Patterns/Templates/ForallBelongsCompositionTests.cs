using Xunit;
using MathCursor.Core.Patterns;
using MathCursor.Core.Patterns.Templates;

namespace MathCursor.Core.Tests.Patterns.Templates
{
    /// <summary>
    /// Tests BOUT-EN-BOUT de la composition <c>forall-belongs</c> +
    /// <c>ensemble</c> + <c>interval-union</c> (post-refacto P5R 2026-05-21 :
    /// convention args espace). Vérifie que le PatternRegistry branché
    /// correctement permet la délégation parent↔enfant complète :
    /// <c>V x R</c> → ∀x ∈ ℝ, <c>V x [0,1]U[3,4]</c> → ∀x ∈ [0,1]∪[3,4]
    /// (= test pilote nommé dans l'ADR cadrage).
    ///
    /// <para>Note P5R : les openers textuels (app a/appartient/dans/in/(-/∈)
    /// ont été retirés. La convention de discrimination var vs domain
    /// passe par la classification du DERNIER arg comme ensemble.</para>
    /// </summary>
    public class ForallBelongsCompositionTests
    {
        private static PatternRegistry FullRegistry()
        {
            return new PatternRegistry(new IPatternTemplate[]
            {
                new ForallBelongsTemplate(),
                new EnsembleTemplate(),
                new IntervalUnionTemplate(),
            });
        }

        private static PatternScanContext Ctx(string source) =>
            new PatternScanContext(
                topAst: null,
                topLatex: source,
                source: source,
                caretOffset: null,
                startPos: 0,
                registry: FullRegistry());

        private static PatternCompletion RunForall(string source)
        {
            var t = new ForallBelongsTemplate();
            var ctx = Ctx(source);
            var head = t.TryMatchHead(ctx)!;
            var completions = t.Expand(head, ctx);
            Assert.NotEmpty(completions);
            return completions[0];
        }

        // ─── ∀x ∈ ℝ (canonical letter, convention args espace) ─────────

        [Fact]
        public void V_x_R_yields_forall_x_in_mathbb_R()
        {
            var c = RunForall("V x R");
            Assert.Equal(@"\forall x \in \mathbb{R}", c.PreviewLatex);
            Assert.Equal("∀x∈ℝ", c.Description);
        }

        [Fact]
        public void V_x_N_yields_forall_x_in_mathbb_N()
        {
            var c = RunForall("V x N");
            Assert.Equal(@"\forall x \in \mathbb{N}", c.PreviewLatex);
            Assert.Equal("∀x∈ℕ", c.Description);
        }

        [Fact]
        public void V_x_Q_yields_forall_x_in_mathbb_Q()
        {
            var c = RunForall("V x Q");
            Assert.Equal(@"\forall x \in \mathbb{Q}", c.PreviewLatex);
        }

        [Fact]
        public void V_x_C_yields_forall_x_in_mathbb_C()
        {
            var c = RunForall("V x C");
            Assert.Equal(@"\forall x \in \mathbb{C}", c.PreviewLatex);
        }

        [Fact]
        public void V_x_R_star_yields_forall_x_in_mathbb_R_sup_star()
        {
            var c = RunForall("V x R*");
            Assert.Equal(@"\forall x \in \mathbb{R}^*", c.PreviewLatex);
            Assert.Equal("∀x∈ℝ*", c.Description);
        }

        // ─── Plusieurs vars + domain ──────────────────────────────────

        [Fact]
        public void V_x_y_R_yields_forall_x_y_in_mathbb_R()
        {
            // 2 vars (x, y) + 1 domain (R reconnu ensemble) → ∀x,y ∈ ℝ
            var c = RunForall("V x y R");
            Assert.Equal(@"\forall x,y \in \mathbb{R}", c.PreviewLatex);
        }

        [Fact]
        public void V_x_y_no_domain_when_no_ensemble_in_last_position()
        {
            // "V x y" : y n'est pas un ensemble identifié → 2 vars, pas de domain.
            var c = RunForall("V x y");
            Assert.Equal(@"\forall x,y", c.PreviewLatex);
            Assert.DoesNotContain(@"\in", c.PreviewLatex);
        }

        [Fact]
        public void V_csv_inline_with_space_domain()
        {
            // "V x,y R" : 1 arg "x,y" (= var-list inline) + 1 domain R
            var c = RunForall("V x,y R");
            Assert.Equal(@"\forall x,y \in \mathbb{R}", c.PreviewLatex);
        }

        // ─── ∀x ∈ [0,1] (interval simple via délégation ensemble→interval) ───

        [Fact]
        public void V_x_closed_interval_via_ensemble_delegation()
        {
            var c = RunForall("V x [0,1]");
            Assert.Equal(
                @"\forall x \in \left[0,1\right]",
                c.PreviewLatex);
        }

        [Fact]
        public void V_x_open_interval_with_paren()
        {
            var c = RunForall("V x (0,1)");
            Assert.Equal(
                @"\forall x \in \left(0,1\right)",
                c.PreviewLatex);
        }

        // ─── ∀x ∈ [0,1]∪[3,4] (TEST PILOTE de l'ADR cadrage P0) ───────

        [Fact]
        public void PILOT_V_x_interval_union_end_to_end()
        {
            // Test bout-en-bout — convention espace pure (= sans `app a`)
            // demande user 2026-05-21 « V vu comme un pattern type limite
            // avec des arguments facultatifs séparés par des espaces ».
            var c = RunForall("V x [0,1]U[3,4]");
            Assert.Equal(
                @"\forall x \in \left[0,1\right] \cup \left[3,4\right]",
                c.PreviewLatex);
            Assert.Equal("∀x∈[0,1]∪[3,4]", c.Description);
        }

        [Fact]
        public void V_x_y_interval_chain()
        {
            // ∀x,y ∈ [0,1]∪[3,4]
            var c = RunForall("V x y [0,1]U[3,4]");
            Assert.Equal(
                @"\forall x,y \in \left[0,1\right] \cup \left[3,4\right]",
                c.PreviewLatex);
        }

        // ─── ∃ (exists) avec composition ──────────────────────────────

        [Fact]
        public void E_x_N_yields_exists_x_in_mathbb_N()
        {
            var c = RunForall("E x N");
            Assert.Equal(@"\exists x \in \mathbb{N}", c.PreviewLatex);
            Assert.Equal("∃x∈ℕ", c.Description);
        }

        [Fact]
        public void E_x_open_interval()
        {
            var c = RunForall("E x (0,1)");
            Assert.Equal(@"\exists x \in \left(0,1\right)", c.PreviewLatex);
        }

        // ─── Mutation composite ────────────────────────────────────────

        [Fact]
        public void Mutation_composes_head_var_and_sub_mutation_for_R()
        {
            // V x R → "forall x in bbR"
            var c = RunForall("V x R");
            Assert.NotNull(c.Mutation);
            Assert.Equal(0, c.Mutation!.Offset);
            Assert.Equal("forall x in bbR", c.Mutation.Replacement);
            Assert.Equal(5, c.Mutation.Length); // "V x R" = 5 chars
        }

        [Fact]
        public void Mutation_composes_for_intervals_no_sub_mutation()
        {
            // V x [0,1] → "forall x in [0,1]"
            var c = RunForall("V x [0,1]");
            Assert.NotNull(c.Mutation);
            Assert.Equal("forall x in [0,1]", c.Mutation!.Replacement);
        }

        [Fact]
        public void Mutation_composes_for_interval_union_chain()
        {
            // V x [0,1]U[3,4] → "forall x in [0,1]U[3,4]"
            var c = RunForall("V x [0,1]U[3,4]");
            Assert.Equal("forall x in [0,1]U[3,4]", c.Mutation!.Replacement);
        }

        // ─── CompletenessScore ─────────────────────────────────────────

        [Fact]
        public void Complete_pattern_has_high_score()
        {
            var c = RunForall("V x R");
            Assert.True(c.CompletenessScore >= 100);
        }

        [Fact]
        public void Partial_pattern_no_var_no_domain_has_low_score()
        {
            var c = RunForall("V");
            Assert.True(c.CompletenessScore <= 50);
        }
    }
}
