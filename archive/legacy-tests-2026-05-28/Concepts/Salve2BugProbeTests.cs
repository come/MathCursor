using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Engine.Tests.Concepts
{
    /// <summary>
    /// Probe bugs 2026-05-24 user-reports (2e salve CF) — 4 fixes YAML/vocab.
    /// </summary>
    public class Salve2BugProbeTests
    {
        private readonly ITestOutputHelper _output;

        public Salve2BugProbeTests(ITestOutputHelper output) { _output = output; }

        // ─── Bug 1 : `inter` → `\cap` ───────────────────────────────

        [Fact]
        public void Inter_keyword_renders_cap()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("[0,1[ inter [0,1]");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.Contains("\\cap", r.TopLatex);
        }

        // ─── Bug 2 : `u` entre intervalles → `\cup` ─────────────────

        [Fact]
        public void U_between_intervals_renders_cup()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("[0,1[ u [0,1]");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.Contains("\\cup", r.TopLatex);
        }

        [Fact]
        public void U_letter_inside_paren_NOT_reclassified()
        {
            // `f(u)` doit garder `u` comme lettre, pas `\cup`.
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("f(u)");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.DoesNotContain("\\cup", r.TopLatex);
            Assert.Contains("u", r.TopLatex);
        }

        [Fact]
        public void U_letter_in_product_NOT_reclassified()
        {
            // `2u` doit garder `u` comme lettre — pas entouré de brackets.
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("2u");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.DoesNotContain("\\cup", r.TopLatex);
        }

        // ─── Bug 5 : `*` FR → `\times` ──────────────────────────────

        [Fact]
        public void Star_in_fr_renders_times()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("X*y");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.Contains("\\times", r.TopLatex);
            Assert.DoesNotContain("\\cdot", r.TopLatex);
        }

        // ─── Bug 4 : `sum` anchor anglais ──────────────────────────

        [Fact]
        public void Sum_english_alias_works()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("sum k 0 n f(k)");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.Contains("\\sum", r.TopLatex);
        }

        // ─── Bug 4 (imbriqué) : sum dans body FuncDef ──────────────

        [Fact]
        public void Sum_with_4_args_inside_funcdef_body()
        {
            // User-report 2026-05-24 : « regle imbriquées non fonctionnelles ».
            // Note : la shape `sum {var} =? {from} {to} {body}` exige 4 args
            // minimum (var, from, to, body). Test ici avec n explicite.
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("F:x->sum k 0 n f(k)*x");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.Contains("\\mapsto", r.TopLatex);
            Assert.Contains("\\sum", r.TopLatex);
        }
    }
}
