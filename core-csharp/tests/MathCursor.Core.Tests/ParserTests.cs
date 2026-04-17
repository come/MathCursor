using MathCursor.Core.Ast;
using MathCursor.Core.Parsing;
using Xunit;

namespace MathCursor.Core.Tests;

public class ParserTests
{
    private static MathNode Parse(string input) => Parser.Parse(Lexer.Lex(input));

    [Fact]
    public void Parse_SimpleFraction()
    {
        var ast = Parse("1/2");
        var frac = Assert.IsType<FractionNode>(ast);
        Assert.Equal("1", Assert.IsType<NumberNode>(frac.Numerator).Value);
        Assert.Equal("2", Assert.IsType<NumberNode>(frac.Denominator).Value);
    }

    [Fact]
    public void Parse_Superscript()
    {
        var ast = Parse("x^2");
        var sup = Assert.IsType<SuperscriptNode>(ast);
        Assert.Equal("x", Assert.IsType<VariableNode>(sup.Base).Name);
        Assert.Equal("2", Assert.IsType<NumberNode>(sup.Exponent).Value);
    }

    [Fact]
    public void Parse_FunctionCall_IsJuxtaposition()
    {
        // f(x) est un var juxtaposé à un paren(x)
        var ast = Parse("f(x)");
        var juxt = Assert.IsType<JuxtapositionNode>(ast);
        Assert.Equal(2, juxt.Parts.Count);
        Assert.Equal("f", Assert.IsType<VariableNode>(juxt.Parts[0]).Name);
        var paren = Assert.IsType<ParenNode>(juxt.Parts[1]);
        Assert.Equal("(", paren.OpenChar);
    }

    [Fact]
    public void Parse_PrecedenceExpMulAdd()
    {
        // 1 + 2*x^3 → add(1, mul(2, pow(x, 3)))
        var ast = Parse("1 + 2*x^3");
        var add = Assert.IsType<BinaryOpNode>(ast);
        Assert.Equal("+", add.Op);
    }

    [Fact]
    public void Parse_EqualityAsOp()
    {
        // f(x) = 2x → op(=, juxt(f, (x)), juxt(2, x))
        var ast = Parse("f(x) = 2x");
        var eq = Assert.IsType<BinaryOpNode>(ast);
        Assert.Equal("=", eq.Op);
    }

    [Fact]
    public void Parse_GreekSymbolSubstitution()
    {
        // alpha devient α via WordSymbols du lexer
        var ast = Parse("alpha");
        var v = Assert.IsType<VariableNode>(ast);
        Assert.Equal("\u03B1", v.Name);
    }

    [Fact]
    public void Parse_EmptyInput_ReturnsEmpty()
    {
        var ast = Parse("");
        Assert.IsType<EmptyNode>(ast);
    }
}
