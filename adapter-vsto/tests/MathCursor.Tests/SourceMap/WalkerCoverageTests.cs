using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using MathCursor.Engine;
using MathCursor.Host.SourceMap;
using Xunit;

namespace MathCursor.Tests.SourceMap
{
    /// <summary>
    /// VERROU du « pas de fallback » (amendement 2026-06-12 de l'ADR
    /// hash-source-map) : l'insertion n'a plus de repli InsertXML, donc TOUT
    /// candidat LaTeX que le moteur peut proposer doit être constructible par
    /// le walker. Le test rejoue les entrées du corpus fixtures dans le
    /// MOTEUR RÉEL (mêmes candidats que la popup en prod), puis chaque
    /// candidat → <c>LatexToOmml</c> → <c>OmmlWalkerWhitelist.IsSupported</c>.
    /// Un seul refus = rouge — le walker doit apprendre la construction AVANT
    /// que le moteur ne l'émette chez l'élève.
    /// </summary>
    public class WalkerCoverageTests
    {
        private const string M = "http://schemas.openxmlformats.org/officeDocument/2006/math";

        [Fact]
        public void TousLesCandidatsDuMoteur_SontConstructiblesParLeWalker()
        {
            var fails = new List<string>();
            var seen = new HashSet<string>();
            int checkedCount = 0;

            foreach (var input in LoadFixtureInputs())
            {
                List<string> cands;
                try { cands = ForestEngine.Analyze(input).Ranked.Select(r => r.Latex).ToList(); }
                catch (Exception) { continue; }   // entrées erreur : pas de candidat

                foreach (var latex in cands)
                {
                    if (string.IsNullOrEmpty(latex) || !seen.Add(latex)) continue;
                    XElement oMath;
                    try { oMath = MathCursor.Serialization.LatexToOmml.Convert(latex); }
                    catch (Exception ex) { fails.Add($"✗ \"{latex}\" — LatexToOmml: {ex.Message}"); continue; }
                    checkedCount++;
                    if (!OmmlWalkerWhitelist.IsSupported(oMath, out string why))
                        fails.Add($"✗ \"{latex}\" — {why}");
                }
            }

            Assert.True(fails.Count == 0,
                $"{checkedCount - fails.Count}/{checkedCount} candidats constructibles — refus :\n"
                + string.Join("\n", fails.Take(40)));
            Assert.True(checkedCount > 300, $"corpus suspect : {checkedCount} candidats seulement");
        }

        [Fact]
        public void EqArrDesBlocs_EstConstructible()
        {
            // Les blocs multilignes (ChainComposer) émettent un eqArr avec
            // marques d'alignement & — hors corpus fixtures, vérifié à part.
            XNamespace m = M;
            var oMath = new XElement(m + "oMath",
                new XElement(m + "eqArr",
                    new XElement(m + "e", Run("&x"), Run("&=1")),
                    new XElement(m + "e", Run("⟺&x"), Run("&=2"))));
            Assert.True(OmmlWalkerWhitelist.IsSupported(oMath, out string why), why);
        }

        [Fact]
        public void BorderBox_EstConstructible()
        {
            // \boxed{x=2} → m:borderBox/m:e (cadre 4 bords, pas de borderBoxPr).
            XNamespace m = M;
            var oMath = new XElement(m + "oMath",
                new XElement(m + "borderBox",
                    new XElement(m + "e", Run("x"), Run("="), Run("2"))));
            Assert.True(OmmlWalkerWhitelist.IsSupported(oMath, out string why), why);
        }

        [Fact]
        public void BorderBoxPr_EstRefuse()
        {
            // On n'émet jamais de borderBoxPr : sa présence = arbre non prévu.
            XNamespace m = M;
            var oMath = new XElement(m + "oMath",
                new XElement(m + "borderBox",
                    new XElement(m + "borderBoxPr"),
                    new XElement(m + "e", Run("x"))));
            Assert.False(OmmlWalkerWhitelist.IsSupported(oMath, out _));
        }

        private static XElement Run(string t)
        {
            XNamespace m = M;
            return new XElement(m + "r", new XElement(m + "t",
                new XAttribute(XNamespace.Xml + "space", "preserve"), t));
        }

        // fixtures.json : même snapshot que les tests moteur ; extraction
        // minimaliste des "in" (convention JSON-plat-sans-lib du projet,
        // identique à PopupLatexCoverageAuditTests).
        private static List<string> LoadFixtureInputs()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "fixtures.json");
            var json = File.ReadAllText(path);
            var inputs = new List<string>();
            int i = 0;
            const string needle = "\"in\":";
            while ((i = json.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
            {
                i += needle.Length;
                while (i < json.Length && json[i] == ' ') i++;
                if (json[i] != '"') continue;
                i++;
                var sb = new System.Text.StringBuilder();
                while (i < json.Length && json[i] != '"')
                {
                    if (json[i] == '\\' && i + 1 < json.Length)
                    {
                        char n = json[i + 1];
                        if (n == 'u' && i + 5 < json.Length)
                        {
                            sb.Append((char)Convert.ToInt32(json.Substring(i + 2, 4), 16));
                            i += 6;
                        }
                        else
                        {
                            sb.Append(n == 'n' ? '\n' : n == 't' ? '\t' : n == 'r' ? '\r' : n);
                            i += 2;
                        }
                    }
                    else sb.Append(json[i++]);
                }
                inputs.Add(sb.ToString());
            }
            return inputs;
        }
    }
}
