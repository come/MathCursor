using System.Collections.Generic;
using System.Linq;
using MathCursor.Core.Tokenization;
using Xunit;

namespace MathCursor.Core.Tests;

public class TokenizerTests
{
    [Fact]
    public void Tokenize_SimpleExpression_ProducesExpectedTokens()
    {
        var tokens = Tokenizer.Tokenize("f(x) = 2x + 1");
        var nonWs = tokens.Where(t => !t.Categories.Contains(UnicodeCategory.Whitespace))
                         .Select(t => t.Normalized).ToArray();
        Assert.Equal(new[] { "f", "(", "x", ")", "=", "2", "x", "+", "1" }, nonWs);
    }

    [Fact]
    public void Tokenize_PreservesPositions()
    {
        var tokens = Tokenizer.Tokenize("ab + 3");
        Assert.Equal(0, tokens[0].Start);
        Assert.Equal(2, tokens[0].End);
        Assert.Equal("ab", tokens[0].Text);
    }

    [Fact]
    public void Tokenize_GroupsMultiCharOperators()
    {
        var tokens = Tokenizer.Tokenize("x >= 1 <=> y <= 0");
        var ops = tokens.Where(t => t.Categories.Contains(UnicodeCategory.Operator))
                       .Select(t => t.Text).ToList();
        Assert.Contains(">=", ops);
        Assert.Contains("<=>", ops);
        Assert.Contains("<=", ops);
    }

    [Fact]
    public void Tokenize_NormalizesMathItalic()
    {
        // 𝑓(𝑥) en math italic U+1D400+
        var tokens = Tokenizer.Tokenize("\uD835\uDC53(\uD835\uDC65)");
        var norm = tokens.Where(t => !t.Categories.Contains(UnicodeCategory.Whitespace))
                        .Select(t => t.Normalized).ToArray();
        Assert.Equal(new[] { "f", "(", "x", ")" }, norm);
    }
}
