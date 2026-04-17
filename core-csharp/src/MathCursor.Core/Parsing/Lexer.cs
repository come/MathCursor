using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MathCursor.Core.Parsing;

/// <summary>
/// Token simplifié consommé par le parser recursive-descent. Différent du
/// Token enrichi de Tokenization/Token.cs (qui est utilisé par le Scorer et
/// le ZoneDetector). Ici on ne garde que type + valeur pour un parsing compact.
/// </summary>
public sealed class LexToken
{
    public LexTokenKind Kind { get; }
    public string Value { get; }

    public LexToken(LexTokenKind kind, string value) { Kind = kind; Value = value; }

    public override string ToString() => $"{Kind}({Value})";
}

public enum LexTokenKind
{
    Number,      // 42, 3.14
    Variable,    // x, alpha, f, π
    Op,          // + - * / ^ = ,
    LParen,      // (
    RParen,      // )
    LBracket,    // [
    RBracket,    // ]
}

/// <summary>
/// Lexer pour le parser d'expression. Porté depuis
/// archive/officejs-prototype/src/taskpane/conversion/tokenizer-v1.ts.
/// Applique : préprocessing exposant (x 2 → x^2) + substitution mots symboliques.
/// </summary>
public static class Lexer
{
    // Mots → symboles Unicode (grec + infini + vide + sqrt...)
    private static readonly Dictionary<string, string> WordSymbols = new()
    {
        ["alpha"] = "\u03B1",
        ["beta"] = "\u03B2",
        ["gamma"] = "\u03B3",
        ["delta"] = "\u03B4",
        ["epsilon"] = "\u03B5",
        ["theta"] = "\u03B8",
        ["lambda"] = "\u03BB",
        ["mu"] = "\u03BC",
        ["pi"] = "\u03C0",
        ["sigma"] = "\u03C3",
        ["omega"] = "\u03C9",
        ["phi"] = "\u03C6",
        ["inf"] = "\u221E",
        ["infini"] = "\u221E",
        ["sqrt"] = "\u221A",
        ["vide"] = "\u2205",
    };

    private static readonly Regex ExponentPrep =
        new(@"([a-zA-Z\d]+)\s+(\d+)(?=\s|$|[+\-*/=)\]])");

    public static IList<LexToken> Lex(string input)
    {
        // Préprocessing : "x 2" (espace puis chiffre) → "x^2"
        var p = ExponentPrep.Replace(input, "$1^$2");

        var tokens = new List<LexToken>();
        int i = 0;
        while (i < p.Length)
        {
            char c = p[i];

            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (char.IsDigit(c))
            {
                var n = new StringBuilder();
                while (i < p.Length && (char.IsDigit(p[i]) || p[i] == '.')) n.Append(p[i++]);
                tokens.Add(new LexToken(LexTokenKind.Number, n.ToString()));
                continue;
            }

            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
            {
                var w = new StringBuilder();
                while (i < p.Length && ((p[i] >= 'a' && p[i] <= 'z') || (p[i] >= 'A' && p[i] <= 'Z')))
                    w.Append(p[i++]);
                var word = w.ToString();
                var key = word.ToLowerInvariant();
                tokens.Add(new LexToken(LexTokenKind.Variable,
                    WordSymbols.TryGetValue(key, out var sym) ? sym : word));
                continue;
            }

            switch (c)
            {
                case '(': tokens.Add(new LexToken(LexTokenKind.LParen, "(")); i++; continue;
                case ')': tokens.Add(new LexToken(LexTokenKind.RParen, ")")); i++; continue;
                case '[': tokens.Add(new LexToken(LexTokenKind.LBracket, "[")); i++; continue;
                case ']': tokens.Add(new LexToken(LexTokenKind.RBracket, "]")); i++; continue;
                case '+':
                case '-':
                case '*':
                case '/':
                case '^':
                case '=':
                case ',':
                    tokens.Add(new LexToken(LexTokenKind.Op, c.ToString())); i++; continue;
                default:
                    i++; // caractère inconnu : skip
                    continue;
            }
        }

        return tokens;
    }
}
