using Xunit;

namespace MathCursor.Engine.Tests.Concepts
{
    /// <summary>
    /// Tests P23.3 : probabilités basiques.
    /// `P(A)`, `P(X=k)` rendus directement par le parser structurel.
    /// `P(A|B)` conditional bar : à fixer P24+ (= `|` séparateur spécial).
    /// </summary>
    public class ProbabiliteTests
    {
        [Fact]
        public void P_simple_event()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("P(A)");
            Assert.Equal("P(A)", r.TopLatex);
        }

        [Fact]
        public void P_with_equation()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("P(X=k)");
            Assert.Equal("P(X = k)", r.TopLatex);
        }

        [Fact]
        public void P_with_two_events_intersection()
        {
            // P(A∩B). Le ∩ est dans vocab.Relations setop → \cap.
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("P(A∩B)");
            Assert.Equal(@"P(A \cap B)", r.TopLatex);
        }
    }
}
