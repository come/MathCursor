using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Engine.Tests.Concepts
{
    /// <summary>
    /// Probe bug 2026-05-23 user-report : source `G :x->1/x` rend `G`
    /// (= tout sauf `G` est mangé). Cause : pattern legacy FuncDef
    /// (<c>Ident : Ident (, Ident)* -&gt; body</c>) jamais porté dans
    /// engine v2. Le `:` arrête le parse et le `->` n'est pas reconnu
    /// comme séparateur de body.
    ///
    /// <para>Convention math lycée FR : `f: x ↦ expr` (= avec \mapsto)
    /// distinct de l'égalité simple `f = expr`.</para>
    /// </summary>
    public class FuncDefBugProbeTests
    {
        private readonly ITestOutputHelper _output;

        public FuncDefBugProbeTests(ITestOutputHelper output) { _output = output; }

        [Fact]
        public void Single_var_funcdef_renders_mapsto()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("G:x->1/x");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.Contains("G:", r.TopLatex);
            Assert.Contains("\\mapsto", r.TopLatex);
            Assert.Contains("\\frac{1}{x}", r.TopLatex);
        }

        [Fact]
        public void Single_var_funcdef_with_space_and_nbsp()
        {
            // User-report : `G :x->1/x` avec NBSP avant `:` (Word autocorrect).
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("G :x->1/x");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.Contains("\\mapsto", r.TopLatex);
            Assert.Contains("\\frac{1}{x}", r.TopLatex);
        }

        [Fact]
        public void Two_vars_funcdef_renders_paren_mapsto()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("f:x,y->x+y");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.Contains("(x,y)", r.TopLatex);
            Assert.Contains("\\mapsto", r.TopLatex);
        }

        [Fact]
        public void Not_a_funcdef_fallback_to_normal_parse()
        {
            // `a:b` sans `->` ne doit PAS être un FuncDef → parse normal.
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("a:b");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.DoesNotContain("\\mapsto", r.TopLatex);
        }
    }
}
