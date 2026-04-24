using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Engine = MathCursor.Core.PatternEngine.PatternEngine;
using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Core.Tests
{
    /// <summary>
    /// Corpus calibré sur le cours de Seconde (Maths.docx). Rapport pass/fail
    /// par catégorie — même mécanisme que CorpusLyceeTests.
    /// </summary>
    public sealed class CorpusSecondeTests
    {
        private readonly ITestOutputHelper _log;
        public CorpusSecondeTests(ITestOutputHelper log) { _log = log; }

        private static string CorpusPath =>
            Path.Combine(AppContext.BaseDirectory, "corpus", "seconde-cours.txt");

        [Fact]
        public void Corpus_seconde_report()
        {
            Assert.True(File.Exists(CorpusPath), $"corpus introuvable : {CorpusPath}");
            var engine = Engine.LoadEmbedded("fr");
            var sections = ParseCorpus(File.ReadAllLines(CorpusPath));

            int total = 0, pass = 0;
            _log.WriteLine($"# Corpus Seconde — {sections.Count} catégories");
            foreach (var (title, pairs) in sections)
            {
                if (pairs.Count == 0) continue;
                int sectionPass = 0;
                var fails = new List<string>();
                foreach (var (input, expected) in pairs)
                {
                    total++;
                    var suggestions = engine.Convert(input);
                    string norm(string s) => s.Replace(" ", "").Replace("\t", "");
                    string needle = norm(expected);
                    if (suggestions.Any(s => norm(s.Latex).Contains(needle)))
                    {
                        sectionPass++; pass++;
                    }
                    else
                    {
                        var top = suggestions.FirstOrDefault();
                        fails.Add($"    FAIL \"{input}\" → expected \"{expected}\", got \"{top?.Latex ?? "<none>"}\"");
                    }
                }
                _log.WriteLine($"## {title} — {sectionPass}/{pairs.Count}");
                foreach (var f in fails) _log.WriteLine(f);
            }
            _log.WriteLine($"# TOTAL : {pass}/{total} ({(total > 0 ? 100.0 * pass / total : 0):F1}%)");
            Assert.True(total > 0, "aucune paire dans le corpus");
        }

        private static List<(string, List<(string, string)>)> ParseCorpus(string[] lines)
        {
            var result = new List<(string, List<(string, string)>)>();
            string currentTitle = "(sans titre)";
            var currentPairs = new List<(string, string)>();
            foreach (var raw in lines)
            {
                var line = raw.TrimEnd();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("#"))
                {
                    var trimmed = line.TrimStart('#', ' ');
                    if (trimmed.Length == 0 || trimmed.StartsWith("---") || trimmed.StartsWith("===")) continue;
                    if (currentPairs.Count > 0 || currentTitle != "(sans titre)")
                    {
                        result.Add((currentTitle, currentPairs));
                        currentPairs = new List<(string, string)>();
                    }
                    currentTitle = trimmed;
                    continue;
                }
                int arrowIdx = line.LastIndexOf(" => ", StringComparison.Ordinal);
                if (arrowIdx < 0) continue;
                var input = line.Substring(0, arrowIdx).Trim();
                var expected = line.Substring(arrowIdx + 4).Trim();
                if (input.Length == 0 || expected.Length == 0) continue;
                currentPairs.Add((input, expected));
            }
            if (currentPairs.Count > 0 || currentTitle != "(sans titre)")
                result.Add((currentTitle, currentPairs));
            return result;
        }
    }
}
