using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MathCursor.TutorialBuilder.Models;

public sealed record TutorialSpec(
    string Version,
    string Lang,
    string Title,
    string Intro,
    IReadOnlyList<TutorialSection> Sections,
    string? TryHere = null);   // libellé localisé de la cellule d'essai (défaut FR)

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
    string? PopupAlt,
    string? Tip = null,    // ligne d'astuce affichée SOUS la consigne (display only,
                           // non validée contre le moteur — ex. « ou : @p »)
    string? Note = null);  // 2e ligne grise SOUS la consigne (après le Tip) — display
                           // only — ex. « (note : Ctrl+Espace pour ouvrir la popup…) »

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
