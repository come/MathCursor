using System.Collections.Generic;
using System.Linq;
using MathCursor.Core.PatternEngine;
using Engine = MathCursor.Core.PatternEngine.PatternEngine;
using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Core.Tests
{
    public sealed class PatternEngineGoldTests
    {
        private readonly ITestOutputHelper _log;
        public PatternEngineGoldTests(ITestOutputHelper log) { _log = log; }

        public static TheoryData<string, string, string> AllExamples()
        {
            var data = new TheoryData<string, string, string>();
            var repo = PatternRepository.LoadEmbedded("fr");
            foreach (var p in repo.Patterns)
                foreach (var ex in p.Examples)
                    if (string.IsNullOrEmpty(ex.Skip))
                        data.Add(p.Id, ex.Input, ex.Output);
            var repoEn = PatternRepository.LoadEmbedded("en");
            foreach (var p in repoEn.Patterns)
            {
                if (!p.Id.EndsWith("_en")) continue;
                foreach (var ex in p.Examples)
                    if (string.IsNullOrEmpty(ex.Skip))
                        data.Add(p.Id, ex.Input, ex.Output);
            }
            return data;
        }

        public static TheoryData<string, string, string, string> SkippedExamples()
        {
            var data = new TheoryData<string, string, string, string>();
            foreach (var lang in new[] { "fr", "en" })
            {
                var repo = PatternRepository.LoadEmbedded(lang);
                foreach (var p in repo.Patterns)
                    foreach (var ex in p.Examples)
                        if (!string.IsNullOrEmpty(ex.Skip))
                            data.Add(p.Id, ex.Input, ex.Output, ex.Skip);
            }
            return data;
        }

        [Theory]
        [MemberData(nameof(AllExamples))]
        public void Gold_example_matches_expected_output(string patternId, string input, string expected)
        {
            string lang = patternId.EndsWith("_en") ? "en" : "fr";
            var engine = Engine.LoadEmbedded(lang);
            var suggestions = engine.Convert(input);

            _log.WriteLine($"[{patternId}] input=\"{input}\" expected=\"{expected}\"");
            _log.WriteLine($"  → {suggestions.Count} suggestions :");
            foreach (var s in suggestions.Take(5))
                _log.WriteLine($"    score={s.Score:F1} pattern={s.PatternId} latex=\"{s.Latex}\"");

            Assert.NotEmpty(suggestions);
            // Comparaison insensible aux whitespaces : LaTeX ignore les espaces entre
            // tokens, donc "x + 1" et "x+1" rendent identique. L'engine peut produire
            // l'une ou l'autre forme selon le pattern qui gagne.
            string norm(string s) => s.Replace(" ", "").Replace("\t", "");
            string normExpected = norm(expected);
            Assert.Contains(suggestions, s => norm(s.Latex) == normExpected);
        }

        /// <summary>
        /// Exemples marqués <c>skip:</c> dans les YAML : la sortie attendue n'est pas
        /// réalisable par le moteur actuel (ambiguïté, pattern limitatif, etc.) mais on
        /// documente les entrées et on logge les candidats renvoyés par l'engine. Ces
        /// cas aident à prioriser les améliorations futures.
        /// </summary>
        [Theory]
        [MemberData(nameof(SkippedExamples))]
        public void Skipped_example_documents_known_limitation(string patternId, string input, string expected, string reason)
        {
            string lang = patternId.EndsWith("_en") ? "en" : "fr";
            var engine = Engine.LoadEmbedded(lang);
            var suggestions = engine.Convert(input);
            _log.WriteLine($"[{patternId}] input=\"{input}\" expected=\"{expected}\"");
            _log.WriteLine($"  skip: {reason}");
            _log.WriteLine($"  → {suggestions.Count} candidats actuels :");
            foreach (var s in suggestions.Take(5))
                _log.WriteLine($"    score={s.Score:F1} pattern={s.PatternId} latex=\"{s.Latex}\"");
            // Pas d'assertion dure : ces cas sont documentaires. Le moteur peut renvoyer
            // 0..N candidats, c'est à la couche appelante de trancher.
        }
    }
}
