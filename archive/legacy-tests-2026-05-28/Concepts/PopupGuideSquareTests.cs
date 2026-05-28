using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Engine.Tests.Concepts
{
    /// <summary>
    /// User-request 2026-05-24 : « quand un truc comme somme ou limite est
    /// reperé/reconnu, je veux la popup avec les carrés jusqu'a la fin de
    /// la reconnaissance, pour aider et montrer les arguments en cours de
    /// frappe ». L'engine v2 doit produire un LaTeX avec <c>\square</c>
    /// sur les slots manquants quand l'ancre est reconnue mais le pattern
    /// incomplet.
    /// </summary>
    public class PopupGuideSquareTests
    {
        private readonly ITestOutputHelper _output;

        public PopupGuideSquareTests(ITestOutputHelper output) { _output = output; }

        // ─── Sum : anchor seul ──────────────────────────────────────

        [Fact]
        public void Sum_alone_shows_full_skeleton()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("sum");
            _output.WriteLine($"top='{r.TopLatex}' complete={r.IsComplete}");
            Assert.Contains("\\sum", r.TopLatex);
            Assert.Contains("\\square", r.TopLatex);
            Assert.False(r.IsComplete);
        }

        [Fact]
        public void Sum_with_var_only_shows_partial_skeleton()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("sum k");
            _output.WriteLine($"top='{r.TopLatex}' complete={r.IsComplete}");
            Assert.Contains("\\sum", r.TopLatex);
            Assert.Contains("k", r.TopLatex);
            Assert.Contains("\\square", r.TopLatex);
            Assert.False(r.IsComplete);
        }

        [Fact]
        public void Sum_with_var_and_from_only()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("sum k 0");
            _output.WriteLine($"top='{r.TopLatex}' complete={r.IsComplete}");
            Assert.Contains("\\sum_{k=0}", r.TopLatex);
            Assert.Contains("\\square", r.TopLatex); // to et body manquants
            Assert.False(r.IsComplete);
        }

        // ─── Lim : anchor seul ──────────────────────────────────────

        [Fact]
        public void Lim_alone_shows_full_skeleton()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("lim");
            _output.WriteLine($"top='{r.TopLatex}' complete={r.IsComplete}");
            Assert.Contains("\\lim", r.TopLatex);
            Assert.Contains("\\square", r.TopLatex);
            Assert.False(r.IsComplete);
        }

        // ─── Test : full match préfère sur partial ─────────────────

        [Fact]
        public void Full_match_preferred_over_partial()
        {
            // `sum k 0 n f(k)` doit donner FULL match, pas partial.
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("sum k 0 n f(k)");
            _output.WriteLine($"top='{r.TopLatex}' complete={r.IsComplete}");
            Assert.DoesNotContain("\\square", r.TopLatex);
            Assert.True(r.IsComplete);
        }
    }
}
