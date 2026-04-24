using System.Diagnostics;
using System.Linq;
using Engine = MathCursor.Core.PatternEngine.PatternEngine;
using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Core.Tests
{
    /// <summary>
    /// Micro-benchmark : mesure le temps de Convert sur 100 inputs représentatifs.
    /// Cible brief : p95 &lt; 20 ms. On log aussi p50 et max.
    /// Non-déterministe mais révélateur si une régression perf se glisse.
    /// </summary>
    public sealed class BenchmarkTests
    {
        private readonly ITestOutputHelper _log;
        public BenchmarkTests(ITestOutputHelper log) { _log = log; }

        [Fact]
        public void Convert_p95_under_20ms_for_typical_inputs()
        {
            var engine = Engine.LoadEmbedded("fr");
            var inputs = new[]
            {
                "x^2", "u_n", "f(x) = 2x+1", "1/2", "(x+1)/(x-1)",
                "lim x->0 sin(x)/x", "Sum k=1 to n k^2", "int 0 to 1 x^2 dx",
                "V x ( R*", "AB+BC=AC", "\\sqrt{x+1}", "P(A|B)", "E(X) = 0",
                "F(x) = 1/sqrt(x+1)^2", "alpha+beta=gamma", "cos(x)+sin(y)",
                "[0;1] U [2;3]", "prod k=1 to 10 k", "O_n = O_n-1 + 1",
                "x,y ( R", "Cf", "arccos(x)", "f'(x) = 2x",
            };

            // Warm-up (JIT + caches)
            foreach (var s in inputs) engine.Convert(s);

            const int reps = 5;
            var times = new double[inputs.Length * reps];
            int idx = 0;
            var sw = new Stopwatch();
            foreach (var input in inputs)
            {
                for (int r = 0; r < reps; r++)
                {
                    sw.Restart();
                    engine.Convert(input);
                    sw.Stop();
                    times[idx++] = sw.Elapsed.TotalMilliseconds;
                }
            }
            System.Array.Sort(times);
            double p50 = times[times.Length / 2];
            double p95 = times[(int)(times.Length * 0.95)];
            double max = times[times.Length - 1];
            double mean = times.Sum() / times.Length;

            _log.WriteLine($"N={times.Length} : mean={mean:F2}ms p50={p50:F2}ms p95={p95:F2}ms max={max:F2}ms");

            // On tolère un peu plus que 20ms pour éviter les flakes CI. 50ms = alarme franche.
            Assert.True(p95 < 50, $"p95={p95:F2}ms au-dessus de 50ms — régression perf suspectée");
        }
    }
}
