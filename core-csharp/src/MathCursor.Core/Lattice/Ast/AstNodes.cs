using System.Collections.Generic;

namespace MathCursor.Core.Lattice.Ast
{
    /// <summary>Atome terminal : nombre, identifiant, lettre grecque.</summary>
    public sealed class Atom : AstNode
    {
        public string Kind { get; }   // "number" | "ident" | "greek"
        public string Value { get; }
        public Atom(string kind, string value) { Kind = kind; Value = value; }
    }

    /// <summary>
    /// Slot non rempli, indexé par sa position dans le keyword parent
    /// (Sum=① var ② start ③ end ④ body, Frac=① num ② den, etc.).
    /// Doublonne comme point d'ancrage Tab autocomplete (cf. algorithm.md §5).
    /// </summary>
    public sealed class Hole : AstNode
    {
        public int Idx { get; }
        public Hole(int idx) { Idx = idx; }
    }

    /// <summary>Constante symbolique (\\infty, \\forall, \\exists, \\in).</summary>
    public sealed class Const : AstNode
    {
        public string Value { get; }
        public Const(string value) { Value = value; }
    }

    /// <summary>Opérateur unaire : +x, -x.</summary>
    public sealed class Unary : AstNode
    {
        public string Op { get; }
        public AstNode Arg { get; }
        public Unary(string op, AstNode arg) { Op = op; Arg = arg; }
    }

    /// <summary>
    /// Opérateur binaire : +, -, *, /. Le drapeau <see cref="Tight"/> reflète
    /// l'absence d'espace adjacent dans la saisie ; <see cref="Implicit"/>
    /// indique une multiplication par adjacence (2x, ab) versus opérateur
    /// explicite (2*x).
    /// </summary>
    public sealed class Bin : AstNode
    {
        public string Op { get; }
        public bool Tight { get; }
        public bool Implicit { get; }
        public AstNode Lhs { get; }
        public AstNode Rhs { get; }
        public Bin(string op, bool tight, bool isImplicit, AstNode lhs, AstNode rhs)
        {
            Op = op; Tight = tight; Implicit = isImplicit; Lhs = lhs; Rhs = rhs;
        }
    }

    /// <summary>Exposant : base^exp.
    /// <para><see cref="IsImplicit"/> = true quand le Sup est issu d'une règle
    /// auto (x2 → x², cos2(x) → cos²(x), 0+ → 0⁺). false quand l'utilisateur
    /// a tapé `^` explicitement. Sert à distinguer côté AlternativeGenerator :
    /// l'ambiguïté x² vs x_2 ne se pose QUE pour x2 implicite, pas pour x^2
    /// où l'utilisateur a déjà tranché.</para>
    /// </summary>
    public sealed class Sup : AstNode
    {
        public AstNode Base { get; }
        public AstNode Exp { get; }
        public bool IsImplicit { get; }
        public Sup(AstNode @base, AstNode exp, bool isImplicit = false)
        {
            Base = @base; Exp = exp; IsImplicit = isImplicit;
        }
    }

    /// <summary>Indice : base_idx.</summary>
    public sealed class Sub : AstNode
    {
        public AstNode Base { get; }
        public AstNode Idx { get; }
        public Sub(AstNode @base, AstNode idx) { Base = @base; Idx = idx; }
    }

    /// <summary>Groupe parenthésé. Le renderer décide si les parens
    /// restent visibles (selon le contexte : argument de fonction, num de
    /// fraction, etc.).</summary>
    public sealed class Group : AstNode
    {
        public AstNode Expr { get; }
        public Group(AstNode expr) { Expr = expr; }
    }

    /// <summary>Fraction.</summary>
    public sealed class Frac : AstNode
    {
        public AstNode Num { get; }
        public AstNode Den { get; }
        public Frac(AstNode num, AstNode den) { Num = num; Den = den; }
    }

    /// <summary>Racine carrée.</summary>
    public sealed class Sqrt : AstNode
    {
        public AstNode Arg { get; }
        public Sqrt(AstNode arg) { Arg = arg; }
    }

    /// <summary>Vecteur (nom = chaîne d'identifiants concaténés, ex : AB → \\vec{AB}).</summary>
    public sealed class Vec : AstNode
    {
        public string? Name { get; }
        public Vec(string? name) { Name = name; }
    }

    /// <summary>Fonction nommée : sin, cos, ln, exp…</summary>
    public sealed class Func : AstNode
    {
        public string Name { get; }
        public AstNode Arg { get; }
        public Func(string name, AstNode arg) { Name = name; Arg = arg; }
    }

    /// <summary>Somme/produit. <see cref="Symbol"/> = "sum" ou "prod".</summary>
    public sealed class Sum : AstNode
    {
        public string Symbol { get; }
        public AstNode Var { get; }
        public AstNode Start { get; }
        public AstNode End { get; }
        public AstNode Body { get; }
        public Sum(string symbol, AstNode var, AstNode start, AstNode end, AstNode body)
        {
            Symbol = symbol; Var = var; Start = start; End = end; Body = body;
        }
    }

    /// <summary>Limite : lim_{var → target} body.</summary>
    public sealed class Lim : AstNode
    {
        public AstNode Var { get; }
        public AstNode Target { get; }
        public AstNode Body { get; }
        public Lim(AstNode var, AstNode target, AstNode body)
        {
            Var = var; Target = target; Body = body;
        }
    }

    /// <summary>Intégrale : ∫_low^high body.</summary>
    public sealed class Int : AstNode
    {
        public AstNode Low { get; }
        public AstNode High { get; }
        public AstNode Body { get; }
        public Int(AstNode low, AstNode high, AstNode body)
        {
            Low = low; High = high; Body = body;
        }
    }

    /// <summary>Définition de fonction : f : x ↦ expr (ou f : (x,y) ↦ expr).
    /// Pattern lycée FR. <see cref="Vars"/> contient ≥ 1 variable ; le rendu
    /// ajoute des parens automatiquement quand <c>Vars.Count > 1</c>.</summary>
    public sealed class FuncDef : AstNode
    {
        public string Name { get; }
        public IReadOnlyList<AstNode> Vars { get; }
        public AstNode Body { get; }
        public FuncDef(string name, IReadOnlyList<AstNode> vars, AstNode body)
        {
            Name = name; Vars = vars; Body = body;
        }
    }

    /// <summary>Intervalle français : <c>[a,b]</c>, <c>[a,b[</c>, <c>]a,b]</c>, <c>]a,b[</c>.
    /// Les flags <see cref="LeftClosed"/> / <see cref="RightClosed"/> indiquent
    /// si la borne est incluse (`[` / `]` côté ouvert/fermé). Le rendu garde la
    /// notation lycée française telle quelle (brackets bruts, pas de `\left`/`\right`
    /// pour préserver la compat WpfMath/Word OMath sur les délimiteurs inversés).</summary>
    public sealed class Interval : AstNode
    {
        public AstNode Low { get; }
        public AstNode High { get; }
        public bool LeftClosed { get; }
        public bool RightClosed { get; }
        public Interval(AstNode low, AstNode high, bool leftClosed, bool rightClosed)
        {
            Low = low; High = high; LeftClosed = leftClosed; RightClosed = rightClosed;
        }
    }
}
