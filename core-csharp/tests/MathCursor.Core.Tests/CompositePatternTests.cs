using System.Linq;
using MathCursor.Core.PatternEngine;
using Engine = MathCursor.Core.PatternEngine.PatternEngine;
using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Core.Tests
{
    /// <summary>
    /// Combinaisons inter-patterns : sum+sqrt, int+fraction, lim+sin/x, etc.
    /// Chaque pattern est testé isolément via gold examples. Ici on vérifie que
    /// les patterns COMPOSÉS produisent un rendu sensé (sans tout perdre dans un
    /// match partiel maladroit).
    /// </summary>
    public sealed class CompositePatternTests
    {
        private readonly ITestOutputHelper _log;
        public CompositePatternTests(ITestOutputHelper log) { _log = log; }

        [Theory]
        [InlineData("Sum k=1 to n sqrt(k)", "\\sum_{k=1}^{n}", "\\sqrt{k}")]
        [InlineData("int 0 to 1 x/sqrt(1+x^2) dx", "\\int_{0}^{1}", "\\sqrt{1+x^2}")]
        [InlineData("lim x->0 sin(x)/x", "\\lim_{x \\to 0}", "\\sin(x)")]
        [InlineData("lim n->+inf 1/sqrt(n)", "\\lim_{n \\to +\\infty}", "\\sqrt{n}")]
        [InlineData("(2x+1)/(sqrt(x)+1)", "\\frac", "\\sqrt{x}")]
        // Note : les énoncés composés séparés par ':' ('V x ( R : f(x) = x^2')
        // ne sont pas encore supportés (1 seul pattern par span pour l'instant).
        public void Composite_contains_expected_substructures(string input, string part1, string part2)
        {
            var engine = Engine.LoadEmbedded("fr");
            var suggestions = engine.Convert(input);
            _log.WriteLine($"Input: \"{input}\"");
            foreach (var s in suggestions.Take(3))
                _log.WriteLine($"  {s.Score,6:F1}  {s.Latex}");

            Assert.NotEmpty(suggestions);
            string norm(string s) => s.Replace(" ", "").Replace("\t", "");
            string n1 = norm(part1), n2 = norm(part2);
            Assert.Contains(suggestions, s => norm(s.Latex).Contains(n1) && norm(s.Latex).Contains(n2));
        }
    }
}
