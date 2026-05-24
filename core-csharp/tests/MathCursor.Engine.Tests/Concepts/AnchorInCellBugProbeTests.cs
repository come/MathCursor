using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Engine.Tests.Concepts
{
    /// <summary>
    /// Probe bug 2026-05-23 user-report : `(a b c ; d e (somme k 0 1 f(k))` rend
    /// `(sommek01f(k))` comme cellule (= concat sans espaces, `somme` pas
    /// reconnu comme ancre).
    ///
    /// <para>Cas générique : n'importe quelle ancre (lim, sum, int, sin, …)
    /// dans une cellule de matrice / groupe / délimité doit être reconnue.
    /// Pas juste `somme`.</para>
    /// </summary>
    public class AnchorInCellBugProbeTests
    {
        private readonly ITestOutputHelper _output;

        public AnchorInCellBugProbeTests(ITestOutputHelper output) { _output = output; }

        [Fact]
        public void Sum_inside_paren_group()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("(somme k 0 1 f(k))");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.Contains("\\sum", r.TopLatex);
        }

        [Fact]
        public void Lim_inside_paren_group()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("(lim x 0 f(x))");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.Contains("\\lim", r.TopLatex);
        }

        [Fact]
        public void Sum_inside_matrix_cell()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("(a b ; (somme k 0 1 k) d)");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.Contains("\\sum", r.TopLatex);
        }
    }
}
