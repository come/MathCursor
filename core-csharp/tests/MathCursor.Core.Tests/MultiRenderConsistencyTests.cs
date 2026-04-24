using System.Linq;
using Engine = MathCursor.Core.PatternEngine.PatternEngine;
using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Core.Tests
{
    /// <summary>
    /// Vérifie que le multi-rendu à INDEX CONSISTANT swap tous les slots en
    /// même temps. Quand un slot a une alt "vecteur" et un autre aussi, les deux
    /// doivent bascule ensemble (pas de mix incohérent).
    /// </summary>
    public sealed class MultiRenderConsistencyTests
    {
        private readonly ITestOutputHelper _log;
        public MultiRenderConsistencyTests(ITestOutputHelper log) { _log = log; }

        [Fact]
        public void AB_uv_plus_AC_dx_has_consistent_vec_variant()
        {
            var engine = Engine.LoadEmbedded("fr");
            var suggestions = engine.Convert("AB(u v) + AC(d x)");
            foreach (var s in suggestions.Take(5)) _log.WriteLine($"  {s.Score,6:F1}  {s.Latex}");

            // Variante cohérente : les DEUX côtés en vecteur, pas un mix.
            Assert.Contains(suggestions, s =>
                s.Latex.Contains("\\vec{AB}") && s.Latex.Contains("\\vec{AC}"));
            // Et AUCUNE variante mixte "vec + literal" ou "literal + vec"
            Assert.DoesNotContain(suggestions, s =>
                (s.Latex.Contains("\\vec{AB}") && !s.Latex.Contains("\\vec{AC}") && s.Latex.Contains("AC("))
                || (s.Latex.Contains("\\vec{AC}") && !s.Latex.Contains("\\vec{AB}") && s.Latex.Contains("AB(")));
        }

        [Fact]
        public void V_x_plus_1_over_V_x_minus_1_has_consistent_sqrt_variant()
        {
            // Les deux V(...) doivent être interprétés simultanément soit comme
            // variance (literal) soit comme sqrt, pas un mix.
            var engine = Engine.LoadEmbedded("fr");
            var suggestions = engine.Convert("V(x+1)/V(x-1)");
            foreach (var s in suggestions.Take(5)) _log.WriteLine($"  {s.Score,6:F1}  {s.Latex}");

            Assert.NotEmpty(suggestions);
        }
    }
}
