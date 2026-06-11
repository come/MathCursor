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
        var specPath = Path.Combine(RepoRoot(), "tools", "TutorialBuilder", "tutorial-spec.fr.json");
        Assert.True(File.Exists(specPath), "spec tutoriel introuvable : " + specPath);
        using var spec = JsonDocument.Parse(File.ReadAllText(specPath));

        using var fixtures = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures.json")));
        // index (entrée FR → fixture)
        var byInput = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var f in fixtures.RootElement.EnumerateArray())
            if (!f.TryGetProperty("culture", out var cu) || cu.GetString() == "fr")
                byInput[f.GetProperty("in").GetString()!] = f;

        var fails = new List<string>();
        foreach (var section in spec.RootElement.GetProperty("sections").EnumerateArray())
            foreach (var item in section.GetProperty("items").EnumerateArray())
            {
                string input = item.GetProperty("input").GetString()!;
                string expected = item.GetProperty("expectedLatex").GetString()!;
                bool showsPopup = item.GetProperty("showsPopup").GetBoolean();
                string? alt = item.TryGetProperty("popupAlt", out var a) ? a.GetString() : null;

                if (!byInput.TryGetValue(input, out var fx))
                { fails.Add($"\"{input}\" : ABSENTE du corpus fixtures.json"); continue; }

                var cands = fx.GetProperty("cands").EnumerateArray().Select(c => c.GetString()).ToList();
                string decision = fx.GetProperty("decision").GetString()!;

                if ((decision == "popup") != showsPopup)
                    fails.Add($"\"{input}\" : showsPopup={showsPopup} mais décision moteur = {decision}");
                if (cands.Count == 0 || cands[0] != expected)
                    fails.Add($"\"{input}\" : expectedLatex \"{expected}\" ≠ top moteur \"{(cands.Count > 0 ? cands[0] : "(aucun)")}\"");
                if (alt != null && !cands.Contains(alt))
                    fails.Add($"\"{input}\" : popupAlt \"{alt}\" absent des candidats [{string.Join(" ; ", cands)}]");
            }

        Assert.True(fails.Count == 0,
            $"{fails.Count} consigne(s) du tuto périmée(s) :\n" + string.Join("\n", fails));
    }
}
