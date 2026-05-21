using Xunit;
using MathCursor.Core.Lattice.Ast;
using MathCursor.Core.Patterns;
using MathCursor.Core.Patterns.Templates;

namespace MathCursor.Core.Tests.Patterns.Templates
{
    /// <summary>
    /// Tests BOUT-EN-BOUT de la composition <c>forall-belongs</c> +
    /// <c>ensemble</c> + <c>interval-union</c>. Vérifie que le PatternRegistry
    /// branché correctement permet la délégation parent↔enfant complète :
    /// <c>V x app a R</c> → ∀x ∈ ℝ, <c>V x app a [0,1]U[3,4]</c> → ∀x ∈ [0,1]∪[3,4]
    /// (= test pilote nommé dans l'ADR cadrage). Étape P5.6 (ADR
    /// <c>2026-05-21-Feat-forall-belongs-pattern</c>).
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

        // ─── ∀x ∈ ℝ (canonical letter) ─────────────────────────────────

        [Fact]
        public void V_x_app_a_R_yields_forall_x_in_mathbb_R()
        {
            var c = RunForall("V x app a R");
            Assert.Equal(@"\forall x \in \mathbb{R}", c.PreviewLatex);
            Assert.Equal("∀x∈ℝ", c.Description);
        }

        [Fact]
        public void V_x_appartient_N_yields_forall_x_in_mathbb_N()
        {
            var c = RunForall("V x appartient N");
            Assert.Equal(@"\forall x \in \mathbb{N}", c.PreviewLatex);
            Assert.Equal("∀x∈ℕ", c.Description);
        }

        [Fact]
        public void V_x_dans_Q_yields_forall_x_in_mathbb_Q()
        {
            var c = RunForall("V x dans Q");
            Assert.Equal(@"\forall x \in \mathbb{Q}", c.PreviewLatex);
        }

        [Fact]
        public void V_x_in_C_yields_forall_x_in_mathbb_C()
        {
            var c = RunForall("V x in C");
            Assert.Equal(@"\forall x \in \mathbb{C}", c.PreviewLatex);
        }

        [Fact]
        public void V_x_belongs_R_star_yields_forall_x_in_mathbb_R_sup_star()
        {
            var c = RunForall("V x app a R*");
            Assert.Equal(@"\forall x \in \mathbb{R}^*", c.PreviewLatex);
            Assert.Equal("∀x∈ℝ*", c.Description);
        }

        // ─── ∀x ∈ [0,1] (interval simple via délégation ensemble→interval) ───

        [Fact]
        public void V_x_app_a_closed_interval_via_ensemble_delegation()
        {
            var c = RunForall("V x app a [0,1]");
            Assert.Equal(
                @"\forall x \in \left[0,1\right]",
                c.PreviewLatex);
        }

        [Fact]
        public void V_x_app_a_open_interval_with_paren()
        {
            var c = RunForall("V x app a (0,1)");
            Assert.Equal(
                @"\forall x \in \left(0,1\right)",
                c.PreviewLatex);
        }

        // ─── ∀x ∈ [0,1]∪[3,4] (TEST PILOTE de l'ADR cadrage P0) ───────

        [Fact]
        public void PILOT_V_x_app_a_interval_union_end_to_end()
        {
            // Test bout-en-bout nommé dans l'ADR cadrage 2026-05-21 :
            // « V x app a [0,1]U[3,4] doit produire la forme idiomatique
            // complète ∀x ∈ [0,1]∪[3,4] »
            var c = RunForall("V x app a [0,1]U[3,4]");
            Assert.Equal(
                @"\forall x \in \left[0,1\right] \cup \left[3,4\right]",
                c.PreviewLatex);
            Assert.Equal("∀x∈[0,1]∪[3,4]", c.Description);
        }

        [Fact]
        public void V_x_y_app_a_interval_chain()
        {
            // ∀x,y ∈ [0,1]∪[3,4]
            var c = RunForall("V x,y app a [0,1]U[3,4]");
            Assert.Equal(
                @"\forall x,y \in \left[0,1\right] \cup \left[3,4\right]",
                c.PreviewLatex);
        }

        // ─── ∃ (exists) avec composition ──────────────────────────────

        [Fact]
        public void E_x_app_a_N_yields_exists_x_in_mathbb_N()
        {
            var c = RunForall("E x app a N");
            Assert.Equal(@"\exists x \in \mathbb{N}", c.PreviewLatex);
            Assert.Equal("∃x∈ℕ", c.Description);
        }

        [Fact]
        public void E_x_app_a_interval_open()
        {
            var c = RunForall("E x app a (0,1)");
            Assert.Equal(@"\exists x \in \left(0,1\right)", c.PreviewLatex);
        }

        // ─── Mutation composite ────────────────────────────────────────

        [Fact]
        public void Mutation_composes_head_var_opener_and_sub_mutation_for_R()
        {
            // V x app a R → "forall x in bbR"
            // V → forall, " x " conservé, "app a" → "in", R → bbR (sub-mut
            // de EnsembleTemplate)
            var c = RunForall("V x app a R");
            Assert.NotNull(c.Mutation);
            Assert.Equal(0, c.Mutation!.Offset);
            Assert.Equal("forall x in bbR", c.Mutation.Replacement);
            Assert.Equal(11, c.Mutation.Length); // "V x app a R" = 11 chars
        }

        [Fact]
        public void Mutation_composes_for_intervals_no_sub_mutation()
        {
            // V x app a [0,1] → "forall x in [0,1]"
            // (interval-union ne produit pas de SourceMutation, donc le
            // domain est reproduit tel quel depuis la source)
            var c = RunForall("V x app a [0,1]");
            Assert.NotNull(c.Mutation);
            Assert.Equal("forall x in [0,1]", c.Mutation!.Replacement);
        }

        [Fact]
        public void Mutation_composes_for_interval_union_chain()
        {
            // V x app a [0,1]U[3,4] → "forall x in [0,1]U[3,4]"
            var c = RunForall("V x app a [0,1]U[3,4]");
            Assert.Equal("forall x in [0,1]U[3,4]", c.Mutation!.Replacement);
        }

        // ─── CompletenessScore ─────────────────────────────────────────

        [Fact]
        public void Complete_pattern_has_high_score()
        {
            var c = RunForall("V x app a R");
            Assert.True(c.CompletenessScore > 80);
        }

        [Fact]
        public void Partial_pattern_no_var_no_opener_has_low_score()
        {
            var c = RunForall("V");
            Assert.True(c.CompletenessScore <= 50);
        }
    }
}
