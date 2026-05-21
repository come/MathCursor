using System.Linq;
using Xunit;
using MathCursor.Core;
using MathCursor.Core.Lattice;
using MathCursor.Core.Patterns;

namespace MathCursor.Core.Tests.Patterns
{
    /// <summary>
    /// Tests d'intégration ZoneResolver + PatternPipeline (P7a, 2026-05-21).
    /// Vérifie que la construction `new ZoneResolver(engine, pipeline, registry)`
    /// active les templates et peuple <c>ResolvedZone.PatternCompletions</c>.
    ///
    /// <para>Le test pilote de l'ADR cadrage P0
    /// (<c>V x [0,1]U[3,4]</c> → ∀x ∈ [0,1]∪[3,4]) est validé ici au
    /// niveau ZoneResolver, complétant les tests
    /// <c>ForallBelongsCompositionTests</c> au niveau Template.</para>
    /// </summary>
    public class ZoneResolverPatternsIntegrationTests
    {
        private static ZoneResolver MakeResolverWithPatterns()
        {
            var (pipeline, registry) = DefaultPatternRegistry.BuildBoth();
            return new ZoneResolver(new LatticeEngine(), pipeline, registry);
        }

        private static ZoneResolver MakeResolverWithoutPatterns()
            => new ZoneResolver(new LatticeEngine());

        // ─── Rétro-compat : resolver sans patterns ─────────────────────

        [Fact]
        public void Resolver_without_pipeline_yields_empty_PatternCompletions()
        {
            var r = MakeResolverWithoutPatterns().Resolve("V x R");
            Assert.NotNull(r.PatternCompletions);
            Assert.Empty(r.PatternCompletions);
        }

        [Fact]
        public void Resolver_without_pipeline_does_not_affect_AllMatches()
        {
            // Sans patterns, AllMatches reste celui du pipeline ambig closed
            // (= comportement P1-P6). Source "AB" doit toujours produire un
            // match RuleTwoUppercase.
            var r = MakeResolverWithoutPatterns().Resolve("AB");
            Assert.NotEmpty(r.AllMatches);
        }

        // ─── Resolver AVEC patterns : V/E/R/N/Z/Q/C couverts ──────────

        [Fact]
        public void V_x_app_a_R_yields_pattern_completion_for_forall_belongs()
        {
            var r = MakeResolverWithPatterns().Resolve("V x R");
            Assert.NotEmpty(r.PatternCompletions);
            var first = r.PatternCompletions[0];
            Assert.Equal(@"\forall x \in \mathbb{R}", first.PreviewLatex);
            Assert.Equal("∀x∈ℝ", first.Description);
        }

        [Fact]
        public void E_x_app_a_N_yields_exists_pattern_completion()
        {
            var r = MakeResolverWithPatterns().Resolve("E x N");
            Assert.NotEmpty(r.PatternCompletions);
            var first = r.PatternCompletions[0];
            Assert.Equal(@"\exists x \in \mathbb{N}", first.PreviewLatex);
        }

        [Fact]
        public void V_alone_yields_forall_completion_with_square_hint()
        {
            var r = MakeResolverWithPatterns().Resolve("V");
            Assert.NotEmpty(r.PatternCompletions);
            var first = r.PatternCompletions[0];
            Assert.Equal(@"\forall", first.PreviewLatex);
            Assert.Contains(@"\square", first.HintLatex);
        }

        [Fact]
        public void R_alone_yields_ensemble_completion_for_mathbb_R()
        {
            var r = MakeResolverWithPatterns().Resolve("R");
            Assert.NotEmpty(r.PatternCompletions);
            Assert.Contains(r.PatternCompletions,
                c => c.PreviewLatex == @"\mathbb{R}");
        }

        // ─── TEST PILOTE de l'ADR cadrage P0, niveau ZoneResolver ──────

        [Fact]
        public void PILOT_V_x_app_a_interval_union_via_zone_resolver()
        {
            // Test pilote bout-en-bout au niveau ZoneResolver. Complète
            // ForallBelongsCompositionTests.PILOT_V_x_app_a_interval_union_end_to_end
            // qui le valide au niveau Template direct.
            var r = MakeResolverWithPatterns().Resolve("V x [0,1]U[3,4]");
            Assert.NotEmpty(r.PatternCompletions);
            var forall = r.PatternCompletions.First(c =>
                c.PreviewLatex.StartsWith(@"\forall"));
            Assert.Equal(
                @"\forall x \in \left[0,1\right] \cup \left[3,4\right]",
                forall.PreviewLatex);
            Assert.Equal("∀x∈[0,1]∪[3,4]", forall.Description);
        }

        // ─── Cas sans matchPattern ────────────────────────────────────

        [Fact]
        public void Random_source_no_pattern_match_yields_empty_PatternCompletions()
        {
            // "AB+CD" ne contient ni V/E ni R/N/Z/Q/C ni interval. Aucun
            // template ne matche → PatternCompletions vide. Les ambig closed
            // (RuleTwoUppercase sur AB/CD) restent dans AllMatches.
            var r = MakeResolverWithPatterns().Resolve("AB+CD");
            Assert.Empty(r.PatternCompletions);
            Assert.NotEmpty(r.AllMatches); // ambig closed restent
        }

        [Fact]
        public void Empty_source_yields_empty_both()
        {
            var r = MakeResolverWithPatterns().Resolve("");
            Assert.Empty(r.PatternCompletions);
            Assert.Empty(r.AllMatches);
        }

        // ─── Coexistence Patterns + AmbigMatch (Choix 5 du plan P7) ────

        [Fact]
        public void Patterns_and_AmbigMatch_flows_are_independent()
        {
            // Vérifie que les 2 flux sont peuplés indépendamment :
            // - "V x R" : pattern actif, ambig closed N/A
            // - "AB" : pattern N/A, ambig closed actif
            var resolver = MakeResolverWithPatterns();
            var rPattern = resolver.Resolve("V x R");
            var rAmbig = resolver.Resolve("AB");

            Assert.NotEmpty(rPattern.PatternCompletions);
            Assert.NotEmpty(rAmbig.AllMatches);
        }
    }
}
