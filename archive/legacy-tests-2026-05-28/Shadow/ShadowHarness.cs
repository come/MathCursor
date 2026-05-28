using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;
using MathCursor.Engine.Rules;

namespace MathCursor.Engine.Tests.Shadow
{
    /// <summary>
    /// Harness shadow P11.15 (2026-05-22) — pour chaque <c>tests:</c> co-localisé
    /// dans les YAML <c>data-v2/concepts/*.yml</c>, exécute le moteur v2 et
    /// rapporte le diff vs la <i>attendue</i> co-localisée (proxy du contrat
    /// <c>ResolvedZone.TopLatex</c>).
    ///
    /// <para>Cible POC : <b>100 %</b> des golden cases (= les 6 cas
    /// limites+sommes ci-dessous). Le harness vs <c>LimAmbigBugTests</c>
    /// legacy demandera de référencer le projet Core.Tests, prévu en P12.</para>
    /// </summary>
    public class ShadowHarness
    {
        private readonly ITestOutputHelper _output;

        public ShadowHarness(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Engine_parity_on_collocated_golden_cases()
        {
            var engine = MathCursor.Engine.MathEngine.BuildDefault("fr");
            var concepts = RuleLoader.LoadAllEmbedded();

            int total = 0;
            int passed = 0;
            var failures = new List<string>();

            foreach (var concept in concepts)
            {
                foreach (var rule in concept.Rules)
                {
                    foreach (var t in rule.Tests)
                    {
                        int idx = t.IndexOf("=>");
                        if (idx < 0) continue;
                        var input = t.Substring(0, idx).Trim().Trim('\'', '"');
                        var expected = t.Substring(idx + 2).Trim().Trim('\'', '"');
                        total++;
                        var result = engine.Resolve(input);
                        if (result.TopLatex == expected)
                        {
                            passed++;
                        }
                        else
                        {
                            failures.Add(
                                $"[{concept.Concept}/{rule.Id}] input='{input}' " +
                                $"expected='{expected}' got='{result.TopLatex}'");
                        }
                    }
                }
            }

            // Rapport humain dans la sortie test.
            _output.WriteLine($"┌─ Engine v2 shadow report — {System.DateTime.Now:yyyy-MM-dd HH:mm}");
            _output.WriteLine($"├─ Concepts : {concepts.Count}");
            _output.WriteLine($"├─ Golden cases : {total}");
            _output.WriteLine($"├─ Passed : {passed} ({(100.0 * passed / System.Math.Max(1, total)):F1}%)");
            _output.WriteLine($"└─ Failed : {failures.Count}");
            foreach (var f in failures) _output.WriteLine("    " + f);

            // Gate POC : 100 % attendu sur ces 6 cas. Si rouge → POC à
            // re-challenger.
            Assert.True(failures.Count == 0,
                $"Parity broken : {failures.Count}/{total} failures. See output.");
        }

        [Fact]
        public void Engine_perf_smoke_50_tokens_under_5ms()
        {
            // Smoke perf : un input ~50 tokens doit re-parser < 5ms.
            // Brief §1.5 : "O(n), n petit → gratuit".
            var engine = MathCursor.Engine.MathEngine.BuildDefault("fr");
            // Concat de plusieurs sommes pour atteindre ~50 tokens.
            var input = "sum k 1 n (1/k) sum i 0 N (a_i)";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 100; i++) engine.Resolve(input);
            sw.Stop();
            double avgMs = sw.Elapsed.TotalMilliseconds / 100.0;
            _output.WriteLine($"Avg re-parse on ~50 tokens : {avgMs:F3} ms");
            Assert.True(avgMs < 5.0, $"Perf gate broken : {avgMs:F3} ms avg");
        }
    }
}
