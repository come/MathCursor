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
    /// Charge le corpus lycée/prépa et reporte quelles paires input=>output passent
    /// ou échouent. Le test NE fait pas échouer sur les non-implémentés : il sert
    /// de tableau de bord pour prioriser les features manquantes.
    ///
    /// Format du fichier : lignes "input => latex_attendu". Les lignes vides et
    /// celles commençant par # sont ignorées (les # en tête de bloc servent de
    /// titres de catégorie dans le rapport).
    /// </summary>
    public sealed class CorpusLyceeTests
    {
        private readonly ITestOutputHelper _log;
        public CorpusLyceeTests(ITestOutputHelper log) { _log = log; }

        private static string CorpusPath =>
            Path.Combine(AppContext.BaseDirectory, "corpus", "lycee-priorities.txt");

        [Fact]
        public void Corpus_report_pass_rate_per_category()
        {
            Assert.True(File.Exists(CorpusPath), $"corpus introuvable : {CorpusPath}");

            var engine = Engine.LoadEmbedded("fr");
            var sections = ParseCorpus(File.ReadAllLines(CorpusPath));

            int totalPairs = 0, totalPass = 0;
            _log.WriteLine($"# Corpus rapport — {sections.Count} catégories");
            _log.WriteLine("");

            foreach (var (title, pairs) in sections)
            {
                if (pairs.Count == 0) continue;
                int pass = 0;
                var fails = new List<string>();
                foreach (var (input, expected) in pairs)
                {
                    totalPairs++;
                    var suggestions = engine.Convert(input);
                    string norm(string s) => s.Replace(" ", "").Replace("\t", "");
                    string needle = norm(expected);
                    if (suggestions.Any(s => norm(s.Latex).Contains(needle)))
                    {
                        pass++; totalPass++;
                    }
                    else
                    {
                        var top = suggestions.FirstOrDefault();
                        fails.Add($"    FAIL \"{input}\" → expected \"{expected}\", got \"{top?.Latex ?? "<none>"}\"");
                    }
                }
                _log.WriteLine($"## {title} — {pass}/{pairs.Count}");
                foreach (var f in fails) _log.WriteLine(f);
                _log.WriteLine("");
            }

            _log.WriteLine($"# TOTAL : {totalPass}/{totalPairs} ({(totalPairs > 0 ? 100.0 * totalPass / totalPairs : 0):F1}%)");
            Assert.True(totalPairs > 0, "aucune paire trouvée dans le corpus");
        }

        private static List<(string title, List<(string input, string expected)> pairs)>
            ParseCorpus(string[] lines)
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
                    if (trimmed.Length == 0) continue;
                    if (trimmed.StartsWith("---") || trimmed.StartsWith("===")) continue;
                    // Nouvelle catégorie
                    if (currentPairs.Count > 0 || currentTitle != "(sans titre)")
                    {
                        result.Add((currentTitle, currentPairs));
                        currentPairs = new List<(string, string)>();
                    }
                    currentTitle = trimmed;
                    continue;
                }
                // Séparateur " => " avec espaces autour pour ne pas matcher un "=>"
                // à l'intérieur de l'input (ex. "P => Q => ...").
                // On prend la DERNIÈRE occurrence : même si l'input contient "=>",
                // le dernier " => " avant la fin de ligne est le séparateur.
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
