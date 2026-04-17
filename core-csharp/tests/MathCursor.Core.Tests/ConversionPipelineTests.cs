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
}
