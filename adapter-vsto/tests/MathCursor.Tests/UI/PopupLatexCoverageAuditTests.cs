// MathCursor — capture d'intention mathématique depuis une saisie clavier linéaire.
// Copyright (C) 2026  Côme de Percin
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MathCursor.UI;
using XamlMath;
using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Tests.UI
{
    /// <summary>
    /// Audit de couverture du rendu POPUP (même méthode que l'audit OMML de
    /// Phase 1c, cf. OmmlCoverageTests côté serialization) : chaque LaTeX
    /// produit par le moteur forest sur les 280 fixtures doit traverser le
    /// pipeline popup — <see cref="MixedLatexRenderer.Tokenize"/> (segments
    /// Unicode/WpfMath) puis <see cref="WpfMathAdapter.Adapt"/> puis le
    /// parseur WpfMath — SANS exception de parsing.
    ///
    /// Limite assumée : « parse OK » ne garantit pas un glyphe correct
    /// (boîte vide silencieuse possible) — pour le visuel, voir les
    /// render-probes PNG (WpfMathRenderProbeTests). Cet audit attrape la
    /// classe d'erreurs la plus fréquente : macro inconnue → exception →
    /// fallback TextBlock brut (LaTeX affiché tel quel dans la popup).
    /// </summary>
    public class PopupLatexCoverageAuditTests
    {
        private readonly ITestOutputHelper _output;
        public PopupLatexCoverageAuditTests(ITestOutputHelper output) { _output = output; }

        // ── fixtures.json : même snapshot que les tests moteur ──────────

        private static List<string> LoadFixtureInputs()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "fixtures.json");
            var json = File.ReadAllText(path);
            // Extraction minimaliste des "in": "..." (cohérent avec la
            // convention JSON-plat-sans-lib du projet).
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
                            // \uXXXX (fixtures NBSP/NNBSP de tolerance lexer)
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

        [Fact]
        public void EveryEngineLatexSurvivesPopupPipeline()
        {
            var inputs = LoadFixtureInputs();
            // garde anti-troncature ; le compte exact vit dans FixtureTests (moteur).
            Assert.True(inputs.Count >= 310);

            // 1. Tous les candidats LaTeX émis par le moteur (dédupliqués).
            //    Chaque entrée est analysée en FR ET en US (plus simple que de
            //    parser le champ "culture", et couvre un superset de candidats).
            var candidates = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var input in inputs)
                foreach (var culture in new[] { MathCursor.Engine.EngineCulture.Fr, MathCursor.Engine.EngineCulture.Us })
                {
                    MathCursor.Engine.AnalyzeResult r;
                    try { r = MathCursor.Engine.ForestEngine.Analyze(input, culture); }
                    catch { continue; } // cas "erreur" du moteur : pas de popup
                    foreach (var c in r.Ranked)
                        if (seen.Add(c.Latex)) candidates.Add(c.Latex);
                }
            _output.WriteLine($"{candidates.Count} candidats LaTeX distincts");
            Assert.True(candidates.Count > 200, "le moteur devrait produire >200 candidats distincts");

            // 2. Pipeline popup sous STA (WpfMath touche aux ressources WPF).
            var failures = StaRunner.Run(() =>
            {
                var fails = new List<string>();
                var parser = WpfMath.Parsers.WpfTeXFormulaParser.Instance;
                foreach (var latex in candidates)
                {
                    foreach (var seg in MixedLatexRenderer.Tokenize(latex))
                    {
                        if (seg.Type != MixedLatexRenderer.SegmentType.WpfMath) continue;
                        if (string.IsNullOrWhiteSpace(seg.Content)) continue;
                        var adapted = WpfMathAdapter.Adapt(seg.Content);
                        try { parser.Parse(adapted); }
                        catch (Exception ex)
                        {
                            fails.Add($"{latex}\n    segment : {seg.Content}\n    adapté  : {adapted}\n    → {ex.GetType().Name}: {FirstLine(ex.Message)}");
                        }
                    }
                }
                return fails;
            });

            foreach (var f in failures) _output.WriteLine("✗ " + f);
            Assert.True(failures.Count == 0,
                $"{failures.Count}/{candidates.Count} candidats échouent le rendu popup :\n"
                + string.Join("\n", failures.Take(40)));
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int nl = s.IndexOf('\n');
            return nl < 0 ? s : s.Substring(0, nl).TrimEnd('\r');
        }
    }
}
