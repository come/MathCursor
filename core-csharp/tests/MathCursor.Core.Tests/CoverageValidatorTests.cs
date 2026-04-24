using System.Collections.Generic;
using System.Linq;
using MathCursor.Core.PatternEngine;
using Engine = MathCursor.Core.PatternEngine.PatternEngine;
using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Core.Tests
{
    /// <summary>
    /// Vérifie que les gold examples ne perdent aucun token fort de l'input dans
    /// la sortie. Utilise CoverageValidator comme oracle. Protège contre les
    /// régressions où un pattern mangerait silencieusement un "d", un nombre,
    /// un identifiant.
    /// </summary>
    public sealed class CoverageValidatorTests
    {
        private readonly ITestOutputHelper _log;
        public CoverageValidatorTests(ITestOutputHelper log) { _log = log; }

        public static TheoryData<string, string, string> AllGoldExamples()
        {
            var data = new TheoryData<string, string, string>();
            foreach (var lang in new[] { "fr", "en" })
            {
                var repo = PatternRepository.LoadEmbedded(lang);
                foreach (var p in repo.Patterns)
                    foreach (var ex in p.Examples)
                        if (string.IsNullOrEmpty(ex.Skip))
                            data.Add(lang, ex.Input, ex.Output);
            }
            return data;
        }

        [Theory]
        [MemberData(nameof(AllGoldExamples))]
        public void Gold_output_preserves_all_strong_input_tokens(string lang, string input, string expected)
        {
            var repo = PatternRepository.LoadEmbedded(lang);
            var validator = new CoverageValidator(repo);
            var missing = validator.FindMissingTokens(input, expected);
            if (missing.Count > 0)
            {
                _log.WriteLine($"Input: \"{input}\" → \"{expected}\"");
                foreach (var m in missing) _log.WriteLine($"  MISSING: {m}");
            }
            Assert.Empty(missing);
        }

        [Fact]
        public void Engine_output_preserves_input_on_typical_inputs()
        {
            // Au lieu du gold, on passe par l'engine réel et on vérifie que le top
            // candidate ne perd pas d'info. C'est le check "runtime" utile.
            var repo = PatternRepository.LoadEmbedded("fr");
            var engine = new Engine(repo);
            var validator = new CoverageValidator(repo);

            var inputs = new[]
            {
                "x+y=5", "f(x)=2x+1", "Sum k=1 to n k^2", "lim x->0 sin(x)/x",
                "V x ( R*", "1/sqrt(x+1)", "AB+BC=AC",
            };
            foreach (var input in inputs)
            {
                var top = engine.Convert(input).FirstOrDefault();
                Assert.NotNull(top);
                var missing = validator.FindMissingTokens(input, top!.Latex);
                if (missing.Count > 0)
                    _log.WriteLine($"\"{input}\" → \"{top.Latex}\" missing: {string.Join(", ", missing)}");
                Assert.Empty(missing);
            }
        }
    }
}
