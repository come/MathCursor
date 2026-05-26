using System.Collections.Generic;
using System.Text;
using MathCursor.Engine.Rewriting;
using MathCursor.Engine.Rewriting.Yaml;
using MathCursor.Engine.Rules;
using MathCursor.Engine.Vocabulary;
using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Engine.Tests.Rewriting
{
    /// <summary>
    /// Phase D-1 — audit : tourne TOUS les tests inline des
    /// <c>data-v2/concepts/*.yml</c> via le <see cref="RewriteEngine"/>
    /// (= règles YAML chargées + primitives). Mesure passes/fails.
    /// Le test ne fail PAS — il imprime le rapport dans <see cref="ITestOutputHelper"/>
    /// pour analyser le gap vs <c>MathEngine.Resolve</c> actuel.
    /// </summary>
    public class PhaseDAuditTests
    {
        private readonly ITestOutputHelper _output;
        public PhaseDAuditTests(ITestOutputHelper output) { _output = output; }

        [Fact]
        public void Audit_all_yaml_tests_against_RewriteEngine()
        {
            var vocab = LocaleVocabulary.LoadEmbedded("fr");
            var rules = new List<RewriteRule>();
            rules.AddRange(MathCursor.Engine.Rewriting.PrimitiveRules.All);
            rules.AddRange(RewriteRuleLoader.LoadAllEmbedded(vocab));
            var engine = new RewriteEngine(vocab, rules);

            int total = 0, pass = 0;
            var fails = new List<string>();
            var concepts = RuleLoader.LoadAllEmbedded();
            foreach (var c in concepts)
            foreach (var rule in c.Rules)
            foreach (var line in rule.Tests)
            {
                var idx = line.IndexOf("=>");
                if (idx < 0) continue;
                var input = line.Substring(0, idx).Trim().Trim('\'', '"');
                var expected = line.Substring(idx + 2).Trim().Trim('\'', '"');
                total++;
                var actual = engine.Resolve(input).TopLatex;
                if (actual == expected)
                {
                    pass++;
                }
                else
                {
                    fails.Add($"  ❌ [{c.Concept}/{rule.Id}] '{input}'");
                    fails.Add($"      expected: {expected}");
                    fails.Add($"      actual:   {actual}");
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"=== Phase D audit ===");
            sb.AppendLine($"Pass: {pass}/{total}  ({100.0 * pass / total:0.0}%)");
            sb.AppendLine();
            if (fails.Count > 0)
            {
                sb.AppendLine($"Failures ({fails.Count / 3}) :");
                foreach (var f in fails) sb.AppendLine(f);
            }
            _output.WriteLine(sb.ToString());

            // Audit ne fail PAS — on regarde le rapport. Mais on impose un
            // plancher pour détecter les régressions massives accidentelles.
            Assert.True(pass >= 1, "Au moins 1 test YAML doit passer pour confirmer que le loader fonctionne.");
        }
    }
}
