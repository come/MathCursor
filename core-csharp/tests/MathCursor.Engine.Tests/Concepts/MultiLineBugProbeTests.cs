using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Engine.Tests.Concepts
{
    /// <summary>
    /// Probe bug 2026-05-23 user-reported : « merge interligne vers multiligne
    /// est complètement cassé, ça merge en mettant tout sur une ligne ». Cas :
    /// commit OMath ligne 1, puis ligne 2 commence par marker align (= `=`,
    /// `=>`, `<=>`) → MarkerChainCascadeMerger produit un mergedSource avec
    /// `\n` séparateurs (cf. ADR 2026-05-04-Feat-multiline-edit-cascade-merge).
    /// Le moteur doit produire un LaTeX align*/eqArray multi-ligne.
    ///
    /// <para>Engine v2 a été promu en P32 (2026-05-23). Hypothèse : engine v2
    /// ne sait pas gérer les `\n` et produit du LaTeX 1-ligne incorrect.</para>
    /// </summary>
    public class MultiLineBugProbeTests
    {
        private readonly ITestOutputHelper _output;

        public MultiLineBugProbeTests(ITestOutputHelper output) { _output = output; }

        [Fact]
        public void TwoLines_align_marker_should_produce_multiline_latex()
        {
            var engine = MathEngine.BuildDefault("fr");
            // Source produit par MarkerChainCascadeMerger après absorption
            // d'une OMath au-dessus contenant "a+b" et un texte courant "= c".
            var r = engine.Resolve("a+b\n= c");
            _output.WriteLine($"input='a+b\\n= c'");
            _output.WriteLine($"top='{r.TopLatex}'");
            _output.WriteLine($"rule={r.RuleId} complete={r.IsComplete} collisions={r.Collisions.Count}");
            // Attendu : LaTeX multi-ligne (align*/eqArray ou \\) — pas une seule ligne plate.
            // Si bug : "a+b = c" sur 1 ligne.
            Assert.True(
                r.TopLatex.Contains("\\\\") || r.TopLatex.Contains("aligned") || r.TopLatex.Contains("array"),
                $"Expected multiline LaTeX construct (\\\\ / aligned / array), got: \"{r.TopLatex}\"");
        }

        [Fact]
        public void ThreeLines_chain_should_produce_multiline_latex()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("x = y\n= z\n= w");
            _output.WriteLine($"input='x = y\\n= z\\n= w'");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.True(
                r.TopLatex.Contains("\\\\") || r.TopLatex.Contains("aligned") || r.TopLatex.Contains("array"),
                $"Expected multiline LaTeX, got: \"{r.TopLatex}\"");
        }
    }
}
