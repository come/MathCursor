using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Engine.Tests.Concepts
{
    /// <summary>
    /// Probe bug 2026-05-23 user-reports :
    /// <list type="bullet">
    ///   <item><c>Cos x</c> rend <c>Cos</c> (= le `x` est mangé).</item>
    ///   <item><c>Cos(x)2</c> rend <c>Cos(x)2</c> (= pas d'exposant,
    ///     user-commented « j'attends cos(x)² »).</item>
    /// </list>
    /// <para>Cas générique :
    /// <list type="bullet">
    ///   <item>(B1) Function known + argument à droite (avec ou sans
    ///     parenthèses, avec ou sans espace). Trim-Sep doit traverser.</item>
    ///   <item>(B2) <c>&lt;groupe&gt;&lt;number&gt;</c> collé doit faire
    ///     <c>&lt;groupe&gt;^{N}</c>. Extension du letter+number aux groupes.</item>
    /// </list></para>
    /// </summary>
    public class CosXBugProbeTests
    {
        private readonly ITestOutputHelper _output;

        public CosXBugProbeTests(ITestOutputHelper output) { _output = output; }

        [Fact]
        public void Cos_x_should_render_as_cos_x()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("cos x");
            _output.WriteLine($"top='{r.TopLatex}'");
            // Attendu : `\cos x` ou similaire, le `x` ne doit pas être perdu.
            Assert.Contains("\\cos", r.TopLatex);
            Assert.Contains("x", r.TopLatex);
        }

        [Fact]
        public void Cos_x_uppercase_should_also_work()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("Cos x");
            _output.WriteLine($"top='{r.TopLatex}'");
            // Tolérance casse : Cos doit être traité comme cos.
            Assert.Contains("\\cos", r.TopLatex);
            Assert.Contains("x", r.TopLatex);
        }

        [Fact]
        public void Cos_paren_x_squared_collé()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("cos(x)2");
            _output.WriteLine($"top='{r.TopLatex}'");
            // Attendu : default OU alt = `\cos(x)^{2}` ou `\cos^{2}(x)`.
            bool ok = r.TopLatex.Contains("\\cos(x)^{2}")
                || r.TopLatex.Contains("\\cos^{2}(x)")
                || r.TopLatex.Contains("\\cos\\left(x\\right)^{2}")
                || System.Linq.Enumerable.Any(r.Collisions,
                    c => c.Latex.Contains("\\cos") && c.Latex.Contains("^{2}"));
            Assert.True(ok, $"Expected cos(x)² form, got top='{r.TopLatex}'");
        }
    }
}
