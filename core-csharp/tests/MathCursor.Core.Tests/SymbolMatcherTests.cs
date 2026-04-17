using MathCursor.Core.Symbols;
using Xunit;

namespace MathCursor.Core.Tests;

public class SymbolMatcherTests
{
    private static string? MatchedReplacement(string input)
    {
        var m = SymbolMatcher.FindSymbol(input);
        return m?.Choices[0].Replacement;
    }

    [Theory]
    [InlineData("alpha", "\u03B1")]
    [InlineData("pi", "\u03C0")]
    [InlineData("Delta", "\u0394")]
    [InlineData("epsilon", "\u03B5")]
    [InlineData("omega", "\u03C9")]
    public void GreekLetters_AreReplaced(string input, string expected)
    {
        Assert.Equal(expected, MatchedReplacement(input));
    }

    [Theory]
    [InlineData(">=", "\u2265")]
    [InlineData("<=", "\u2264")]
    [InlineData("!=", "\u2260")]
    [InlineData("<=>", "\u27FA")]
    [InlineData("=>", "\u27F9")]
    public void ComparisonAndImplication(string input, string expected)
    {
        Assert.Equal(expected, MatchedReplacement(input));
    }

    [Theory]
    [InlineData("Vx(R", "\u2200x \u2208 \u211D")]
    [InlineData("V x dans N", "\u2200x \u2208 \u2115")]
    [InlineData("qq y c Z", "\u2200y \u2208 \u2124")]
    public void ForAllPatterns(string input, string expected)
    {
        Assert.Equal(expected, MatchedReplacement(input));
    }

    [Theory]
    [InlineData("Ex(R", "\u2203x \u2208 \u211D")]
    [InlineData("E!x", "\u2203!x")]
    public void ExistsPatterns(string input, string expected)
    {
        Assert.Equal(expected, MatchedReplacement(input));
    }

    [Theory]
    [InlineData("(R", "\u2208\u211D")]
    [InlineData("!(R", "\u2209\u211D")]
    [InlineData("sub R", "\u2282\u211D")]
    public void SetMembership(string input, string expected)
    {
        Assert.Equal(expected, MatchedReplacement(input));
    }

    [Theory]
    [InlineData("AuB", "A\u222AB")]
    [InlineData("AnB", "A\u2229B")]
    public void UnionIntersection(string input, string expected)
    {
        Assert.Equal(expected, MatchedReplacement(input));
    }

    [Theory]
    [InlineData("inf", "\u221E")]
    [InlineData("-inf", "-\u221E")]
    [InlineData("+inf", "+\u221E")]
    [InlineData("vide", "\u2205")]
    public void Infinity_AndEmptySet(string input, string expected)
    {
        Assert.Equal(expected, MatchedReplacement(input));
    }

    [Theory]
    [InlineData("f'", "f\u2032")]
    [InlineData("g''", "g\u2033")]
    public void Derivatives(string input, string expected)
    {
        Assert.Equal(expected, MatchedReplacement(input));
    }

    [Theory]
    [InlineData("ang ABC", "\u2220ABC")]
    [InlineData("seg AB", "[AB]")]
    public void Angle_AndSegment(string input, string expected)
    {
        Assert.Equal(expected, MatchedReplacement(input));
    }

    [Fact]
    public void Vector_HasCombiningArrowOnEachLetter()
    {
        // "vec AB" → A⃗B⃗ (chaque lettre suivie de U+20D7)
        var r = MatchedReplacement("vec AB");
        Assert.Equal("A\u20D7B\u20D7", r);
    }

    [Fact]
    public void NonMatchingText_ReturnsNull()
    {
        Assert.Null(SymbolMatcher.FindSymbol("Bonjour tout le monde"));
        Assert.Null(SymbolMatcher.FindSymbol("f(x)=2x"));
    }

    [Fact]
    public void MatchAtEnd_WithProseBefore()
    {
        // "Soit x dans alpha" → matche "alpha" en fin
        var m = SymbolMatcher.FindSymbol("Soit x dans alpha");
        Assert.NotNull(m);
        Assert.Equal("alpha", m!.Raw);
        Assert.Equal("\u03B1", m.Choices[0].Replacement);
    }

    [Fact]
    public void TrailingWhitespace_IsIgnored()
    {
        var m = SymbolMatcher.FindSymbol("alpha   \t  ");
        Assert.NotNull(m);
        Assert.Equal("\u03B1", m!.Choices[0].Replacement);
    }
}
