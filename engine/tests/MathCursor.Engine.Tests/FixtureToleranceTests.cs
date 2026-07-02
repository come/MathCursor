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
using Xunit;

namespace MathCursor.Engine.Tests;

// Rejoue les 280 fixtures sous les TOLÉRANCES du lexer (ADR
// 2026-06-10-Fix-nbsp-keyword-case-tolerance) : chaque mutation d'entrée que
// Word AutoCorrect peut produire doit donner EXACTEMENT le résultat attendu
// de la fixture d'origine (décision + candidats + note).
public sealed class FixtureToleranceTests
{
    private sealed class Fixture
    {
        [JsonPropertyName("in")] public string In { get; set; } = "";
        [JsonPropertyName("culture")] public string? Culture { get; set; }
        [JsonPropertyName("decision")] public string Decision { get; set; } = "";
        [JsonPropertyName("cands")] public List<string> Cands { get; set; } = new();
        [JsonPropertyName("note")] public bool Note { get; set; }

        public EngineCulture Engine => Culture == "us" ? EngineCulture.Us : EngineCulture.Fr;
    }

    private static List<Fixture> Load()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures.json"));
        return JsonSerializer.Deserialize<List<Fixture>>(json) ?? new();
    }

    private static string J(IEnumerable<string> xs) => "[" + string.Join(", ", xs.Select(x => "\"" + x + "\"")) + "]";

    private static void RunAll(IEnumerable<(Fixture F, string Mutated)> cases, string label)
    {
        var fails = new List<string>();
        int total = 0;
        foreach (var (f, mutated) in cases)
        {
            total++;
            string decision; List<string> cands; bool note;
            try
            {
                var r = ForestEngine.Analyze(mutated, f.Engine);
                decision = r.Decision;
                cands = r.Ranked.Select(x => x.Latex).ToList();
                note = r.HasNote;
            }
            catch (Exception e)
            {
                fails.Add($"✗ \"{mutated}\" ({label} de \"{f.In}\")\n    attendu : {f.Decision}  {J(f.Cands)}\n    obtenu  : throw ({e.Message})");
                continue;
            }
            if (decision != f.Decision || !cands.SequenceEqual(f.Cands) || note != f.Note)
                fails.Add($"✗ \"{mutated}\" ({label} de \"{f.In}\")\n    attendu : {f.Decision}  {J(f.Cands)}\n    obtenu  : {decision}  {J(cands)}");
        }
        Assert.True(fails.Count == 0,
            $"{total - fails.Count}/{total} OK ({label}) — {fails.Count} échec(s) :\n" + string.Join("\n", fails.Take(30)));
        Assert.True(total > 0, $"aucun cas généré pour {label}");
    }

    // ── NBSP : remplacer TOUS les espaces par U+00A0 ne change RIEN ─────────

    [Fact]
    public void ToutesLesFixtures_NbspEquivalentEspace()
    {
        RunAll(Load().Where(f => f.In.Contains(' '))
                     .Select(f => (f, f.In.Replace(' ', '\u00A0'))), "NBSP");
    }

    [Fact]
    public void ToutesLesFixtures_NbspFineEquivalentEspace()
    {
        RunAll(Load().Where(f => f.In.Contains(' '))
                     .Select(f => (f, f.In.Replace(' ', '\u202F'))), "NNBSP");
    }

    // ── capitale de début de phrase (Word AutoCorrect) ──────────────────────
    // Seulement quand le 1er mot est un MOT-OPÉRATEUR (Shape non-null,
    // non-atome) en minuscules : c'est la garantie du repli de casse. Les
    // atomes (pi → Pi = \Pi) et les unités gardent leur casse signifiante.

    [Fact]
    public void ToutesLesFixtures_CapitaleDebutDePhrase_MemeResultat()
    {
        var cases = new List<(Fixture, string)>();
        foreach (var f in Load())
        {
            int n = 0;
            while (n < f.In.Length && f.In[n] >= 'a' && f.In[n] <= 'z') n++;
            if (n < 2) continue; // pas un mot ou 1 lettre
            string word = f.In.Substring(0, n);
            string key = f.Engine.Canon(word);
            if (!Vocabulary.Vocab.TryGetValue(key, out var e)) continue;
            if (e.Shape == null || e.Shape == "atom") continue;
            cases.Add((f, char.ToUpperInvariant(f.In[0]) + f.In.Substring(1)));
        }
        RunAll(cases, "capitale");
    }
}
