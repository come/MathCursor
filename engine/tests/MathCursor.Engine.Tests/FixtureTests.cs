using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MathCursor.Engine;
using Xunit;

namespace MathCursor.Engine.Tests;

// Rejoue forest/test.mjs : analyze() sur chaque fixture, compare décision + candidats
// (LaTeX, ordonnés) + présence de note. fixtures.json = SNAPSHOT généré depuis la
// source JS (baseline 280/280, CONTRAT DE FIDÉLITÉ du portage) + les extensions
// C# post-portage (champ optionnel "culture": "us" — défaut FR).
//
// POLITIQUE (2026-06-10) : tout nouveau comportement à sortie déterministe
// s'ajoute ICI (entrée → décision + candidats exacts), pas en [Fact] dédié —
// il est alors rejoué automatiquement par les 3 pipelines (moteur, OMML,
// popup) ET par les mutations de FixtureToleranceTests. Les [Fact] codés
// restent réservés aux PROPRIÉTÉS (équivalences, mutations de corpus).
public sealed class FixtureTests
{
    private sealed class Fixture
    {
        [JsonPropertyName("in")] public string In { get; set; } = "";
        [JsonPropertyName("culture")] public string? Culture { get; set; }
        [JsonPropertyName("decision")] public string Decision { get; set; } = "";
        [JsonPropertyName("cands")] public List<string> Cands { get; set; } = new();
        [JsonPropertyName("note")] public bool Note { get; set; }
    }

    private static List<Fixture> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<Fixture>>(json) ?? new();
    }

    private static string J(IEnumerable<string> xs) => "[" + string.Join(", ", xs.Select(x => "\"" + x + "\"")) + "]";

    [Fact]
    public void AllFixturesMatch()
    {
        var fixtures = Load();
        Assert.Equal(449, fixtures.Count);

        var fails = new List<string>();
        int pass = 0;

        foreach (var f in fixtures)
        {
            string decision; List<string> cands; bool note;
            try
            {
                var r = ForestEngine.Analyze(f.In, f.Culture == "us" ? EngineCulture.Us : null);
                decision = r.Decision;
                cands = r.Ranked.Select(x => x.Latex).ToList();
                note = r.HasNote;
            }
            catch (Exception e)
            {
                fails.Add($"✗ \"{f.In}\"\n    attendu : {f.Decision}  {J(f.Cands)}\n    obtenu  : throw ({e.Message})");
                continue;
            }

            if (decision == f.Decision && cands.SequenceEqual(f.Cands) && note == f.Note) pass++;
            else
                fails.Add($"✗ \"{f.In}\"\n    attendu : {f.Decision}  {J(f.Cands)}{(f.Note ? "  +note" : "")}\n    obtenu  : {decision}  {J(cands)}{(note ? "  +note" : "")}");
        }

        Assert.True(fails.Count == 0,
            $"{pass}/{fixtures.Count} OK — {fails.Count} échec(s) :\n" + string.Join("\n", fails.Take(50)));
    }
}
