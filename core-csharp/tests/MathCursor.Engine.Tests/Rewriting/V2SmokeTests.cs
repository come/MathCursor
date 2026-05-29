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
        // ── Phase 2 : composition récursive profonde ──
        [InlineData("1/sum k 0 n f(k)", "\\frac{1}{\\sum_{k=0}^{n} f(k)}")]
        [InlineData("sum k=1 n k", "\\sum_{k=1}^{n} k")]
        [InlineData("lim x 0 f + lim x 1 g", "\\lim_{x \\to 0} f+\\lim_{x \\to 1} g")]
        [InlineData("frac (sum k 0 n k) 2", "\\frac{\\sum_{k=0}^{n} k}{2}")]
        [InlineData("vec u + vec v", "\\vec{u}+\\vec{v}")]
        // ── Phase 6 : anchor 3-formes ──
        [InlineData("frac(1 2)", "\\frac{1}{2}")]
        [InlineData("frac(1, 2)", "\\frac{1}{2}")]
        [InlineData("sum(k, 1, n, k)", "\\sum_{k=1}^{n} k")]
        [InlineData("sqrt(2)", "\\sqrt{2}")]
        // ── Phase 7 : prefix-match + alias ──
        [InlineData("somme k 1 n k", "\\sum_{k=1}^{n} k")]
        [InlineData("som k 1 n k", "\\sum_{k=1}^{n} k")]
        [InlineData("limite x 0 f(x)", "\\lim_{x \\to 0} f(x)")]
        public void Resolves(string input, string expected)
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve(input);
            _out.WriteLine($"'{input}' → '{r.TopLatex}'  (rule={r.RuleId})");
            Assert.Equal(expected, r.TopLatex);
        }
    }
}
