using MathCursor.Engine;
using MathCursor.Engine.Adapter;
using Xunit;

namespace MathCursor.Engine.Adapter.Tests
{
    /// <summary>
    /// Typing-flow / partial-match (ADR 2026-05-30-Fix-partial-match-anchors,
    /// réalise le Principe 4 de l'ADR moteur V2). Vérifie qu'un anchor reconnu
    /// lève immédiatement un squelette à carrés (<c>\square</c>) avec
    /// <c>IsIncomplete=true</c>, et que chaque frappe remplit un slot — sans
    /// attendre que la saisie soit complète. C'est ce que la popup affiche pour
    /// guider l'élève.
    /// </summary>
    public class TypingFlowE2eTests
    {
        private static readonly EngineZoneSource _source =
            new EngineZoneSource(MathEngine.BuildDefault("fr"));

        // Remplissage PROGRESSIF d'une somme dans un dénominateur : à chaque
        // frappe un carré se remplit, la zone reste incomplète jusqu'au bout.
        [Theory]
        [InlineData("som",             @"\sum_{\square=\square}^{\square} \square",     true)]
        [InlineData("1/som",           @"\frac{1}{\sum_{\square=\square}^{\square} \square}", true)]
        [InlineData("1/som k",         @"\frac{1}{\sum_{k=\square}^{\square} \square}", true)]
        [InlineData("1/som k 1",       @"\frac{1}{\sum_{k=1}^{\square} \square}",       true)]
        [InlineData("1/som k 1 n",     @"\frac{1}{\sum_{k=1}^{n} \square}",             true)]
        [InlineData("1/som k 1 n k",   @"\frac{1}{\sum_{k=1}^{n} k}",                   false)]
        public void Sum_in_denominator_fills_progressively(
            string input, string expectedLatex, bool expectedIncomplete)
        {
            var zone = _source.TryResolve(input, out _);
            Assert.NotNull(zone);
            Assert.Equal(expectedLatex, zone!.TopLatex);
            Assert.Equal(expectedIncomplete, zone.IsIncomplete);
        }

        // Les autres anchors lèvent aussi leur squelette dès le mot-clé.
        [Theory]
        [InlineData("vec",     @"\vec{\square}")]
        [InlineData("sqrt",    @"\sqrt{\square}")]
        [InlineData("int",     @"\int_{\square}^{\square} \square \, d\square")]
        [InlineData("lim",     @"\lim_{\square \to \square} \square")]
        [InlineData("forall",  @"\forall \square \in \square, \square")]
        public void Bare_anchor_raises_skeleton(string input, string expectedLatex)
        {
            var zone = _source.TryResolve(input, out _);
            Assert.NotNull(zone);
            Assert.Equal(expectedLatex, zone!.TopLatex);
            Assert.True(zone.IsIncomplete);
        }
    }
}
