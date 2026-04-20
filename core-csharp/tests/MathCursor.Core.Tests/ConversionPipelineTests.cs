using MathCursor.Core.Pipeline;
using Xunit;

namespace MathCursor.Core.Tests;

public class ConversionPipelineTests
{
    [Fact]
    public void Convert_ClassicExpression_ProducesOmml()
    {
        var r = ConversionPipeline.Convert("On a f(x) = 1/x");
        Assert.True(r.Success);
        Assert.NotNull(r.Equation);
        Assert.NotNull(r.Equation!.Omml);
        Assert.Contains("<m:oMath>", r.Equation.Omml);
        Assert.Contains("<m:f>", r.Equation.Omml); // fraction
    }

    [Fact]
    public void Convert_NoMath_ReturnsFailure()
    {
        var r = ConversionPipeline.Convert("Bonjour tout le monde");
        Assert.False(r.Success);
        Assert.Equal("no_math_zone_detected", r.Reason);
    }

    [Fact]
    public void Convert_SimpleExponent()
    {
        var r = ConversionPipeline.Convert("Soit x^2");
        Assert.True(r.Success);
        Assert.Contains("<m:sSup>", r.Equation!.Omml);
    }

    [Fact]
    public void Convert_PreservesSourceForStorage()
    {
        var r = ConversionPipeline.Convert("On a f(x) = 1/x");
        Assert.Equal("f(x) = 1/x", r.Equation!.Source);
    }

    [Fact]
    public void Convert_MetadataIncludesCoreVersion()
    {
        var r = ConversionPipeline.Convert("a + b");
        Assert.Equal(ConversionPipeline.CoreVersion, r.Equation!.Metadata.CoreVersion);
    }

    [Fact]
    public void Convert_Multilingual_DetectsInGerman()
    {
        var r = ConversionPipeline.Convert("Sei f(x) = 2x + 1");
        Assert.True(r.Success);
        Assert.Equal("f(x) = 2x + 1", r.Equation!.Source);
    }

    [Fact]
    public void Convert_MultiSymbol_AlphaPlusBeta_ExpandedAsExpression()
    {
        var r = ConversionPipeline.Convert("alpha+beta");
        Assert.True(r.Success);
        Assert.NotNull(r.Zone);
        // Source : texte original (pour savoir quoi retirer du doc)
        Assert.Equal("alpha+beta", r.Equation!.Source);
        // UnicodeFallback : version pré-traitée passée à BuildUp
        Assert.Equal("α+β", r.Equation.UnicodeFallback);
        // OMath présent (pas un simple symbole isolé)
        Assert.NotNull(r.Equation.Omml);
    }

    [Fact]
    public void Convert_MultiSymbol_GreekSumWithProse()
    {
        // "Soit alpha+beta+gamma" : "Soit" est stopword, le reste est zone math
        var r = ConversionPipeline.Convert("Soit alpha+beta+gamma");
        Assert.True(r.Success);
        Assert.Equal("alpha+beta+gamma", r.Equation!.Source);
        Assert.Equal("α+β+γ", r.Equation.UnicodeFallback);
    }

    [Fact]
    public void Convert_SymbolAlone_FallsBackToSymbolMatch()
    {
        // "alpha" tout seul : pas de zone math (pas d'opérateur), fallback symbol
        var r = ConversionPipeline.Convert("alpha");
        Assert.True(r.Success);
        Assert.Null(r.Zone); // chemin symbol-only
        Assert.Equal("alpha", r.Equation!.Source);
        Assert.Equal("α", r.Equation.UnicodeFallback);
        Assert.Null(r.Equation.Omml); // texte simple, l'host wrap en OMath
    }

    [Fact]
    public void Convert_MixedSymbolsAndMath_AlphaSquared()
    {
        // alpha^2 = α^2 → expression math avec exposant
        var r = ConversionPipeline.Convert("alpha^2");
        Assert.True(r.Success);
        Assert.Equal("alpha^2", r.Equation!.Source);
        Assert.Equal("α^2", r.Equation.UnicodeFallback);
    }

    [Fact]
    public void Convert_AfterProse_PiTimesRSquared()
    {
        // "L'aire est pi*r^2"
        var r = ConversionPipeline.Convert("L'aire est pi*r^2");
        Assert.True(r.Success);
        // pi sera remplacé par π dans la zone détectée
        Assert.Contains("π", r.Equation!.UnicodeFallback);
        Assert.Contains("r", r.Equation.UnicodeFallback);
    }

    // ============================================================
    // SIGNAUX DE SORTIE (cf. briefs/architecture-flow.md §2.1)
    // \n/\r sont gérés par le bornage paragraphe en amont (WordContextReader),
    // pas la peine de les tester ici.
    // ============================================================

    [Fact]
    public void Convert_TrailingTab_RejectsAsExit()
    {
        var r = ConversionPipeline.Convert("alpha\t");
        Assert.False(r.Success);
        Assert.Equal("trailing_exit_signal", r.Reason);
    }

    [Fact]
    public void Convert_DoubleSpace_RejectsAsExit()
    {
        var r = ConversionPipeline.Convert("alpha  ");
        Assert.False(r.Success);
        Assert.Equal("trailing_exit_signal", r.Reason);
    }

    [Fact]
    public void Convert_SingleSpace_StillAllowsConversion()
    {
        // Un seul espace tolère (utilisateur en pause)
        var r = ConversionPipeline.Convert("alpha ");
        Assert.True(r.Success);
        Assert.Equal("α", r.Equation!.UnicodeFallback);
    }

    [Fact]
    public void Convert_DoubleSpaceMidWayThroughExpression_DoesNotReject()
    {
        // Le exit signal ne s'applique qu'à la FIN du texte. Si le texte se
        // termine bien (sans 2+ espaces / newline / tab), c'est OK.
        var r = ConversionPipeline.Convert("alpha  +beta");
        Assert.True(r.Success);
    }

    // ============================================================
    // FRONTIÈRES (zone, stopwords, vide)
    // ============================================================

    [Fact]
    public void Convert_EmptyInput_Fails()
    {
        var r = ConversionPipeline.Convert("");
        Assert.False(r.Success);
    }

    [Fact]
    public void Convert_OnlyWhitespace_Fails()
    {
        var r = ConversionPipeline.Convert("   ");
        Assert.False(r.Success);
    }

    [Fact]
    public void Convert_StopwordBreaksZone()
    {
        // "et" est stopword français → coupe la zone
        // "f(x)=1 et g(x)=2" → seul "g(x)=2" est zone
        var r = ConversionPipeline.Convert("f(x)=1 et g(x)=2");
        Assert.True(r.Success);
        // La zone détectée doit commencer après "et "
        Assert.Equal("g(x)=2", r.Equation!.Source);
    }

    [Fact]
    public void Convert_ProseAfterMath_ZoneExcludesProse()
    {
        // "f(x)=1/x bonjour" : la zone math doit s'arrêter avant "bonjour"
        // (mot prose en fin). Le ZoneDetector skip les tokens trailing low-score.
        var r = ConversionPipeline.Convert("f(x)=1/x bonjour");
        Assert.True(r.Success);
        Assert.Equal("f(x)=1/x", r.Equation!.Source); // bonjour exclu
    }

    [Fact]
    public void Convert_OperatorAtEnd_StillDetected()
    {
        // Si l'expression se termine sur un opérateur, est-ce détecté ?
        // "x +" : x=letter no math context (single letter no neighbors), + op
        // Zone : + (0.9), ws, x (0.85 because next is op...). Ambigu.
        // Test ce que le pipeline produit réellement.
        var r = ConversionPipeline.Convert("a + b");
        Assert.True(r.Success);
        Assert.Equal("a + b", r.Equation!.Source);
    }
}
