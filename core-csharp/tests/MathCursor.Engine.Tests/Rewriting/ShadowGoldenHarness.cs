using System.Collections.Generic;
using MathCursor.Engine.Rewriting;
using MathCursor.Engine.Rewriting.Yaml;
using MathCursor.Engine.Rules;
using MathCursor.Engine.Vocabulary;
using Xunit;

namespace MathCursor.Engine.Tests.Rewriting
{
    /// <summary>
    /// Phase D-6 — shadow harness : tourne les <see cref="RuleSpec.Tests"/>
    /// co-localisés YAML via le <see cref="RewriteEngine"/> au lieu du
    /// <see cref="MathCursor.Engine.MathEngine"/> actuel. Permet de mesurer
    /// si la bascule est sûre AVANT de toucher le moteur prod.
    ///
    /// <para>Si tous ces tests passent (= 55/55 vert dans cet harness), la
    /// bascule <c>MathEngine.Resolve → RewriteEngine.Resolve</c> est sûre
    /// vis-à-vis des cas YAML inline.</para>
    ///
    /// <para>Reste à valider : les ~250 autres tests engine (= collision,
    /// slurp, edge cases) qui ne sont pas dans les YAML inline. Ceux-là
    /// seront vérifiés en Phase D-6b.</para>
    /// </summary>
    public class ShadowGoldenHarness
    {
        private static RewriteEngine BuildShadowEngine()
        {
            var vocab = LocaleVocabulary.LoadEmbedded("fr");
            var rules = new List<RewriteRule>();
            rules.AddRange(PrimitiveRules.All);
            rules.AddRange(RewriteRuleLoader.LoadAllEmbedded(vocab));
            return new RewriteEngine(vocab, rules);
        }

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
        public void Shadow_RewriteEngine_matches_YAML_expected(
            string concept, string ruleId, string input, string expected)
        {
            var engine = BuildShadowEngine();
            var result = engine.Resolve(input);
            Assert.True(result.TopLatex == expected,
                $"[{concept}/{ruleId}] '{input}': expected '{expected}', got '{result.TopLatex}'");
        }
    }
}
