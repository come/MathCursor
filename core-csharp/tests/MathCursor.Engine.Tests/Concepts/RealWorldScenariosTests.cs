using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Engine.Tests.Concepts
{
    /// <summary>
    /// P25 (2026-05-22) : tests de scénarios réels demandés par l'user.
    /// Couvre les cas user-tapped typiques pour valider que l'Engine v2
    /// rend correctement + détecte les collisions appropriées.
    /// </summary>
    public class RealWorldScenariosTests
    {
        private readonly ITestOutputHelper _output;

        public RealWorldScenariosTests(ITestOutputHelper output)
        {
            _output = output;
        }

        // ─── Fraction avec contexte gauche : f(x)=1/x+1 ──────────────────

        [Fact]
        public void Fx_equals_one_over_x_plus_one_yields_two_candidates()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("f(x)=1/x+1");
            _output.WriteLine($"top='{r.TopLatex}' collisions={r.Collisions.Count}");
            foreach (var c in r.Collisions)
                _output.WriteLine($"  cand: '{c.Latex}' ({c.Description})");
            // Doit avoir 2 candidats (= défaut + slurp).
            Assert.True(r.Collisions.Count >= 1,
                $"Attendu ≥ 1 collision pour `f(x)=1/x+1`, reçu {r.Collisions.Count}");
            // Top : f(x) = \frac{1}{x}+1
            Assert.Contains(@"\frac{1}{x}+1", r.TopLatex);
            // Slurp : f(x) = \frac{1}{x+1}
            Assert.Contains(r.Collisions, c => c.Latex.Contains(@"\frac{1}{x+1}"));
        }

        // ─── Geometrie AB/AC ─────────────────────────────────────────────

        [Fact]
        public void AB_over_AC_yields_consistent_lines()
        {
            // `AB/AC` : ratio de longueurs (= notation géo).
            // Top : \frac{AB}{AC}
            // Pas de collision attendue (= pas de `+` ou `-` après).
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("AB/AC");
            _output.WriteLine($"top='{r.TopLatex}' collisions={r.Collisions.Count}");
            Assert.Equal(@"\frac{AB}{AC}", r.TopLatex);
            Assert.Empty(r.Collisions);
        }

        // ─── Géométrie : (AB)/(CD) ───────────────────────────────────────

        [Fact]
        public void Paren_AB_over_paren_CD_renders_fraction()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("(AB)/(CD)");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.Contains(@"\frac{", r.TopLatex);
            Assert.Contains("AB", r.TopLatex);
            Assert.Contains("CD", r.TopLatex);
        }

        // ─── Équations avec fraction : x = 1/y + z ───────────────────────

        [Fact]
        public void Equation_with_fraction_and_slurp()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("x=1/y+z");
            _output.WriteLine($"top='{r.TopLatex}' collisions={r.Collisions.Count}");
            // Top : x = \frac{1}{y}+z
            Assert.Contains(@"\frac{1}{y}+z", r.TopLatex);
            // Slurp attendu : x = \frac{1}{y+z}
            Assert.True(r.Collisions.Count >= 1,
                $"Attendu ≥ 1 slurp pour `x=1/y+z`, reçu {r.Collisions.Count}");
        }

        // ─── Pas de slurp dans des cas non ambigus ───────────────────────

        [Fact]
        public void No_slurp_when_fraction_isolated()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("a/b");
            Assert.Equal(@"\frac{a}{b}", r.TopLatex);
            Assert.Empty(r.Collisions);
        }

        [Fact]
        public void No_slurp_when_only_multiplication_after()
        {
            // `a/b*c` : pas de + ou - après → pas de slurp.
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("a/b*c");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.Empty(r.Collisions);
        }

        // ─── Cas multiples fractions ──────────────────────────────────────

        [Fact]
        public void Sum_of_two_fractions_no_slurp_needed()
        {
            // `1/a+1/b` : pas vraiment de slurp utile, c'est juste 1/a + 1/b.
            // Le slurp serait `1/(a+1/b)` (= moins probable).
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("1/a+1/b");
            _output.WriteLine($"top='{r.TopLatex}' collisions={r.Collisions.Count}");
            Assert.Contains(@"\frac{1}{a}", r.TopLatex);
            Assert.Contains(@"\frac{1}{b}", r.TopLatex);
        }
    }
}
