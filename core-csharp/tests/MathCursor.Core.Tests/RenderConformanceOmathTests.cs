using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Core.Tests
{
    /// <summary>
    /// Conformance OMath : pour chaque paire input → latex du corpus extrait
    /// (corpus/yaml-gold-extracted.txt + corpus lycée/seconde), on passe la
    /// sortie LaTeX par <see cref="LatexToUnicodeMath.Convert"/> et on assure
    /// qu'il ne reste AUCUNE macro LaTeX (\xxx) hors d'une whitelist de
    /// commandes que Word UnicodeMath consomme nativement. Toute fuite =
    /// texte LaTeX brut côté Word, UX cassée.
    ///
    /// Source : corpus extrait (indépendant du moteur), donc ce test reste
    /// valide après bascule PatternEngine → lattice.
    /// </summary>
    public sealed class RenderConformanceOmathTests
    {
        private readonly ITestOutputHelper _log;
        public RenderConformanceOmathTests(ITestOutputHelper log) { _log = log; }

        // Commandes LaTeX que Word UnicodeMath reconnaît nativement et qu'on
        // laisse donc passer. Whitelist volontairement courte.
        private static readonly HashSet<string> AllowedResidualMacros = new HashSet<string>
        {
            "vec", "hat", "bar", "tilde", "dot", "ddot",
            "widehat", "widetilde", "overline", "underline",
        };

        private static readonly Regex LatexMacroRegex =
            new Regex(@"\\([a-zA-Z]+)", RegexOptions.Compiled);

        public static TheoryData<string, string> AllGoldOutputs()
        {
            var data = new TheoryData<string, string>();
            var corpusFile = Path.Combine(
                AppContext.BaseDirectory, "corpus", "yaml-gold-extracted.txt");
            if (!File.Exists(corpusFile)) return data; // pas extrait → vide
            foreach (var line in File.ReadAllLines(corpusFile))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                int idx = line.LastIndexOf(" => ", StringComparison.Ordinal);
                if (idx < 0) continue;
                var input = line.Substring(0, idx).Trim();
                var latex = line.Substring(idx + 4).Trim();
                if (input.Length > 0 && latex.Length > 0)
                    data.Add(input, latex);
            }
            return data;
        }

        [Theory]
        [MemberData(nameof(AllGoldOutputs))]
        public void Gold_latex_converts_to_unicodemath_without_residual_macros(
            string input, string latex)
        {
            string unicodeMath = LatexToUnicodeMath.Convert(latex);
            var residuals = LatexMacroRegex.Matches(unicodeMath)
                .Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .Where(name => !AllowedResidualMacros.Contains(name))
                .Distinct()
                .ToList();

            if (residuals.Count > 0)
            {
                _log.WriteLine($"input=\"{input}\"");
                _log.WriteLine($"  latex     : {latex}");
                _log.WriteLine($"  unicodemath: {unicodeMath}");
                _log.WriteLine($"  RESIDUAL MACROS (would leak as raw LaTeX in Word): {string.Join(", ", residuals.Select(r => "\\" + r))}");
            }
            Assert.Empty(residuals);
        }
    }
}
