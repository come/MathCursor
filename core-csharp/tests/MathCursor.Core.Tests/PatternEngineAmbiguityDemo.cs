using System.Linq;
using Engine = MathCursor.Core.PatternEngine.PatternEngine;
using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Core.Tests
{
    /// <summary>
    /// Démontre qu'on renvoie toujours une liste classée, pas un seul choix.
    /// </summary>
    public sealed class PatternEngineAmbiguityDemo
    {
        private readonly ITestOutputHelper _log;
        public PatternEngineAmbiguityDemo(ITestOutputHelper log) { _log = log; }

        [Theory]
        [InlineData("P(A)")]
        [InlineData("cos(x)")]
        [InlineData("f(x)")]
        [InlineData("x + y = 5")]
        [InlineData("AB+BC=AC")]
        [InlineData("AB")]
        [InlineData("AB.BC")]
        [InlineData("AB^BC")]
        [InlineData("u(x,y)")]
        [InlineData("u(x y)")]
        [InlineData("AB(x y)")]
        [InlineData("1/V((x+1)/(x+2)^2)")]
        [InlineData("1/V((x+1)/(x+2)^2)+1")]
        [InlineData("x   +   y   =   5")]
        [InlineData("u( x ,  y )")]
        [InlineData("AB(u v) + AC(d x)")]
        [InlineData("alpha + beta = gamma")]
        [InlineData("V x ( R")]
        [InlineData("x ( R")]
        [InlineData("p <=> q")]
        [InlineData("A_ABCD")]
        [InlineData("Cf")]
        [InlineData("arccos(x)")]
        [InlineData("asin(x)")]
        [InlineData("R*")]
        [InlineData("R+")]
        [InlineData("R-")]
        [InlineData("R*+")]
        [InlineData("R-{0}")]
        [InlineData("V x ( R*")]
        [InlineData("x ( R+")]
        [InlineData("V x,y ( R*")]
        [InlineData("Sum k=1 k<10 f(k)+1")]
        [InlineData("sum k=1 a n f(k)")]
        [InlineData("prod(k,1,n) k")]
        [InlineData("Lim(x,0) f(x)")]
        [InlineData("1/V((x+1)/(x+2)^2) = 12")]
        [InlineData("Vx([0;1]")]
        [InlineData("V x ( [0;1]")]
        [InlineData("x ( [-1;1[")]
        [InlineData("Vx,y(R")]
        [InlineData("x,y ( R")]
        [InlineData("12/v(t+d+5)")]
        [InlineData("1/V((x+1)/(x+2)^2) + alpha = 12/v(t+d+5)")]
        [InlineData("V(x+1)")]
        [InlineData("Vx(R")]
        [InlineData("lim x->1 f(x) = 1")]
        [InlineData("O_n = O_n-1")]
        [InlineData("u_n+1 = 2*u_n + 3")]
        public void Shows_ranked_candidates(string input)
        {
            var engine = Engine.LoadEmbedded("fr");
            var suggestions = engine.Convert(input);
            _log.WriteLine($"Input: \"{input}\" → {suggestions.Count} candidats");
            foreach (var s in suggestions)
                _log.WriteLine($"  {s.Score,6:F1}  [{s.PatternId,-25}]  {s.Latex}");
            Assert.NotEmpty(suggestions);
        }

        [Theory]
        [InlineData("x = 1/2 + 3", "\\frac{1}{2}")]
        [InlineData("p/q", "\\frac{p}{q}")]
        [InlineData("(x+1)/(x-1)", "\\frac{x+1}{x-1}")]
        [InlineData("f(x)/g(x)", "\\frac{f(x)}{g(x)}")]
        [InlineData("F(x)=1/sqrt(x+205)^2", "\\sqrt{x+205}^2")]
        [InlineData("sqrt(x)^2", "\\sqrt{x}^2")]
        [InlineData("1/sqrt(x)", "\\frac{1}{\\sqrt{x}}")]
        public void Always_renders_division_as_frac(string input, string shouldContain)
        {
            var engine = Engine.LoadEmbedded("fr");
            var suggestions = engine.Convert(input);
            _log.WriteLine($"Input: \"{input}\"");
            foreach (var s in suggestions.Take(3))
                _log.WriteLine($"  {s.Score,6:F1}  [{s.PatternId,-25}]  {s.Latex}");
            string norm(string s) => s.Replace(" ", "").Replace("\t", "");
            string needle = norm(shouldContain);
            Assert.Contains(suggestions, s => norm(s.Latex).Contains(needle));
        }
    }
}
