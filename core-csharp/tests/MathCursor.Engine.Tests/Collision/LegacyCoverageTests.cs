using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Engine.Tests.Collision
{
    /// <summary>
    /// P30 (2026-05-22) : RED tests pour les cas legacy non encore couverts
    /// par Engine v2. Voir audit (Explore agent).
    /// </summary>
    public class LegacyCoverageTests
    {
        private readonly ITestOutputHelper _output;
        public LegacyCoverageTests(ITestOutputHelper output) { _output = output; }

        // ─── Slurp exposant : x^a+b → x^{a+b} (= alt) ─────────────────

        [Fact]
        public void Slurp_exposant_x_caret_a_plus_b()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("x^a+b");
            _output.WriteLine($"top='{r.TopLatex}' coll={r.Collisions.Count}");
            // Top : x^{a}+b (= défaut left-assoc).
            Assert.Equal("x^a+b", r.TopLatex);
            // Slurp : x^{a+b}.
            Assert.Contains(r.Collisions, c => c.Latex.Contains("x^{a+b}"));
        }

        // ─── Slurp indice : u_n+1 → u_{n+1} (= alt) ───────────────────

        [Fact]
        public void Slurp_indice_u_under_n_plus_1()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("u_n+1");
            _output.WriteLine($"top='{r.TopLatex}' coll={r.Collisions.Count}");
            // Top : u_{n}+1 (= défaut).
            Assert.Equal("u_n+1", r.TopLatex);
            // Slurp : u_{n+1}.
            Assert.Contains(r.Collisions, c => c.Latex.Contains("u_{n+1}"));
        }

        // ─── ABC triangle (= 3 majuscules) ────────────────────────────

        [Fact]
        public void Three_uppercase_yields_triangle_alt()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("ABC");
            _output.WriteLine($"top='{r.TopLatex}' coll={r.Collisions.Count}");
            Assert.Equal("ABC", r.TopLatex);
            Assert.Contains(r.Collisions, c => c.Latex == @"\triangle ABC");
        }

        // ─── Angle refactor (= ^X doit toujours marcher) ──────────────

        [Fact]
        public void Caret_X_still_yields_angle()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("^ABC");
            Assert.Equal(@"\widehat{ABC}", r.TopLatex);
        }

        [Fact]
        public void Caret_single_letter_still_yields_angle()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("^a");
            Assert.Equal(@"\widehat{a}", r.TopLatex);
        }
    }
}
