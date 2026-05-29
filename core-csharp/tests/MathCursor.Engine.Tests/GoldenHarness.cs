using System.Collections.Generic;
using System.Linq;
using MathCursor.Engine;
using MathCursor.Engine.Rules;
using Xunit;

namespace MathCursor.Engine.Tests
{
    /// <summary>
    /// Harness golden auto-validant : exécute via le moteur V2 chaque test
    /// co-localisé dans les YAML <c>data/concepts/*.yml</c>.
    ///
    /// <para>Format d'un test (= dans le YAML, sous <c>tests:</c>) :</para>
    /// <code>
    /// tests:
    ///   - "frac 1 2 => \\frac{1}{2}"
    ///   - "1/2 => \\frac{1}{2} | \\frac{1}{2}"   # collisions : top | alt | alt
    /// </code>
    ///
    /// <para>L'attendu après <c>=&gt;</c> peut contenir plusieurs lectures
    /// séparées par <c> | </c>. La 1ère = <see cref="EngineResult.TopLatex"/>
    /// attendu. Les suivantes = collisions attendues (= vérifiées dès que le
    /// multi-chains est en place ; ignorées en Phase 1).</para>
    ///
    /// <para>Aucune dépendance Word — pur moteur. C'est le cahier des charges
    /// exécutable du résolveur.</para>
    /// </summary>
    public class GoldenHarness
    {
        public static IEnumerable<object[]> AllCases()
        {
            foreach (var concept in RuleLoader.LoadAllEmbedded())
            foreach (var rule in concept.Rules)
            foreach (var line in rule.Tests)
            {
                var idx = line.IndexOf("=>");
                if (idx < 0) continue;
                var input = line.Substring(0, idx).Trim();
                var expectedRaw = line.Substring(idx + 2).Trim();
                yield return new object[] { concept.Concept, rule.Id, input, expectedRaw };
            }
        }

        [Theory]
        [MemberData(nameof(AllCases))]
        public void Golden(string concept, string ruleId, string input, string expectedRaw)
        {
            // Séparateur de collision = " | " (pipe ENTOURÉ d'espaces). Sûr
            // car le LaTeX `\|` (= barres de norme) n'a pas d'espace autour
            // du pipe.
            var expected = expectedRaw.Split(new[] { " | " }, System.StringSplitOptions.None)
                .Select(s => s.Trim()).ToArray();
            var engine = MathEngine.BuildDefault("fr");
            var result = engine.Resolve(input);

            // 1ère lecture = TopLatex attendu.
            Assert.True(result.TopLatex == expected[0],
                $"[{concept}/{ruleId}] '{input}'\n" +
                $"  attendu : {expected[0]}\n" +
                $"  obtenu  : {result.TopLatex}");
        }
    }
}
