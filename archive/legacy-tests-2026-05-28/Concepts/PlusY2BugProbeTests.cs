using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Engine.Tests.Concepts
{
    /// <summary>
    /// Probe bug 2026-05-23 user-reported : "x2 commit + y2 commit le + est mangé".
    /// Scénario : 2 commits séparés. Le 2e commit prend la zone "+y2" en isolation
    /// (intra-merger ne déclenche pas — `+` n'est pas dans
    /// <c>IntraOMathsMerger.IsMergeMarker</c>). On vérifie ce que le moteur v2
    /// retourne pour "+y2" en isolation.
    /// </summary>
    public class PlusY2BugProbeTests
    {
        private readonly ITestOutputHelper _output;

        public PlusY2BugProbeTests(ITestOutputHelper output) { _output = output; }

        [Fact]
        public void PlusY2_in_isolation_must_keep_leading_plus()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("+y2");
            _output.WriteLine($"input='+y2'");
            _output.WriteLine($"top='{r.TopLatex}'");
            _output.WriteLine($"rule={r.RuleId} complete={r.IsComplete} collisions={r.Collisions.Count}");
            for (int i = 0; i < r.Collisions.Count; i++)
                _output.WriteLine($"  cand[{i}] latex='{r.Collisions[i].Latex}' rule={r.Collisions[i].RuleId}");
            Assert.Contains("+", r.TopLatex);
            Assert.Contains("y^{2}", r.TopLatex);
        }

        [Fact]
        public void PlusY2_with_space_before_must_keep_leading_plus()
        {
            // Si le manual trigger trim les espaces, le source est "+y2" sans espace,
            // mais si l'utilisateur tape "+ y2" et que le trim n'opère pas, ça donne
            // ce cas. On teste les deux.
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("+ y2");
            _output.WriteLine($"input='+ y2'");
            _output.WriteLine($"top='{r.TopLatex}'");
            _output.WriteLine($"rule={r.RuleId} complete={r.IsComplete} collisions={r.Collisions.Count}");
            Assert.Contains("+", r.TopLatex);
            Assert.Contains("y^{2}", r.TopLatex);
        }

        [Fact]
        public void X2_plus_y2_full_source_keeps_plus()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("x2+y2");
            _output.WriteLine($"input='x2+y2'");
            _output.WriteLine($"top='{r.TopLatex}'");
            _output.WriteLine($"rule={r.RuleId} complete={r.IsComplete}");
            Assert.Contains("x^{2}", r.TopLatex);
            Assert.Contains("y^{2}", r.TopLatex);
            Assert.Contains("+", r.TopLatex);
        }

        // Bug user-reported 2026-05-23 « le = saute au commit » — source
        // `=1/2x+1` rendu `\frac{1}{2}x+1` au lieu de `=\frac{1}{2}x+1`.
        // Même classe que le bug `+y2` mais sur `=` (et tous markers comp/rel).
        [Fact]
        public void Equals_in_isolation_must_keep_leading_equal()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("=1/2x+1");
            _output.WriteLine($"input='=1/2x+1'");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.Contains("=", r.TopLatex);
            Assert.Contains("\\frac{1}{2}", r.TopLatex);
        }

        [Fact]
        public void Leftrightarrow_in_isolation_must_keep_leading_marker()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("<=> x+1");
            _output.WriteLine($"input='<=> x+1'");
            _output.WriteLine($"top='{r.TopLatex}'");
            // \iff et \Leftrightarrow sont équivalents LaTeX (= rendu ⇔).
            Assert.True(
                r.TopLatex.Contains("\\Leftrightarrow") || r.TopLatex.Contains("\\iff"),
                $"Expected \\Leftrightarrow or \\iff, got: {r.TopLatex}");
            Assert.Contains("x", r.TopLatex);
        }

        [Fact]
        public void Rightarrow_in_isolation_must_keep_leading_marker()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("=> y2");
            _output.WriteLine($"input='=> y2'");
            _output.WriteLine($"top='{r.TopLatex}'");
            // \Rightarrow et \implies sont équivalents LaTeX.
            Assert.True(
                r.TopLatex.Contains("\\Rightarrow") || r.TopLatex.Contains("\\implies"),
                $"Expected \\Rightarrow or \\implies, got: {r.TopLatex}");
            Assert.Contains("y^{2}", r.TopLatex);
        }
    }
}
