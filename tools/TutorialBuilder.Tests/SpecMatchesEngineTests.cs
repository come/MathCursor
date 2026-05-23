using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MathCursor.Core;
using MathCursor.Core.Lattice;
using MathCursor.TutorialBuilder.Models;
using Xunit;

namespace MathCursor.TutorialBuilder.Tests;

/// <summary>
/// Verrou anti-stale du tutoriel : pour chaque item de chaque
/// <c>tutorial-spec.{lang}.json</c>, vérifie que le comportement de
/// <see cref="LatticeEngine.ConvertWithAmbiguity"/> matche ce qui est
/// promis à l'utilisateur (<c>expectedLatex</c> + présence de popup).
///
/// <para>Casser ce test signifie : soit une régression du parser, soit la
/// spec du tutoriel est en retard sur les règles. Pas de skip toléré —
/// la suite force l'arbitrage explicite.</para>
///
/// <para>Cf. ADR <c>2026-05-22-Feat-tutorial-docx-generated-onboarding</c>.</para>
/// </summary>
public sealed class SpecMatchesEngineTests
{
    private readonly LatticeEngine _engine = new();

    /// <summary>
    /// Convention de rendu mult par locale, alignée sur l'ADR matrix-pattern
    /// et les tests Core qui forcent ces valeurs (cf. AlternativeGeneratorTests).
    /// </summary>
    private static string MultSymbolFor(string lang) => lang switch
    {
        "en" => "\\cdot ",
        _ => "\\times ",
    };

    public static IEnumerable<object[]> AllItems()
    {
        foreach (var (lang, spec) in LoadAllSpecs())
        foreach (var section in spec.Sections)
        foreach (var item in section.Items)
        {
            yield return new object[] { lang, section.Id, item.Input, item };
        }
    }

    [Theory]
    [MemberData(nameof(AllItems))]
    public void Item_TopLatex_matches_expected(string lang, string sectionId, string input, TutorialItem item)
    {
        _ = sectionId;
        _ = input;
        var prev = LatexRenderer.GlobalOptions.MultSymbol;
        try
        {
            LatexRenderer.GlobalOptions.MultSymbol = MultSymbolFor(lang);
            var result = _engine.ConvertWithAmbiguity(item.Input);
            Assert.Equal(item.ExpectedLatex, result.TopLatex);
        }
        finally { LatexRenderer.GlobalOptions.MultSymbol = prev; }
    }

    [Theory]
    [MemberData(nameof(AllItems))]
    public void Item_popup_presence_matches_expected(string lang, string sectionId, string input, TutorialItem item)
    {
        _ = sectionId;
        _ = input;
        var prev = LatexRenderer.GlobalOptions.MultSymbol;
        try
        {
            LatexRenderer.GlobalOptions.MultSymbol = MultSymbolFor(lang);
            var result = _engine.ConvertWithAmbiguity(item.Input);
            Assert.Equal(item.ShowsPopup, result.Spot != null);
        }
        finally { LatexRenderer.GlobalOptions.MultSymbol = prev; }
    }

    [Theory]
    [MemberData(nameof(AllItems))]
    public void Item_popup_alt_is_among_alternatives(string lang, string sectionId, string input, TutorialItem item)
    {
        _ = sectionId;
        _ = input;
        if (item.PopupAlt is null) return;

        var prev = LatexRenderer.GlobalOptions.MultSymbol;
        try
        {
            LatexRenderer.GlobalOptions.MultSymbol = MultSymbolFor(lang);
            var result = _engine.ConvertWithAmbiguity(item.Input);
            Assert.NotNull(result.Spot);
            var alts = result.Spot!.Alternatives.Select(a => a.Latex).ToList();
            Assert.Contains(item.PopupAlt, alts);
        }
        finally { LatexRenderer.GlobalOptions.MultSymbol = prev; }
    }

    internal static IEnumerable<(string lang, TutorialSpec spec)> LoadAllSpecs()
    {
        var paths = Directory.GetFiles(AppContext.BaseDirectory, "tutorial-spec.*.json");
        if (paths.Length == 0)
            throw new FileNotFoundException(
                $"Aucune spec tutorial-spec.*.json trouvée. Vérifier <None Include> dans le csproj. Recherche : {AppContext.BaseDirectory}");
        foreach (var path in paths)
        {
            var spec = TutorialSpecLoader.Load(File.ReadAllText(path));
            yield return (spec.Lang, spec);
        }
    }
}
