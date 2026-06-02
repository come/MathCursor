using System.Linq;
using MathCursor.Engine;
using Xunit;

namespace MathCursor.Engine.Adapter.Tests
{
    /// <summary>
    /// Collisions de portée (tight-chain) via le fork multi-chaînes du
    /// Principe 5 (ADR 2026-05-30-Feat-beam-search-principe-5). `1/x+1` a deux
    /// ordres de composition valides : `/` d'abord (PEMDAS, top) ou `+` d'abord
    /// (dénominateur gourmand). Les deux émergent du fork — sans flag ni
    /// scanner ad-hoc — et la popup les reçoit comme collisions.
    /// </summary>
    public class TightChainCollisionE2eTests
    {
        private static readonly MathEngine _engine = MathEngine.BuildDefault("fr");

        [Theory]
        [InlineData("1/x+1", @"\frac{1}{x}+1", @"\frac{1}{x+1}")]
        [InlineData("2/n+3", @"\frac{2}{n}+3", @"\frac{2}{n+3}")]
        public void Frac_followed_by_addition_collides_pemdas_vs_greedy(
            string input, string expectedTop, string expectedGreedy)
        {
            var r = _engine.Resolve(input);
            Assert.Equal(expectedTop, r.TopLatex);
            Assert.Contains(r.Collisions, c => c.Latex == expectedGreedy);
        }

        [Theory]
        [InlineData("1/2")]   // pas de chaîne additive → 1 seule lecture
        [InlineData("1/x")]
        [InlineData("frac 1 2")]
        public void Unambiguous_fraction_has_no_collision(string input)
        {
            var r = _engine.Resolve(input);
            Assert.Empty(r.Collisions);
        }
    }
}
