using System.Collections.Generic;
using MathCursor.Core.Ast;

namespace MathCursor.Core.Parsing;

/// <summary>
/// Parser recursive-descent des expressions math.
/// Précédence : ^ > */ > +-=,
/// Porté depuis archive/officejs-prototype/src/taskpane/conversion/parser.ts.
/// </summary>
public static class Parser
{
    private sealed class State { public int I; }

    public static MathNode Parse(IList<LexToken> tokens)
    {
        var state = new State { I = 0 };
        return ParseAdditive(tokens, state);
    }

    // additive : [+|-|=] term ((+|-|=|,) term)*
    private static MathNode ParseAdditive(IList<LexToken> tks, State p)
    {
        MathNode left;
        if (p.I < tks.Count && tks[p.I].Kind == LexTokenKind.Op && "+-=".Contains(tks[p.I].Value))
        {
            var op = tks[p.I++].Value;
            left = new UnaryNode { Op = op, Child = ParseMultiplicative(tks, p) };
        }
        else
        {
            left = ParseMultiplicative(tks, p);
        }

        while (p.I < tks.Count && tks[p.I].Kind == LexTokenKind.Op && "+-=,".Contains(tks[p.I].Value))
        {
            var op = tks[p.I++].Value;
            left = new BinaryOpNode { Op = op, Left = left, Right = ParseMultiplicative(tks, p) };
        }
        return left;
    }

    // multiplicative : power ((* | / | juxtaposition) power)*
    private static MathNode ParseMultiplicative(IList<LexToken> tks, State p)
    {
        var left = ParsePower(tks, p);
        while (p.I < tks.Count)
        {
            var nx = tks[p.I];
            if (nx.Kind == LexTokenKind.Op && nx.Value == "/")
            {
                p.I++;
                left = new FractionNode { Numerator = left, Denominator = ParsePower(tks, p) };
                continue;
            }
            if (nx.Kind == LexTokenKind.Op && nx.Value == "*")
            {
                p.I++;
                left = new BinaryOpNode { Op = "\u00D7", Left = left, Right = ParsePower(tks, p) };
                continue;
            }
            if (nx.Kind == LexTokenKind.Number || nx.Kind == LexTokenKind.Variable
                || nx.Kind == LexTokenKind.LParen || nx.Kind == LexTokenKind.LBracket)
            {
                var right = ParsePower(tks, p);
                if (left is JuxtapositionNode j) { j.Parts.Add(right); }
                else { left = new JuxtapositionNode { Parts = new List<MathNode> { left, right } }; }
                continue;
            }
            break;
        }
        return left;
    }

    // power : unary ^ power (droite-associatif)
    private static MathNode ParsePower(IList<LexToken> tks, State p)
    {
        if (p.I < tks.Count && tks[p.I].Kind == LexTokenKind.Op && "+-".Contains(tks[p.I].Value))
        {
            var op = tks[p.I++].Value;
            return new UnaryNode { Op = op, Child = ParsePower(tks, p) };
        }
        var basis = ParseAtom(tks, p);
        if (p.I < tks.Count && tks[p.I].Kind == LexTokenKind.Op && tks[p.I].Value == "^")
        {
            p.I++;
            return new SuperscriptNode { Base = basis, Exponent = ParsePower(tks, p) };
        }
        return basis;
    }

    // atom : nombre | variable | (expr) | [expr]
    private static MathNode ParseAtom(IList<LexToken> tks, State p)
    {
        if (p.I >= tks.Count) return new EmptyNode();
        var tk = tks[p.I];

        if (tk.Kind == LexTokenKind.LParen || tk.Kind == LexTokenKind.LBracket)
        {
            var open = tk.Kind == LexTokenKind.LParen ? "(" : "[";
            var close = tk.Kind == LexTokenKind.LParen ? ")" : "]";
            p.I++;
            var inner = ParseAdditive(tks, p);
            if (p.I < tks.Count && tks[p.I].Value == close) p.I++;
            return new ParenNode { OpenChar = open, Inner = inner };
        }

        if (tk.Kind == LexTokenKind.Number) { p.I++; return new NumberNode { Value = tk.Value }; }
        if (tk.Kind == LexTokenKind.Variable) { p.I++; return new VariableNode { Name = tk.Value }; }

        p.I++;
        return new VariableNode { Name = tk.Value };
    }
}
