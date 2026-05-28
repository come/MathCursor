using Xunit;

namespace MathCursor.Engine.Tests.Collision
{
    /// <summary>
    /// P24 (2026-05-22) : collision slurp fraction. Brief v5 §2.4 :
    /// `a/b+c` → 2 candidats : (a/b)+c défaut, a/(b+c) démoté.
    /// </summary>
    public class SlurpFractionTests
    {
        [Fact]
        public void Slurp_one_over_x_plus_one_yields_slurp_collision()
        {
            // P28 : `x` est vec candidate → collisions vec aussi. Le slurp
            // reste présent.
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("1/x+1");
            Assert.Equal(@"\frac{1}{x}+1", r.TopLatex);
            Assert.Contains(r.Collisions, c => c.Latex == @"\frac{1}{x+1}");
        }

        [Fact]
        public void Slurp_a_over_b_minus_c()
        {
            // P28 : `a`, `b`, `c` sont vec candidates → collisions
            // additionnelles vec. Le slurp reste présent.
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("a/b-c");
            Assert.Equal(@"\frac{a}{b}-c", r.TopLatex);
            Assert.Contains(r.Collisions, c => c.Latex == @"\frac{a}{b-c}");
        }

        [Fact]
        public void No_slurp_when_no_fraction()
        {
            // `a+b` n'a pas de `/` → pas de slurp. Mais P28 ajoute vec
            // collisions car a/b sont vec candidates.
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("a+b");
            Assert.DoesNotContain(r.Collisions, c => c.Latex.Contains(@"\frac"));
        }

        [Fact]
        public void No_slurp_when_no_addition_after()
        {
            // `1/x` seul → pas de slurp.
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("1/x");
            Assert.Empty(r.Collisions);
        }
    }
}
