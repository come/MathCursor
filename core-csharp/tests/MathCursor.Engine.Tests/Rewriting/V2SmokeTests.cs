using MathCursor.Engine;
using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Engine.Tests.Rewriting
{
    /// <summary>Smoke tests du moteur V2 (Phase 1). Cas de base.</summary>
    public class V2SmokeTests
    {
        private readonly ITestOutputHelper _out;
        public V2SmokeTests(ITestOutputHelper o) { _out = o; }

        [Theory]
        [InlineData("frac 1 2", "\\frac{1}{2}")]
        [InlineData("1/2", "\\frac{1}{2}")]
        [InlineData("a+b", "a+b")]
        [InlineData("frac n n+1", "\\frac{n}{n+1}")]
        [InlineData("frac (x+1) (x-1)", "\\frac{x+1}{x-1}")]
        [InlineData("sqrt 2", "\\sqrt{2}")]
        [InlineData("sqrt (x+1)", "\\sqrt{x+1}")]
        [InlineData("sum k 1 n k", "\\sum_{k=1}^{n} k")]
        [InlineData("vec u", "\\vec{u}")]
        [InlineData("x2", "x^{2}")]
        public void Resolves(string input, string expected)
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve(input);
            _out.WriteLine($"'{input}' → '{r.TopLatex}'  (rule={r.RuleId})");
            Assert.Equal(expected, r.TopLatex);
        }
    }
}
