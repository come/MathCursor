using Xunit;
using MathCursor.Core;
using MathCursor.Core.Lattice;

namespace MathCursor.Core.Tests
{
    /// <summary>
    /// Vérifie la cohérence Core ↔ popup-filter pour les rules à 1 match :
    /// <c>match.Start/End</c> doit == <c>resolved.SpotStart/SpotEnd</c>
    /// pour que le popup détecte AppliedAltIdx via le check de bornes.
    /// </summary>
    public class LimAmbigBug2Tests
    {
        [Fact]
        public void Spot_bounds_match_single_match_bounds_for_lim_case()
        {
            var engine = new LatticeEngine();
            var resolver = new ZoneResolver(engine);
            var r = resolver.Resolve("Lim x 0 f(x)= 1/x+1 *4");

            Assert.NotNull(r.Spot);
            Assert.Single(r.AllMatches);
            var m = r.AllMatches[0];

            // C'est l'invariant que le popup utilise pour filtrer l'AppliedAltIdx.
            // Si SpotStart != m.Start (ou idem End), le check
            // `m.Start == spotStart` côté popup échoue → filter actif inactif
            // → alt[0] reste affichée en doublon.
            Assert.Equal(r.SpotStart, m.Start);
            Assert.Equal(r.SpotEnd, m.End);
        }

        [Fact]
        public void Spot_bounds_match_for_two_uppercase_AB_AC_AD()
        {
            var engine = new LatticeEngine();
            var resolver = new ZoneResolver(engine);
            var r = resolver.Resolve("AB+AC=AD");

            // 3 matches : AB, AC, AD. Le Spot est le rightmost.
            Assert.True(r.AllMatches.Count >= 2);
            var rightmost = r.AllMatches[r.AllMatches.Count - 1];
            Assert.Equal(r.SpotStart, rightmost.Start);
            Assert.Equal(r.SpotEnd, rightmost.End);
        }
    }
}
