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
