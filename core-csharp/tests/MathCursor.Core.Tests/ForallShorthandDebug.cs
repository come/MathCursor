using System.Linq;
using Engine = MathCursor.Core.PatternEngine.PatternEngine;
using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Core.Tests
{
    /// <summary>
    /// Vérifie que "V x ( R" et "V x C R" produisent tous deux
    /// "\forall x \in \mathbb{R}" en top-1.
    /// </summary>
    public sealed class ForallShorthandDebug
    {
        private readonly ITestOutputHelper _log;
        public ForallShorthandDebug(ITestOutputHelper log) { _log = log; }

        [Theory]
        [InlineData("V x ( R", true)]
        [InlineData("V x C R", true)]
        [InlineData("Vx(R", true)]
        [InlineData("Vx C R", true)]
        [InlineData("V x,y ( R", true)]
        [InlineData("V x,y C R", true)]
        // Sans V initial : appartenance simple, pas forall
        [InlineData("x ( R", false)]
        [InlineData("x C R", false)]
        public void Shorthand_top1_is_correct(string input, bool expectForall)
        {
            var engine = Engine.LoadEmbedded("fr");
            var sugg = engine.Convert(input);
            _log.WriteLine($"Input: {input} → {sugg.Count} candidats");
            foreach (var s in sugg.Take(5))
                _log.WriteLine($"  score={s.Score:F1} [{s.PatternId}] {s.Latex}");

            Assert.NotEmpty(sugg);
            var top = sugg[0];
            Assert.Contains("\\in", top.Latex);
            Assert.Contains("\\mathbb{R}", top.Latex);
            if (expectForall)
                Assert.Contains("\\forall", top.Latex);
            else
                Assert.DoesNotContain("\\forall", top.Latex);
        }
    }
}
