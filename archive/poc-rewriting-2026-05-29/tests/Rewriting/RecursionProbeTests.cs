using MathCursor.Engine;
using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Engine.Tests.Rewriting
{
    /// <summary>Probe pour comprendre comment la récursion est gérée.</summary>
    public class RecursionProbeTests
    {
        private readonly ITestOutputHelper _output;
        public RecursionProbeTests(ITestOutputHelper output) { _output = output; }

        [Theory]
        [InlineData("frac 1 2")]
        [InlineData("1/Somme k 0 n f(k)")]
        [InlineData("1/sum k 0 n f(k)")]
        [InlineData("Somme k 0 n 1/f(k)")]
        [InlineData("sum k 0 n 1/f(k)")]
        [InlineData("frac 1 (sum k 0 n f(k))")]
        [InlineData("frac 1 sum k 0 n f(k)")]
        [InlineData("frac n n+1")]
        public void Show(string input)
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve(input);
            _output.WriteLine($"INPUT:  {input}");
            _output.WriteLine($"OUTPUT: {r.TopLatex}");
            _output.WriteLine($"RULE:   {r.RuleId}");
            _output.WriteLine("---");
        }
    }
}
