using System.Collections.Generic;
using MathCursor.Core;
using MathCursor.Core.Lattice;
using MathCursor.Core.Resolution;
using Xunit;

namespace MathCursor.Core.Tests.Resolution
{
    /// <summary>
    /// Tests de l'ordre de précédence post-refactor (cf. brief
    /// 2026-05-07-rule-pin-span-override-refactor étape 4) :
    /// SpanOverride > RulePin > SpanPin legacy > ScoringHints > default.
    /// </summary>
    public class ZoneResolverV2PrecedenceTests
    {
        private static ZoneResolver MakeResolver()
            => new ZoneResolver(LatticeEngine.LoadEmbedded("fr"));

        // ─── SpanOverride ──────────────────────────────────────────────

        [Fact]
        public void SpanOverride_matched_applies_alt()
        {
            // Source "AB" → 1 match two-uppercase, signature (rule,"AB",0,0).
            // SpanOverride alt 1 (paren) doit splice → \left(AB\right).
            var sig = new MatchSignature("two-uppercase", "AB", 0, 0);
            var sidecar = new ResolutionSidecar(null, null, null,
                new[] { new SpanOverride(sig, 1) });

            var resolved = MakeResolver().Resolve("AB", null, sidecar);

            Assert.Contains("AB", resolved.TopLatex);
            // Alt 1 = "\\left(AB\\right)" (cf. AlternativeGenerator)
            Assert.Contains("\\left(", resolved.TopLatex);
        }

        [Fact]
        public void SpanOverride_revert_keeps_default_no_fallback_to_RulePin()
        {
            // Combinaison "tordue" pour vérifier la précédence :
            //  - RulePin two-uppercase:vec actif (vec partout par défaut)
            //  - SpanOverride sur AB = revert (-1) → AB doit rester brut
            // Si la précédence est respectée, on a "AB+\vec{CD}".
            var sigAB = new MatchSignature("two-uppercase", "AB", 0, 0);
            var sidecar = new ResolutionSidecar(null, null,
                new[] { new RulePin("two-uppercase", 0) },
                new[] { new SpanOverride(sigAB, SpanOverride.AltIdxRevert) });

            var resolved = MakeResolver().Resolve("AB+CD", null, sidecar);

            // CD a vec (RulePin), AB est revert (= reste "AB" brut).
            Assert.Contains("\\vec{CD}", resolved.TopLatex);
            Assert.DoesNotContain("\\vec{AB}", resolved.TopLatex);
        }

        // ─── RulePin ──────────────────────────────────────────────────

        [Fact]
        public void RulePin_applies_to_all_matches_of_rule()
        {
            // RulePin two-uppercase:vec → splice tous les two-uppercase.
            var sidecar = new ResolutionSidecar(null, null,
                new[] { new RulePin("two-uppercase", 0) }, null);

            var resolved = MakeResolver().Resolve("AB+CD=AB", null, sidecar);

            Assert.Contains("\\vec{AB}", resolved.TopLatex);
            Assert.Contains("\\vec{CD}", resolved.TopLatex);
        }

        [Fact]
        public void RulePin_does_not_apply_to_other_rules()
        {
            // RulePin canonical-set:1 (alt arbitraire) ne doit pas affecter
            // les two-uppercase.
            var sidecar = new ResolutionSidecar(null, null,
                new[] { new RulePin("canonical-set", 1) }, null);

            var resolved = MakeResolver().Resolve("AB", null, sidecar);

            // AB reste brut (pas de RulePin two-uppercase).
            Assert.DoesNotContain("\\vec", resolved.TopLatex);
        }

        // ─── SpanOverride > RulePin (override local domine global) ────

        [Fact]
        public void SpanOverride_dominates_RulePin_for_matched_span()
        {
            // RulePin two-uppercase:vec (alt 0) actif partout.
            // MAIS SpanOverride sur AB = paren (alt 1) → AB en paren, CD en vec.
            var sigAB = new MatchSignature("two-uppercase", "AB", 0, 0);
            var sidecar = new ResolutionSidecar(null, null,
                new[] { new RulePin("two-uppercase", 0) },     // vec partout
                new[] { new SpanOverride(sigAB, 1) });         // mais AB = paren

            var resolved = MakeResolver().Resolve("AB+CD", null, sidecar);

            Assert.Contains("\\left(", resolved.TopLatex); // AB en paren
            Assert.Contains("\\vec{CD}", resolved.TopLatex); // CD en vec (RulePin)
        }

        // ─── SpanPin legacy (rétro-compat v1) ─────────────────────────

        [Fact]
        public void SpanPin_legacy_still_works()
        {
            // Sidecar v1 avec un SpanPin (non encore converti en SpanOverride).
            // ZoneResolver doit le résoudre via le pin matching span-level.
            var sidecar = new ResolutionSidecar(
                new[] { new SpanPin("two-uppercase", 0, 2, 0) }, // AB → vec
                null, null, null);

            var resolved = MakeResolver().Resolve("AB", null, sidecar);

            Assert.Contains("\\vec{AB}", resolved.TopLatex);
        }

        [Fact]
        public void RulePin_dominates_SpanPin_legacy()
        {
            // RulePin = vec, SpanPin legacy = paren. L'ordre de check
            // est RulePin d'abord → vec gagne.
            var sidecar = new ResolutionSidecar(
                new[] { new SpanPin("two-uppercase", 0, 2, 1) },  // AB → paren legacy
                null,
                new[] { new RulePin("two-uppercase", 0) },        // vec v2
                null);

            var resolved = MakeResolver().Resolve("AB", null, sidecar);

            Assert.Contains("\\vec{AB}", resolved.TopLatex);
            Assert.DoesNotContain("\\left(", resolved.TopLatex);
        }

        // ─── Default (rien dans le sidecar) ───────────────────────────

        [Fact]
        public void Empty_sidecar_keeps_default()
        {
            var resolved = MakeResolver().Resolve("AB", null, ResolutionSidecar.Empty);
            // AB brut (default), pas de splice.
            Assert.Contains("AB", resolved.TopLatex);
            Assert.DoesNotContain("\\vec", resolved.TopLatex);
            Assert.DoesNotContain("\\left(", resolved.TopLatex);
        }
    }
}
