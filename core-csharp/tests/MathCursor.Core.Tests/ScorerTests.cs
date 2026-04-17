using System.Collections.Generic;
using System.Linq;
using MathCursor.Core.Tokenization;
using MathCursor.Core.ZoneDetection;
using Xunit;

namespace MathCursor.Core.Tests;

public class ScorerTests
{
    private static IList<Token> TokenizeAndScore(string text)
    {
        var tokens = (IList<Token>)Tokenizer.Tokenize(text);
        Scorer.ScoreAll(tokens);
        return tokens;
    }

    [Fact]
    public void Stopwords_Score0()
    {
        var tokens = TokenizeAndScore("on a");
        var on = tokens.First(t => t.Normalized == "on");
        Assert.Equal(0.0, on.Mathiness);
    }

    [Fact]
    public void MathFunctions_ScoreHigh()
    {
        var tokens = TokenizeAndScore("sin(x)");
        var sin = tokens.First(t => t.Normalized == "sin");
        Assert.True(sin.Mathiness >= 0.9, $"sin.Mathiness = {sin.Mathiness}, attendu >= 0.9");
    }

    [Fact]
    public void Operators_ScoreHigh()
    {
        var tokens = TokenizeAndScore("a + b");
        var plus = tokens.First(t => t.Text == "+");
        Assert.True(plus.Mathiness >= 0.8, $"'+'.Mathiness = {plus.Mathiness}, attendu >= 0.8");
    }

    [Fact]
    public void LongProseWords_ScoreLow()
    {
        var tokens = TokenizeAndScore("Bonjour");
        Assert.True(tokens[0].Mathiness < 0.3,
            $"Bonjour.Mathiness = {tokens[0].Mathiness}, attendu < 0.3");
    }
}
