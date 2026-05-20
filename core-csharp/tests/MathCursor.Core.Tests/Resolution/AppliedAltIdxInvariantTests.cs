using System.Linq;
using MathCursor.Core;
using MathCursor.Core.Lattice;
using MathCursor.Core.Resolution;
using Xunit;

namespace MathCursor.Core.Tests.Resolution
{
    /// <summary>
    /// Tests régression pour l'invariant 2026-05-07 :
    /// <c>AmbiguityMatch.AppliedAltIdx</c> doit toujours pointer sur l'alt
    /// que <see cref="ZoneResolver"/> a effectivement appliquée dans le
    /// <see cref="ResolvedZone.TopLatex"/>. La popup utilise ça pour
    /// filtrer l'alt active de la liste affichée → garantie « la finale
    /// n'apparaît jamais en désambig » par construction.
    /// </summary>
    public class AppliedAltIdxInvariantTests
    {
        private static ZoneResolver MakeResolver()
            => new ZoneResolver(LatticeEngine.LoadEmbedded("fr"));

        // ─── Cas vierge : aucune alt appliquée ────────────────────────

        [Fact]
        public void Empty_sidecar_no_alt_applied()
        {
            var resolved = MakeResolver().Resolve("AB");

            Assert.Single(resolved.AllMatches);
            var m = resolved.AllMatches[0];
            Assert.Equal("two-uppercase", m.Spot.RuleId);
            // Cas vierge : default reste, AppliedAltIdx = -1.
            Assert.Equal(-1, m.AppliedAltIdx);
            Assert.Equal("AB", resolved.TopLatex);
            Assert.Equal("AB", resolved.BaseTopLatex);
        }

        // ─── RulePin actif → AppliedAltIdx pointe sur cette alt ─────────

        [Fact]
        public void RulePin_vec_active_AppliedAltIdx_is_zero()
        {
            var sidecar = new ResolutionSidecar(null, null,
                new[] { new RulePin("two-uppercase", 0) }, null);

            var resolved = MakeResolver().Resolve("AB", null, sidecar);

            var m = resolved.AllMatches[0];
            Assert.Equal(0, m.AppliedAltIdx);  // alt 0 = vec
            Assert.Equal("\\vec{AB}", resolved.TopLatex);
            Assert.Equal("AB", resolved.BaseTopLatex);
        }

        [Fact]
        public void RulePin_paren_active_AppliedAltIdx_is_one()
        {
            var sidecar = new ResolutionSidecar(null, null,
                new[] { new RulePin("two-uppercase", 1) }, null);

            var resolved = MakeResolver().Resolve("AB", null, sidecar);

            var m = resolved.AllMatches[0];
            Assert.Equal(1, m.AppliedAltIdx);  // alt 1 = paren
            Assert.Contains("\\left(AB", resolved.TopLatex);
        }

        // ─── Multiple matches : chacun a son propre AppliedAltIdx ─────

        [Fact]
        public void Multiple_two_uppercase_each_has_AppliedAltIdx()
        {
            var sidecar = new ResolutionSidecar(null, null,
                new[] { new RulePin("two-uppercase", 0) }, null);

            var resolved = MakeResolver().Resolve("AB+CD", null, sidecar);

            Assert.Equal(2, resolved.AllMatches.Count);
            foreach (var m in resolved.AllMatches)
            {
                Assert.Equal(0, m.AppliedAltIdx);  // tous splicés en vec
            }
            Assert.Contains("\\vec{AB}", resolved.TopLatex);
            Assert.Contains("\\vec{CD}", resolved.TopLatex);
        }

        // ─── SpanOverride domine RulePin ──────────────────────────────

        [Fact]
        public void SpanOverride_dominates_RulePin_in_AppliedAltIdx()
        {
            // RulePin vec (alt 0) actif, mais SpanOverride paren (alt 1) sur AB.
            var resolved0 = MakeResolver().Resolve("AB+CD"); // pour récupérer la signature de AB
            var sigAB = resolved0.AllMatches.First(m => m.Spot.DefaultLatex == "AB").Signature!;

            var sidecar = new ResolutionSidecar(null, null,
                new[] { new RulePin("two-uppercase", 0) },
                new[] { new SpanOverride(sigAB, 1) });

            var resolved = MakeResolver().Resolve("AB+CD", null, sidecar);

            var ab = resolved.AllMatches.First(m => m.Spot.DefaultLatex == "AB");
            var cd = resolved.AllMatches.First(m => m.Spot.DefaultLatex == "CD");
            Assert.Equal(1, ab.AppliedAltIdx);  // override paren
            Assert.Equal(0, cd.AppliedAltIdx);  // RulePin vec
        }

        // ─── SpanOverride revert → AppliedAltIdx = -1 (default reste) ─

        [Fact]
        public void SpanOverride_revert_AppliedAltIdx_minus_one()
        {
            var resolved0 = MakeResolver().Resolve("AB");
            var sig = resolved0.AllMatches[0].Signature!;

            var sidecar = new ResolutionSidecar(null, null,
                new[] { new RulePin("two-uppercase", 0) },     // vec actif
                new[] { new SpanOverride(sig, SpanOverride.AltIdxRevert) }); // mais AB revert

            var resolved = MakeResolver().Resolve("AB", null, sidecar);

            var m = resolved.AllMatches[0];
            // Revert explicit → pas de splice → AppliedAltIdx = -1.
            Assert.Equal(-1, m.AppliedAltIdx);
            Assert.Equal("AB", resolved.TopLatex);
        }

        // ─── Cas user 2026-05-07 : letter-sup-number ──────────────────

        [Fact]
        public void Y2_with_RulePin_indice_AppliedAltIdx_is_zero()
        {
            // Cas exact du screenshot user : Y2 avec RulePin
            // letter-sup-number:0 (= indice).
            var sidecar = new ResolutionSidecar(null, null,
                new[] { new RulePin("letter-sup-number", 0) }, null);

            var resolved = MakeResolver().Resolve("Y2", null, sidecar);

            var m = resolved.AllMatches.First(x => x.Spot.RuleId == "letter-sup-number");
            Assert.Equal(0, m.AppliedAltIdx);     // alt 0 = indice
            Assert.Equal("Y_{2}", resolved.TopLatex);   // splicé
            Assert.Equal("Y^{2}", resolved.BaseTopLatex); // default exposant
        }

        // ─── AppliedAltIdx vs default : invariant ──────────────────────

        [Fact]
        public void AppliedAltIdx_minus_one_when_no_splice_applied()
        {
            // Pas de RulePin, pas de SpanOverride, pas de hints contextuels.
            var resolved = MakeResolver().Resolve("AB");

            // Tous les matches ont AppliedAltIdx = -1.
            foreach (var m in resolved.AllMatches)
                Assert.Equal(-1, m.AppliedAltIdx);
        }

        [Fact]
        public void AppliedAltIdx_in_range_when_set()
        {
            var sidecar = new ResolutionSidecar(null, null,
                new[] { new RulePin("two-uppercase", 2) }, null);  // crochet

            var resolved = MakeResolver().Resolve("AB", null, sidecar);

            var m = resolved.AllMatches[0];
            Assert.True(m.AppliedAltIdx >= 0);
            Assert.True(m.AppliedAltIdx < m.Spot.Alternatives.Count);
            Assert.Equal(2, m.AppliedAltIdx);  // crochet
        }

        // ─── Cohérence TopLatex ↔ AppliedAltIdx ───────────────────────

        [Theory]
        [InlineData(0, "\\vec{AB}")]
        [InlineData(1, "\\left(AB\\right)")]
        [InlineData(2, "\\left[AB\\right]")]
        public void TopLatex_matches_AppliedAltIdx(int altIdx, string expectedFragment)
        {
            var sidecar = new ResolutionSidecar(null, null,
                new[] { new RulePin("two-uppercase", altIdx) }, null);

            var resolved = MakeResolver().Resolve("AB", null, sidecar);

            var m = resolved.AllMatches[0];
            Assert.Equal(altIdx, m.AppliedAltIdx);
            Assert.Contains(expectedFragment, resolved.TopLatex);
            // L'alt à AppliedAltIdx, lue depuis Spot.Alternatives, doit
            // matcher ce qui est dans TopLatex.
            string altLatex = m.Spot.Alternatives[m.AppliedAltIdx].Latex;
            Assert.Contains(altLatex, resolved.TopLatex);
        }
    }
}
