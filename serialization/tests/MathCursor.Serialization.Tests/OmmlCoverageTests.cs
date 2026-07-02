// MathCursor: capturing mathematical intent from linear keyboard input.
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
using System.Text.Json;
using System.Text.Json.Serialization;
using MathCursor.Engine;
using MathCursor.Serialization;
using Xunit;

namespace MathCursor.Serialization.Tests;

// Pont moteur → Word : chaque LaTeX produit par le moteur (sur les 280 fixtures) doit
// se convertir en OMML structurellement valide, SANS fuite de commande LaTeX.
public sealed class OmmlCoverageTests
{
    private sealed class Fixture
    {
        [JsonPropertyName("in")] public string In { get; set; } = "";
        [JsonPropertyName("culture")] public string Culture { get; set; }
        [JsonPropertyName("cands")] public List<string> Cands { get; set; } = new();
    }

    private static List<Fixture> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures.json");
        return JsonSerializer.Deserialize<List<Fixture>>(File.ReadAllText(path)) ?? new();
    }

    // Restes de commandes LaTeX qui ne devraient JAMAIS apparaître dans l'OMML.
    private static readonly string[] Leaks =
    {
        "binom", "begin", "end{", "mathbin", "square", "langle", "rangle",
        "lfloor", "rfloor", "lceil", "rceil", "bmod", "Vert", "colon", "nexists",
    };

    [Fact]
    public void EveryEngineLatexConvertsCleanly()
    {
        var fixtures = Load();
        // garde anti-troncature ; le compte exact vit dans FixtureTests (moteur).
        Assert.True(fixtures.Count >= 310);

        var fails = new List<string>();
        int n = 0;
        foreach (var f in fixtures)
        {
            var r = ForestEngine.Analyze(f.In, f.Culture == "us" ? EngineCulture.Us : null);
            foreach (var cand in r.Ranked)
            {
                n++;
                string omml;
                try { omml = LatexToOmml.ConvertToString(cand.Latex); }
                catch (Exception e) { fails.Add($"\"{f.In}\" :: {cand.Latex} → THROW {e.Message}"); continue; }

                if (omml.Contains('\\'))
                    fails.Add($"\"{f.In}\" :: {cand.Latex} → backslash résiduel dans l'OMML");
                foreach (var leak in Leaks)
                    if (omml.Contains(leak))
                        fails.Add($"\"{f.In}\" :: {cand.Latex} → fuite « {leak} » : {omml}");
                // Base de script VIDE (<m:sSup><m:e/>…) : Word affiche sa
                // boîte de saisie carrée — bug « carrés » \mathrm{cm}^{3}
                // du 2026-06-10. Une base vide n'est JAMAIS légitime ici.
                if (HasEmptyScriptBase(cand.Latex))
                    fails.Add($"\"{f.In}\" :: {cand.Latex} → base de script VIDE (carré Word)");
            }
        }

        Assert.True(fails.Count == 0,
            $"{n} candidats testés — {fails.Count} échec(s) :\n" + string.Join("\n", fails.Take(40)));
    }

    private static bool HasEmptyScriptBase(string latex)
    {
        var el = LatexToOmml.Convert(latex);
        var m = LatexToOmml.M;
        foreach (var name in new[] { "sSup", "sSub", "sSubSup" })
            foreach (var script in el.Descendants(m + name))
            {
                var e = script.Element(m + "e");
                if (e == null || !e.HasElements) return true;
            }
        return false;
    }

    [Theory]
    // golden : les nouveaux handlers produisent la bonne structure OMML.
    [InlineData("\\binom{n}{k}", "noBar")]
    [InlineData("\\begin{pmatrix}1 & 2 \\\\ 3 & 4\\end{pmatrix}", "<m>")]
    [InlineData("\\begin{bmatrix}1 & 2 \\\\ 3 & 4\\end{bmatrix}", "<m>")]
    [InlineData("\\begin{bmatrix}1 & 2 \\\\ 3 & 4\\end{bmatrix}", "[")]
    [InlineData("\\lfloor x\\rfloor ", "⌊")]
    [InlineData("\\lceil x\\rceil ", "⌈")]
    [InlineData("a\\mid b", "∣")]
    [InlineData("a\\not\\subset b", "⊄")]
    [InlineData("\\left\\|x\\right\\|", "‖")]
    [InlineData("\\langle u,v\\rangle ", "⟨")]
    [InlineData("AB \\mathbin{/\\!/} CD", "//")]
    [InlineData("\\frac{1}{x+1}", "<f>")]
    [InlineData("1\\mathrm{cm}^{3}+1\\mathrm{cm}^{3}=3\\mathrm{cm}^{4}", "cm")]
    // « 1 cm » tapé AVEC espace : l'espace fine \, doit survivre jusqu'à
    // l'OMML (U+2009) — le code hérité l'avalait (bug user 2026-06-10).
    [InlineData("1\\,\\mathrm{cm}^{3}", "1 ")]
    public void GoldenStructure(string latex, string expectedFragment)
    {
        var omml = LatexToOmml.ConvertToString(latex);
        Assert.Contains(expectedFragment, omml);
        Assert.DoesNotContain('\\', omml);
    }
}
