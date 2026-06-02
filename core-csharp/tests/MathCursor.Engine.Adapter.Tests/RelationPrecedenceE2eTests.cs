using System.Linq;
using MathCursor.Engine;
using Xunit;

namespace MathCursor.Engine.Adapter.Tests
{
    /// <summary>
    /// Précédence des relations (ADR 2026-06-02-Fix-relation-precedence) : `=`
    /// est le plus lâche et n'est jamais absorbé par une fraction. `f(x) = 1/x+1`
    /// donne `f(x) = …`, pas `\frac{f(x)=1}{…}`.
    /// </summary>
    public class RelationPrecedenceE2eTests
    {
        private static readonly MathEngine _engine = MathEngine.BuildDefault("fr");

        [Fact]
        public void Equation_with_fraction_rhs_keeps_equals_loosest()
        {
            var r = _engine.Resolve("f(x) = 1/x+1");
            Assert.Equal(@"f(x)=\frac{1}{x}+1", r.TopLatex);
            Assert.Contains(r.Collisions, c => c.Latex == @"f(x)=\frac{1}{x+1}");
            // AUCUNE lecture parasite où le = est dans un numérateur.
            Assert.DoesNotContain(r.Collisions, c => c.Latex.Contains(@"\frac{f(x)=1}"));
        }

        [Theory]
        [InlineData("forall n N n>0", @"\forall n \in \mathbb{N}, n>0")] // corps relation
        [InlineData("forall x R P(x)", @"\forall x \in \mathbb{R}, P(x)")] // corps expr
        [InlineData("a <=> b", @"a \iff b")]
        [InlineData("x in R", @"x \in \mathbb{R}")]
        public void Relations_render_correctly(string input, string expected)
        {
            Assert.Equal(expected, _engine.Resolve(input).TopLatex);
        }
    }
}
