using Xunit;
using MathCursor.Engine;

namespace MathCursor.Engine.Tests.Concepts
{
    /// <summary>
    /// Tests P13 (brief v5 §3) sur la composition body greedy-jusqu'à-ancre :
    /// la 2e ancre stoppe le body de la 1re, permet l'imbrication via 1er
    /// opérande.
    /// </summary>
    public class CompositionTests
    {
        [Fact]
        public void Body_greedy_until_next_anchor_composes_via_infix()
        {
            // P14 : composition top-level. `lim x 0 f + lim x 1 g` →
            // (lim_1) + (lim_2). La 2e ancre stoppe le body de la 1re,
            // puis le `+` top-level lie les 2 lim au niveau supérieur.
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("lim x 0 f + lim x 1 g");
            Assert.Equal(@"\lim_{x \to 0} f+\lim_{x \to 1} g", r.TopLatex);
        }

        [Fact]
        public void Body_greedy_wide_steno_default()
        {
            // `lim x 0 1/x+1` : pas d'ancre dans le body, on consomme tout.
            // Brief v5 §3 : wide est le guess steno.
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("lim x 0 1/x+1");
            Assert.Equal(@"\lim_{x \to 0} \frac{1}{x}+1", r.TopLatex);
        }
    }
}
