using Xunit;
using MathCursor.Core;
using MathCursor.Core.Lattice;

namespace MathCursor.Core.Tests
{
    /// <summary>
    /// Comportement de l'ambig <c>tight-chain-extension</c> sur l'expression
    /// "Lim f(x) 0 g(x) = 1/x+1 + 2" : la fraction <c>1/x+1</c> est
    /// ambiguë (= <c>(1/x)+1</c> vs <c>1/(x+1)</c>), donc 2 alts proposées
    /// avec topLatex = alt[0] par défaut.
    ///
    /// <para>Test ajouté 2026-05-21 pour documenter le comportement Core.
    /// Le visuel popup tronqué constaté côté UI est un problème de layout
    /// WPF (overflow), pas du Core.</para>
    /// </summary>
    public class LimAmbigBugTests
    {
        private const string Source = "Lim f(x) 0 g(x) = 1/x+1 + 2";

        [Fact]
        public void Engine_returns_tight_chain_extension_with_2_alts()
        {
            var engine = new LatticeEngine();
            var r = engine.ConvertWithAmbiguity(Source);

            Assert.NotNull(r.Spot);
            Assert.Equal("tight-chain-extension", r.Spot.RuleId);
            Assert.Equal(2, r.Spot.Alternatives.Count);
        }

        [Fact]
        public void Default_alt_is_left_associative_fraction()
        {
            var engine = new LatticeEngine();
            var r = engine.ConvertWithAmbiguity(Source);

            // alt[0] = (1/x) + 1 + 2 (= la fraction NE consomme PAS +1)
            Assert.Equal(
                @"\lim_{f\left(x\right) \to 0} g\left(x\right)=\frac{1}{x}+1+2",
                r.Spot.Alternatives[0].Latex);
            Assert.Equal(r.Spot.Alternatives[0].Latex, r.TopLatex);
        }

        [Fact]
        public void Second_alt_extends_denominator_to_x_plus_1()
        {
            var engine = new LatticeEngine();
            var r = engine.ConvertWithAmbiguity(Source);

            // alt[1] = 1/(x+1) + 2 (= la fraction consomme +1)
            Assert.Equal(
                @"\lim_{f\left(x\right) \to 0} g\left(x\right)=\frac{1}{x+1}+2",
                r.Spot.Alternatives[1].Latex);
        }

        [Fact]
        public void Resolver_no_pref_leaves_AppliedAltIdx_at_minus_1()
        {
            // Sans pref, AppliedAltIdx reste -1 (pas de choix user).
            // Le filtrage de l'alt qui matche DefaultLatex est fait CÔTÉ
            // FILTER (PopupAltFilter), pas via AppliedAltIdx → pas de revert
            // dans ce cas (rien à revert).
            var engine = new LatticeEngine();
            var resolver = new ZoneResolver(engine);
            var r = resolver.Resolve(Source);

            Assert.NotNull(r.AllMatches);
            Assert.Single(r.AllMatches);
            Assert.Equal(-1, r.AllMatches[0].AppliedAltIdx);
        }

        [Fact]
        public void Resolver_user_pref_sets_AppliedAltIdx()
        {
            // Avec pref, AppliedAltIdx = altIdx pref → popup filtre cet alt.
            var engine = new LatticeEngine();
            var resolver = new ZoneResolver(engine);
            resolver.AddPreference("tight-chain-extension", 1);
            var r = resolver.Resolve(Source);

            var m = r.AllMatches[0];
            Assert.Equal(1, m.AppliedAltIdx);
        }
    }
}
