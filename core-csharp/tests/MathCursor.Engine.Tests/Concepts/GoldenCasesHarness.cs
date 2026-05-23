using System.Collections.Generic;
using System.Linq;
using Xunit;
using MathCursor.Engine.Rules;

namespace MathCursor.Engine.Tests.Concepts
{
    /// <summary>
    /// Harness golden cases auto-validants : pour chaque <see cref="RuleSpec.Tests"/>
    /// co-localisé dans les YAML <c>data-v2/concepts/*.yml</c>, vérifie que
    /// <see cref="Engine.Resolve"/> retourne le LaTeX attendu.
    ///
    /// <para>Format des tests YAML : <c>"input =&gt; expected_latex"</c>.</para>
    ///
    /// <para>Brief v4 §2.1 : <c>tests:</c> co-localisés → chaque règle est
    /// auto-validante.</para>
    /// </summary>
    public class GoldenCasesHarness
    {
        public static IEnumerable<object[]> AllGoldenCases()
        {
            var concepts = RuleLoader.LoadAllEmbedded();
            foreach (var c in concepts)
            {
                foreach (var rule in c.Rules)
                {
                    for (int i = 0; i < rule.Tests.Count; i++)
                    {
                        var line = rule.Tests[i];
                        var idx = line.IndexOf("=>");
                        if (idx < 0) continue;
                        var input = line.Substring(0, idx).Trim().Trim('\'', '"');
                        var expected = line.Substring(idx + 2).Trim().Trim('\'', '"');
                        yield return new object[] { c.Concept, rule.Id, input, expected };
                    }
                }
            }
        }

        [Theory]
        [MemberData(nameof(AllGoldenCases))]
        public void Golden_case_passes(string concept, string ruleId, string input, string expected)
        {
            var engine = MathCursor.Engine.MathEngine.BuildDefault("fr");
            var result = engine.Resolve(input);
            Assert.True(result.TopLatex == expected,
                $"Concept {concept}/{ruleId} on '{input}': expected '{expected}', got '{result.TopLatex}'");
            Assert.Equal(ruleId, result.RuleId);
        }
    }
}
