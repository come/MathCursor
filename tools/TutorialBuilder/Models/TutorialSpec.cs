using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MathCursor.TutorialBuilder.Models;

public sealed record TutorialSpec(
    string Version,
    string Lang,
    string Title,
    string Intro,
    IReadOnlyList<TutorialSection> Sections);

public sealed record TutorialSection(
    string Id,
    string Title,
    string Intro,
    IReadOnlyList<TutorialItem> Items,
    string? Note = null);   // astuce affichée SOUS le tableau d'exercices (optionnelle)

public sealed record TutorialItem(
    string Instruction,
    string Input,
    string ExpectedLatex,
    bool ShowsPopup,
    string? PopupAlt);

public static class TutorialSpecLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static TutorialSpec Load(string json)
    {
        var spec = JsonSerializer.Deserialize<TutorialSpec>(json, Options)
            ?? throw new System.InvalidOperationException("tutorial-spec.json est vide ou mal formé");
        return spec;
    }
}
