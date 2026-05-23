using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MathCursor.TutorialBuilder.Models;
using Xunit;

namespace MathCursor.TutorialBuilder.Tests;

/// <summary>
/// Round-trip et structure de base des specs — détecte une régression du
/// schéma JSON avant que les tests engine ne tournent. Couvre toutes les
/// langues présentes dans le dossier.
/// </summary>
public sealed class TutorialSpecLoaderTests
{
    public static IEnumerable<object[]> AllSpecPaths()
    {
        foreach (var path in Directory.GetFiles(System.AppContext.BaseDirectory, "tutorial-spec.*.json"))
        {
            yield return new object[] { path };
        }
    }

    [Theory]
    [MemberData(nameof(AllSpecPaths))]
    public void Spec_loads_with_at_least_one_section_with_items(string path)
    {
        var spec = TutorialSpecLoader.Load(File.ReadAllText(path));
        Assert.NotEmpty(spec.Sections);
        // Au moins une section a des items (les sections "meta" type intro
        // popup peuvent avoir items vides — elles sont juste explicatives).
        Assert.Contains(spec.Sections, s => s.Items.Count > 0);
    }

    [Theory]
    [MemberData(nameof(AllSpecPaths))]
    public void Spec_metadata_is_present(string path)
    {
        var spec = TutorialSpecLoader.Load(File.ReadAllText(path));
        Assert.False(string.IsNullOrWhiteSpace(spec.Version));
        Assert.False(string.IsNullOrWhiteSpace(spec.Lang));
        Assert.False(string.IsNullOrWhiteSpace(spec.Title));
        Assert.False(string.IsNullOrWhiteSpace(spec.Intro));
    }

    [Theory]
    [MemberData(nameof(AllSpecPaths))]
    public void Spec_filename_lang_matches_metadata_lang(string path)
    {
        // tutorial-spec.fr.json → "fr"
        var name = Path.GetFileNameWithoutExtension(path);
        var lang = name.Split('.').Last();
        var spec = TutorialSpecLoader.Load(File.ReadAllText(path));
        Assert.Equal(lang, spec.Lang);
    }

    [Theory]
    [MemberData(nameof(AllSpecPaths))]
    public void Item_required_fields_are_present(string path)
    {
        var spec = TutorialSpecLoader.Load(File.ReadAllText(path));
        foreach (var section in spec.Sections)
        foreach (var item in section.Items)
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Instruction), $"section={section.Id}, input={item.Input}");
            Assert.False(string.IsNullOrWhiteSpace(item.Input), $"section={section.Id}");
            Assert.False(string.IsNullOrWhiteSpace(item.ExpectedLatex), $"section={section.Id}, input={item.Input}");
        }
    }

    [Theory]
    [MemberData(nameof(AllSpecPaths))]
    public void PopupAlt_only_present_when_showsPopup_true(string path)
    {
        var spec = TutorialSpecLoader.Load(File.ReadAllText(path));
        foreach (var section in spec.Sections)
        foreach (var item in section.Items)
        {
            if (item.PopupAlt != null)
            {
                Assert.True(item.ShowsPopup,
                    $"section={section.Id}, input={item.Input} a popupAlt mais showsPopup=false");
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllSpecPaths))]
    public void Round_trip_preserves_structure(string path)
    {
        var spec = TutorialSpecLoader.Load(File.ReadAllText(path));
        var json = JsonSerializer.Serialize(spec, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        var reloaded = TutorialSpecLoader.Load(json);
        Assert.Equal(spec.Sections.Count, reloaded.Sections.Count);
        for (var i = 0; i < spec.Sections.Count; i++)
        {
            Assert.Equal(spec.Sections[i].Items.Count, reloaded.Sections[i].Items.Count);
        }
    }
}
