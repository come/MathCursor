using System.Linq;
using Engine = MathCursor.Core.PatternEngine.PatternEngine;
using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Core.Tests
{
    public sealed class IntervalRecursionDebug
    {
        private readonly ITestOutputHelper _log;
        public IntervalRecursionDebug(ITestOutputHelper log) { _log = log; }

        [Theory]
        [InlineData("[3;4]")]
        [InlineData("[0;1]")]
        [InlineData("[0;1]U[3;4]")]
        [InlineData("Df")]
        [InlineData("Df = R")]
        [InlineData("Q1")]
        public void Interval_debug(string input)
        {
            var engine = Engine.LoadEmbedded("fr");
            var sugg = engine.Convert(input);
            _log.WriteLine($"Input: {input} → {sugg.Count} candidats");
            foreach (var s in sugg.Take(5))
                _log.WriteLine($"  score={s.Score:F1} [{s.PatternId}] {s.Latex} (consumed={s.ConsumedTokens}/{s.TotalTokens})");
        }
    }
}
