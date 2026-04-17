using MathCursor.Core.Ast;
using MathCursor.Core.Parsing;
using MathCursor.Core.Serialization;
using Xunit;

namespace MathCursor.Core.Tests;

public class OmmlSerializerTests
{
    private static string SerializeFromSource(string input) =>
        OmmlSerializer.Serialize(Parser.Parse(Lexer.Lex(input)));

    [Fact]
    public void Serialize_Number_ProducesMrWithCambriaMath()
    {
        var xml = SerializeFromSource("42");
        Assert.Contains("<m:r>", xml);
        Assert.Contains("<m:t>42</m:t>", xml);
        Assert.Contains("Cambria Math", xml);
    }

    [Fact]
    public void Serialize_Fraction_WrapsInMF()
    {
        var xml = SerializeFromSource("1/2");
        Assert.Contains("<m:f>", xml);
        Assert.Contains("<m:num>", xml);
        Assert.Contains("<m:den>", xml);
    }

    [Fact]
    public void Serialize_Superscript_WrapsInMSSup()
    {
        var xml = SerializeFromSource("x^2");
        Assert.Contains("<m:sSup>", xml);
        Assert.Contains("<m:e>", xml);
        Assert.Contains("<m:sup>", xml);
    }

    [Fact]
    public void Serialize_FractionWithParens_StripsOuterParens()
    {
        // (a+b)/(c+d) → numerator et denominator ne gardent PAS les parens englobantes
        var xml = SerializeFromSource("(a+b)/(c+d)");
        Assert.Contains("<m:f>", xml);
        // Comme on retire les parens, on ne doit PAS retrouver "(" directement dans les num/den
        // en tant que m:t autonome. Ce test est un sanity check.
        var numStart = xml.IndexOf("<m:num>");
        var numEnd = xml.IndexOf("</m:num>");
        var numPart = xml.Substring(numStart, numEnd - numStart);
        Assert.DoesNotContain("<m:t>(</m:t>", numPart);
    }

    [Fact]
    public void Serialize_EscapesXmlSpecialChars()
    {
        // < > & dans les variables/opérateurs doivent être escapés
        var xml = OmmlSerializer.Serialize(new VariableNode { Name = "<x>" });
        Assert.Contains("&lt;x&gt;", xml);
        Assert.DoesNotContain("<m:t><x>", xml);
    }

    [Fact]
    public void BuildPackage_WrapsInOMathPkg()
    {
        var inner = SerializeFromSource("x^2");
        var pkg = OmmlSerializer.BuildPackage(inner);
        Assert.Contains("<pkg:package", pkg);
        Assert.Contains("<m:oMath>", pkg);
        Assert.Contains(inner, pkg);
    }
}
