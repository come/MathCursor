using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Engine.Tests.Concepts
{
    /// <summary>
    /// Probe bug 2026-05-23 user-reported (3× reports, 1× comment user
    /// « je veux 1/x² ») : source `1/x2` rend `\frac{1}{x}2` au lieu de
    /// `\frac{1}{x^2}` (= le `2` flotte hors fraction au lieu d'être
    /// l'exposant de `x`).
    ///
    /// <para>Cas générique : `<expr>/<letter><number>` doit absorber le
    /// number comme exposant du letter dans le dénominateur (= conv math
    /// standard, `1/x²` est trivial pour un lycéen).</para>
    /// </summary>
    public class OneOverXNBugProbeTests
    {
        private readonly ITestOutputHelper _output;

        public OneOverXNBugProbeTests(ITestOutputHelper output) { _output = output; }

        [Fact]
        public void OneOverX2_should_make_x_squared_in_denominator()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("1/x2");
            _output.WriteLine($"top='{r.TopLatex}'");
            _output.WriteLine($"collisions={r.Collisions.Count}");
            for (int i = 0; i < r.Collisions.Count; i++)
                _output.WriteLine($"  cand[{i}] latex='{r.Collisions[i].Latex}' rule={r.Collisions[i].RuleId}");
            // Attendu : default OU alt = \frac{1}{x^{2}}
            bool defaultOrAlt = r.TopLatex.Contains("\\frac{1}{x^{2}}")
                || System.Linq.Enumerable.Any(r.Collisions, c => c.Latex.Contains("\\frac{1}{x^{2}}"));
            Assert.True(defaultOrAlt,
                $"Expected \\frac{{1}}{{x^{{2}}}} as default or alt, got top='{r.TopLatex}'");
        }

        [Fact]
        public void GeneralX_NUMBER_pattern_inside_denominator()
        {
            // Le pattern doit marcher sur d'autres lettres + chiffres.
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("a/b3");
            _output.WriteLine($"top='{r.TopLatex}'");
            for (int i = 0; i < r.Collisions.Count; i++)
                _output.WriteLine($"  cand[{i}] latex='{r.Collisions[i].Latex}'");
            bool defaultOrAlt = r.TopLatex.Contains("\\frac{a}{b^{3}}")
                || System.Linq.Enumerable.Any(r.Collisions, c => c.Latex.Contains("\\frac{a}{b^{3}}"));
            Assert.True(defaultOrAlt, $"Expected \\frac{{a}}{{b^{{3}}}}, got top='{r.TopLatex}'");
        }
    }
}
