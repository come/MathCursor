using System.Linq;
using MathCursor.Engine;
using MathCursor.Engine.Adapter;
using Xunit;

namespace MathCursor.Engine.Adapter.Tests
{
    /// <summary>
    /// Phase 1 collisions (ADR 2026-05-29-Feat-collision-uppercase-seq) :
    /// vérifie headless ce que la popup REÇOIT pour une séquence majuscule —
    /// top + alternatives — via le point exact qui l'alimente
    /// (<see cref="EngineZoneSource"/> → <c>ResolvedZone.PatternCompletions</c>).
    /// </summary>
    public class UppercaseCollisionE2eTests
    {
        private static readonly EngineZoneSource _source =
            new EngineZoneSource(MathEngine.BuildDefault("fr"));

        [Theory]
        [InlineData("AB", "AB", "\\vec{AB}", "(AB)")]
        [InlineData("ABC", "ABC", "\\vec{ABC}", "(ABC)")]
        public void Uppercase_seq_offers_vector_and_paren_collisions(
            string input, string expectedTop, string expectedVec, string expectedParen)
        {
            var zone = _source.TryResolve(input, out _);
            Assert.NotNull(zone);
            Assert.Equal(expectedTop, zone!.TopLatex);

            // Les deux collisions sont présentes (l'ordre n'est pas garanti :
            // il dépend de l'exploration du fork, pas d'une priorité).
            var previews = zone.PatternCompletions.Select(p => p.PreviewLatex).ToList();
            Assert.Equal(2, previews.Count);
            Assert.Contains(expectedVec, previews);
            Assert.Contains(expectedParen, previews);
        }

        [Theory]
        [InlineData("ab")]   // minuscules : produit de scalaires, pas un vecteur
        [InlineData("xy")]
        [InlineData("X")]    // 1 lettre : pas une séquence
        [InlineData("ABCD")] // 4 lettres : hors plage 2-3
        public void Non_uppercase_seq_has_no_collision(string input)
        {
            var zone = _source.TryResolve(input, out _);
            Assert.NotNull(zone);
            Assert.Empty(zone!.PatternCompletions);
        }
    }
}
