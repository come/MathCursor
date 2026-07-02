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
using Xunit;

namespace MathCursor.Engine.Tests;

// ANTI-PÉREMPTION du tutoriel (tools/TutorialBuilder/tutorial-spec.fr.json) :
// chaque consigne du tuto DOIT être une fixture exacte du corpus — entrée,
// décision (auto/popup), top et alternative. Si le moteur évolue et qu'une
// fixture est régénérée, ce test casse → le tuto se met à jour AVANT de
// mentir aux élèves. (Politique : le tuto est DÉRIVÉ des fixtures, triées
// par section mathématiques — demande utilisateur 2026-06-10.)
public sealed class TutorialSpecTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MathCursor.sln")))
            dir = dir.Parent;
        Assert.True(dir != null, "MathCursor.sln introuvable en remontant depuis " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    [Fact]
    public void ChaqueConsigneDuTuto_EstUneFixtureExacte()
    {
        // index (entrée → fixture) : culture FR ou générique (les entrées EN du
        // tuto — sum, sqrt, in, forall… — sont génériques dans le corpus).
        using var fixtures = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures.json")));
        var byInput = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var f in fixtures.RootElement.EnumerateArray())
            if (!f.TryGetProperty("culture", out var cu) || cu.GetString() == "fr")
                byInput[f.GetProperty("in").GetString()!] = f;

        // TOUS les specs présents (fr + en) sont validés : un tuto qui ment au
        // lecteur casse le build, quelle que soit la langue.
        var specDir = Path.Combine(RepoRoot(), "tools", "TutorialBuilder");
        var specs = Directory.GetFiles(specDir, "tutorial-spec.*.json");
        Assert.True(specs.Length >= 1, "aucun spec tutoriel dans " + specDir);

        var fails = new List<string>();
        foreach (var specPath in specs)
        {
            string lang = Path.GetFileName(specPath);
            using var spec = JsonDocument.Parse(File.ReadAllText(specPath));
            foreach (var section in spec.RootElement.GetProperty("sections").EnumerateArray())
                foreach (var item in section.GetProperty("items").EnumerateArray())
                {
                    string input = item.GetProperty("input").GetString()!;
                    string expected = item.GetProperty("expectedLatex").GetString()!;
                    bool showsPopup = item.GetProperty("showsPopup").GetBoolean();
                    string? alt = item.TryGetProperty("popupAlt", out var a) ? a.GetString() : null;

                    if (!byInput.TryGetValue(input, out var fx))
                    { fails.Add($"[{lang}] \"{input}\" : ABSENTE du corpus fixtures.json"); continue; }

                    var cands = fx.GetProperty("cands").EnumerateArray().Select(c => c.GetString()).ToList();
                    string decision = fx.GetProperty("decision").GetString()!;

                    if ((decision == "popup") != showsPopup)
                        fails.Add($"[{lang}] \"{input}\" : showsPopup={showsPopup} mais décision moteur = {decision}");
                    if (cands.Count == 0 || cands[0] != expected)
                        fails.Add($"[{lang}] \"{input}\" : expectedLatex \"{expected}\" ≠ top moteur \"{(cands.Count > 0 ? cands[0] : "(aucun)")}\"");
                    if (alt != null && !cands.Contains(alt))
                        fails.Add($"[{lang}] \"{input}\" : popupAlt \"{alt}\" absent des candidats [{string.Join(" ; ", cands)}]");
                }
        }

        Assert.True(fails.Count == 0,
            $"{fails.Count} consigne(s) du tuto périmée(s) :\n" + string.Join("\n", fails));
    }
}
