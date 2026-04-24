using System.Linq;
using Engine = MathCursor.Core.PatternEngine.PatternEngine;
using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Core.Tests
{
    /// <summary>
    /// Vérifie que sur "lim x->0+ f(x) = ..." on n'a plus les candidats
    /// "\lim_{x \to 0} + f(x) = ..." (le + flottant était le signe
    /// d'un découpage incorrect).
    /// </summary>
    public sealed class LimitOneSidedFilterTest
    {
        private readonly ITestOutputHelper _log;
        public LimitOneSidedFilterTest(ITestOutputHelper log) { _log = log; }

        [Fact]
        public void One_sided_limit_no_floating_plus_in_output()
        {
            var engine = Engine.LoadEmbedded("fr");
            var input = "lim x -> 0+ f(x) = 1/(x+2)^2";
            var suggestions = engine.Convert(input);
            _log.WriteLine($"Input: {input} → {suggestions.Count} candidats");
            foreach (var s in suggestions)
                _log.WriteLine($"  score={s.Score:F1} [{s.PatternId,-30}] {s.Latex}");

            // Doit contenir le bon candidat
            Assert.Contains(suggestions, s => s.Latex.Contains("\\to 0^+") || s.Latex.Contains("\\to 0^{+}"));

            // NE doit PAS contenir "\lim_{x \to 0} + f(x)" (le + flottant)
            Assert.DoesNotContain(suggestions, s =>
                s.Latex.Contains("\\to 0} + f")
                || s.Latex.Contains("\\to 0} +f"));
        }
    }
}
