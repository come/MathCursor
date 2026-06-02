using System.Linq;
using MathCursor.Engine;
using Xunit;

namespace MathCursor.Engine.Adapter.Tests
{
    /// <summary>
    /// Collisions récursives génériques (ADR 2026-06-02-Feat-recursive-collisions-
    /// variants) : une ambiguïté tight-chain dans le CORPS d'un anchor remonte en
    /// collision, à toute profondeur, via les Variants propagés.
    /// </summary>
    public class RecursiveCollisionE2eTests
    {
        private static readonly MathEngine _engine = MathEngine.BuildDefault("fr");

        [Theory]
        // corps de somme
        [InlineData("somm k=1 2 f(x)/x+1",
            @"\sum_{k=1}^{2} \frac{f(x)}{x}+1", @"\sum_{k=1}^{2} \frac{f(x)}{x+1}")]
        // imbriqué : collision propagée à travers 2 niveaux (somme dans fraction)
        [InlineData("1/sum k 1 n 1/k+1",
            @"\frac{1}{\sum_{k=1}^{n} \frac{1}{k}+1}", @"\frac{1}{\sum_{k=1}^{n} \frac{1}{k+1}}")]
        public void Tight_chain_in_anchor_body_collides(string input, string top, string greedy)
        {
            var r = _engine.Resolve(input);
            Assert.Equal(top, r.TopLatex);
            Assert.Contains(r.Collisions, c => c.Latex == greedy);
        }

        [Theory]
        [InlineData("sum k 1 n k")]   // corps non ambigu → aucune collision
        [InlineData("vec u+1")]
        public void Unambiguous_body_has_no_collision(string input)
        {
            Assert.Empty(_engine.Resolve(input).Collisions);
        }
    }
}
