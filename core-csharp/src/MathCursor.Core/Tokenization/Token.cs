using System.Collections.Generic;

namespace MathCursor.Core.Tokenization;

/// <summary>Token produit par le tokenizer : texte, positions, catégories, score mathiness.</summary>
public sealed class Token
{
    public string Text { get; init; } = "";
    public string Normalized { get; init; } = "";
    public int Start { get; init; }
    public int End { get; init; }
    public HashSet<UnicodeCategory> Categories { get; init; } = new();
    public double Mathiness { get; set; }
}

public enum UnicodeCategory
{
    Letter,
    GreekLetter,
    MathSymbol,
    Operator,
    Paren,
    Comma,
    Dot,
    Digit,
    Whitespace,
    Unknown,
}
