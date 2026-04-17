using System.Collections.Generic;

namespace MathCursor.Core.Ast;

/// <summary>
/// Racine de l'AST math. Schéma normatif dans specs/ast-schema.json
/// (à compléter en phase B).
/// </summary>
public abstract class MathNode
{
    public abstract string Kind { get; }
}

public sealed class NumberNode : MathNode
{
    public override string Kind => "number";
    public string Value { get; init; } = "";
}

public sealed class VariableNode : MathNode
{
    public override string Kind => "variable";
    public string Name { get; init; } = "";
}

public sealed class BinaryOpNode : MathNode
{
    public override string Kind => "binop";
    public string Op { get; init; } = "";
    public MathNode Left { get; init; } = null!;
    public MathNode Right { get; init; } = null!;
}

public sealed class FractionNode : MathNode
{
    public override string Kind => "frac";
    public MathNode Numerator { get; init; } = null!;
    public MathNode Denominator { get; init; } = null!;
}

public sealed class SuperscriptNode : MathNode
{
    public override string Kind => "sup";
    public MathNode Base { get; init; } = null!;
    public MathNode Exponent { get; init; } = null!;
}

public sealed class ParenNode : MathNode
{
    public override string Kind => "paren";
    public string OpenChar { get; init; } = "(";
    public MathNode Inner { get; init; } = null!;
}

public sealed class JuxtapositionNode : MathNode
{
    public override string Kind => "juxt";
    public IList<MathNode> Parts { get; init; } = new List<MathNode>();
}

public sealed class UnaryNode : MathNode
{
    public override string Kind => "unary";
    public string Op { get; init; } = "";
    public MathNode Child { get; init; } = null!;
}

public sealed class EmptyNode : MathNode
{
    public override string Kind => "empty";
}
