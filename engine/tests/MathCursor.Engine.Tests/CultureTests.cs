using System.Linq;
using MathCursor.Engine;
using Xunit;

namespace MathCursor.Engine.Tests;

// EngineCulture threadée en paramètre (ADR 2026-06-10-Feat-ribbon-columns-settings-culture) :
// FR (défaut) et US côte à côte sur le même process — pas d'état global, donc
// chaque cas vérifie les DEUX cultures pour verrouiller l'isolation.
public sealed class CultureTests
{
    private static string Top(string src, EngineCulture? culture = null)
        => ForestEngine.Analyze(src, culture).Ranked[0].Latex;

    // ── décimale ────────────────────────────────────────────────────────────

    [Fact]
    public void Decimal_Fr_DotAndCommaIn_CommaOut()
    {
        Assert.Equal("1{,}5", Top("1.5"));
        Assert.Equal("1{,}5", Top("1,5"));
    }

    [Fact]
    public void Decimal_Us_DotIn_DotOut()
    {
        Assert.Equal("1.5", Top("1.5", EngineCulture.Us));
    }

    [Fact]
    public void Decimal_Us_CommaIsNotADecimal()
    {
        // en US la virgule reste un séparateur : « 1,5 » ne produit JAMAIS le nombre 1.5
        var cands = ForestEngine.Analyze("1,5", EngineCulture.Us).Ranked.Select(c => c.Latex);
        Assert.DoesNotContain("1.5", cands);
    }

    // ── intervalle ──────────────────────────────────────────────────────────

    [Fact]
    public void Interval_Fr_Semicolon()
    {
        Assert.Equal("[0;1]", Top("[0;1]"));
    }

    [Fact]
    public void Interval_Us_Comma()
    {
        Assert.Equal("[0,1]", Top("[0,1]", EngineCulture.Us));
    }

    // ── matrice ─────────────────────────────────────────────────────────────

    [Fact]
    public void Matrix_Fr_Pmatrix()
    {
        var top = ForestEngine.Analyze("(1,2;3,4)").Ranked.Select(c => c.Latex);
        Assert.Contains(top, l => l.Contains("\\begin{pmatrix}") && l.Contains("\\end{pmatrix}"));
    }

    [Fact]
    public void Matrix_Us_Bmatrix()
    {
        var top = ForestEngine.Analyze("(1,2;3,4)", EngineCulture.Us).Ranked.Select(c => c.Latex);
        Assert.Contains(top, l => l.Contains("\\begin{bmatrix}") && l.Contains("\\end{bmatrix}"));
    }
}
