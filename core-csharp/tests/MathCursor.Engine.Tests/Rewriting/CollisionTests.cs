using System.Linq;
using MathCursor.Engine;
using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Engine.Tests.Rewriting
{
    /// <summary>
    /// Phase 4 (brique collision) : quand ≥2 règles matchent la même région
    /// différemment, la meilleure devient <see cref="EngineResult.TopLatex"/>
    /// et les autres remontent en <see cref="EngineResult.Collisions"/>
    /// (= candidats popup). Démontré avec exposant vs indice sur `x2`.
    /// </summary>
    public class CollisionTests
    {
        private readonly ITestOutputHelper _out;
        public CollisionTests(ITestOutputHelper o) { _out = o; }

        [Fact]
        public void X2_top_is_superscript_alt_is_subscript()
        {
            var r = MathEngine.BuildDefault("fr").Resolve("x2");
            _out.WriteLine($"top={r.TopLatex} ; alts={string.Join(", ", r.Collisions.Select(c => c.Latex))}");

            Assert.Equal("x^{2}", r.TopLatex);
            Assert.Contains(r.Collisions, c => c.Latex == "x_{2}");
        }

        [Fact]
        public void No_collision_when_unambiguous()
        {
            var r = MathEngine.BuildDefault("fr").Resolve("frac 1 2");
            Assert.Equal("\\frac{1}{2}", r.TopLatex);
            Assert.Empty(r.Collisions);
        }
    }
}
