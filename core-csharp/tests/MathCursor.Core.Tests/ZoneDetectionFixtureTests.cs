using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using MathCursor.Core.Tokenization;
using MathCursor.Core.ZoneDetection;
using Xunit;

namespace MathCursor.Core.Tests;

/// <summary>
/// Conformité cross-implémentations : le core C# doit produire les mêmes
/// résultats de détection de zone que le core TS du prototype.
/// Lit specs/test-fixtures/phase1-zone-detection.json (47 cas FR/EN/DE/ES).
/// </summary>
public class ZoneDetectionFixtureTests
{
    public sealed class FixtureCase
    {
        public string Id { get; set; } = "";
        public string Lang { get; set; } = "";
        public string Input { get; set; } = "";
        public string? ExpectedZone { get; set; }
        public string Description { get; set; } = "";
        public override string ToString() => $"{Id}: {Description}";
    }

    private sealed class FixtureFile
    {
        public FixtureCase[] Cases { get; set; } = Array.Empty<FixtureCase>();
    }

    private static FixtureFile LoadFixtures()
    {
        // Copié dans fixtures/ au build via MathCursor.Core.Tests.csproj
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "phase1-zone-detection.json");
        Assert.True(File.Exists(path), $"Fixture introuvable : {path}");

        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };
        var result = JsonSerializer.Deserialize<FixtureFile>(json, options);
        Assert.NotNull(result);
        return result!;
    }

    private static string? DetectZoneText(string input)
    {
        var tokens = Tokenizer.Tokenize(input);
        Scorer.ScoreAll((IList<Token>)tokens);
        var zone = ZoneDetector.Detect(tokens);
        return zone?.Normalized;
    }

    private static string NormalizeForComparison(string s) =>
        Regex.Replace(s, @"\s+", " ").Trim();

    public static IEnumerable<object[]> AllCases()
    {
        foreach (var c in LoadFixtures().Cases)
        {
            yield return new object[] { c };
        }
    }

    [Theory]
    [MemberData(nameof(AllCases))]
    public void ZoneDetection_Case(FixtureCase c)
    {
        var result = DetectZoneText(c.Input);

        if (c.ExpectedZone == null)
        {
            Assert.True(result == null,
                $"[{c.Id}] attendait null, obtenu \"{result}\" (input: \"{c.Input}\")");
        }
        else
        {
            Assert.NotNull(result);
            var actual = NormalizeForComparison(result!);
            var expected = NormalizeForComparison(c.ExpectedZone);
            Assert.True(actual == expected,
                $"[{c.Id}] attendait \"{expected}\", obtenu \"{actual}\" (input: \"{c.Input}\")");
        }
    }
}
