using System.Collections.Generic;
using MathCursor.Core.Lattice.Ast;

namespace MathCursor.Core.Lattice
{
    /// <summary>
    /// Parser récursif descendant qui consomme une séquence d'arêtes (top-1 du
    /// lattice) et produit un AST avec Holes pour les slots manquants.
    ///
    /// Grammaire (cf. algorithm.md §4) :
    /// <code>
    /// Expr     = Term ((+|-) Term)*
    /// Term     = Postfix ((*|/) Postfix | implicit)*
    /// Postfix  = Primary (^ Argument | _ Argument)*
    /// Primary  = Unary | Atom | Group | Scope | Func
    /// Argument = Atom | Group | TightChain | Unary
    /// Body     = Postfix (TightOp Postfix | adjacent Postfix)*  // s'arrête au binop loose
    /// Scope    = Lim | Sum | Int | Sqrt | Frac | Vec
    /// </code>
    ///
    /// Les <see cref="LatticeEdge"/> de type Space doivent être filtrés AVANT
    /// d'entrer dans le parser (ils servent uniquement à calculer le drapeau
    /// Tight au lexer).
    /// </summary>
    public sealed class Parser
    {
        private readonly IReadOnlyList<LatticeEdge> _toks;
        private int _i;

        public Parser(IReadOnlyList<LatticeEdge> tokens)
        {
            // Filtrer les Space ici plutôt que d'imposer au caller — moins fragile.
            var filtered = new List<LatticeEdge>(tokens.Count);
            foreach (var t in tokens)
                if (t.Type != EdgeType.Space) filtered.Add(t);
            _toks = filtered;
            _i = 0;
        }

        // ---------------- Helpers ----------------

        private LatticeEdge? Peek(int off = 0)
            => (_i + off) < _toks.Count ? _toks[_i + off] : null;

        private LatticeEdge Consume() => _toks[_i++];

        private int PrevEnd() => _i > 0 ? _toks[_i - 1].End : -1;

        private bool IsOp(params string[] values)
        {
            var t = Peek();
            if (t == null || t.Type != EdgeType.Op) return false;
            foreach (var v in values) if (t.Value == v) return true;
            return false;
        }

        private bool IsTightAdjacent()
        {
            var t = Peek();
            return t != null && t.Start == PrevEnd();
        }

        private bool CanStartFactor()
        {
            var t = Peek();
            if (t == null) return false;
            return t.Type == EdgeType.Number || t.Type == EdgeType.Ident
                || t.Type == EdgeType.Greek || t.Type == EdgeType.Function
                || t.Type == EdgeType.Keyword
                || (t.Type == EdgeType.Op && t.Value == "(");
        }

        private static Hole Hole(int idx) => new Hole(idx);

        // ---------------- Entry ----------------

        public AstNode Parse()
        {
            var e = ParseRelation();
            return e ?? Hole(1);
        }

        // ---------------- Relation / Expr / Term / Postfix ----------------

        // Relation = Expr (RelOp Expr)*
        // RelOp ∈ {=, <, >, <=, >=, !=, <>}. Top-level UNIQUEMENT (à l'intérieur
        // d'un Group ou d'un Argument on reste sur Expr — convention math
        // classique : on n'écrit pas "lim x 0 (f(x) = 1)" mais "lim x 0 f(x)").
        private AstNode? ParseRelation()
        {
            var lhs = ParseExpr();
            if (lhs == null) return null;
            while (IsRelOp())
            {
                var op = Consume();
                var rhs = ParseExpr() ?? (AstNode)Hole(1);
                lhs = new Bin(op.Value, op.Tight ?? false, false, lhs, rhs);
            }
            return lhs;
        }

        private bool IsRelOp()
        {
            var t = Peek();
            if (t == null || t.Type != EdgeType.Op) return false;
            switch (t.Value)
            {
                case "=":
                case "<":
                case ">":
                case "<=":
                case ">=":
                case "!=":
                case "<>":
                    return true;
                default:
                    return false;
            }
        }

        // Expr = Term ((+|-) Term)*
        private AstNode? ParseExpr()
        {
            var lhs = ParseTerm();
            if (lhs == null) return null;
            while (IsOp("+", "-"))
            {
                var op = Consume();
                var rhs = ParseTerm() ?? (AstNode)Hole(1);
                lhs = new Bin(op.Value, op.Tight ?? false, false, lhs, rhs);
            }
            return lhs;
        }

        // Term = Postfix ((*|/) Postfix | implicitMult)*
        private AstNode? ParseTerm()
        {
            var lhs = ParsePostfix();
            if (lhs == null) return null;
            while (true)
            {
                if (IsOp("*", "/"))
                {
                    var op = Consume();
                    var rhs = ParsePostfix() ?? (AstNode)Hole(1);
                    lhs = new Bin(op.Value, op.Tight ?? false, false, lhs, rhs);
                }
                else if (CanStartFactor())
                {
                    var tight = IsTightAdjacent();
                    var rhs = ParsePostfix();
                    if (rhs == null) break;
                    lhs = new Bin("*", tight, true, lhs, rhs);
                }
                else break;
            }
            return lhs;
        }

        // Postfix = Primary (^ Argument | _ Argument | NumberTight)*
        //
        // Règle "Number tight implicite = exposant" : si après un primary
        // non-Number on voit un Number adjacent tight (sans espace), on
        // l'interprète comme exposant (typo français : x² = x^2, cos²(x)).
        // Ne s'applique pas si le primary EST un Number — "23" reste "23",
        // pas "2 puissance 3".
        // C'est cette règle qui rend les exemples user :
        //   x2     → x²       (Atom + Number tight)
        //   cos2(x) → cos²(x)  (Func + Number tight, l'arg (x) suit comme
        //                       multiplication implicite tight = arg de la
        //                       fonction sublimée par la règle "Func arg")
        // Mais préserve :
        //   2x     → 2*x       (Number + atom : Number n'est pas le primary)
        //   23     → 23        (Number + Number : règle inactive)
        private AstNode? ParsePostfix()
        {
            var @base = ParsePrimary();
            if (@base == null) return null;
            while (true)
            {
                if (IsOp("^", "_"))
                {
                    var op = Consume();
                    var arg = ParseArgument() ?? (AstNode)Hole(1);
                    @base = op.Value == "^" ? (AstNode)new Sup(@base, arg) : new Sub(@base, arg);
                    continue;
                }
                if (Peek() is { Type: EdgeType.Number } numTok && IsTightAdjacent()
                    && !(@base is Atom a && a.Kind == "number"))
                {
                    Consume();
                    @base = new Sup(@base, new Atom("number", numTok.Value));
                    continue;
                }
                break;
            }
            return @base;
        }

        // Primary = Unary | Atom | Group | Scope | Func
        private AstNode? ParsePrimary()
        {
            var t = Peek();
            if (t == null) return null;

            // Unary - ou +
            if (t.Type == EdgeType.Op && (t.Value == "-" || t.Value == "+"))
            {
                var op = Consume();
                var arg = ParsePrimary() ?? (AstNode)Hole(1);
                return new Unary(op.Value, arg);
            }
            // Group
            if (t.Type == EdgeType.Op && t.Value == "(")
            {
                Consume();
                var e = ParseExpr() ?? (AstNode)Hole(1);
                if (IsOp(")")) Consume();
                return new Group(e);
            }
            if (t.Type == EdgeType.Keyword) return ParseScope();
            if (t.Type == EdgeType.Function)
            {
                Consume();
                var arg = ParseArgument() ?? (AstNode)Hole(1);
                // Application de la règle générique "Number tight après nom =
                // exposant" au cas particulier d'une fonction. Le flow de
                // ParseArgument absorbe le Number ET le Group qui suit dans
                // un Bin(*, implicit, tight) — on remap ici en Sup(Func, Num)
                // quand le second opérande est un Group (parens explicites).
                // Sans parens (cos2x), on conserve la mult implicite arg-of.
                if (arg is Bin bin && bin.Op == "*" && bin.Implicit && bin.Tight
                    && bin.Lhs is Atom lhsAtom && lhsAtom.Kind == "number"
                    && bin.Rhs is Group)
                {
                    return new Sup(new Func(t.Value, bin.Rhs), bin.Lhs);
                }
                return new Func(t.Value, arg);
            }
            if (t.Type == EdgeType.Number) { Consume(); return new Atom("number", t.Value); }
            if (t.Type == EdgeType.Ident)  { Consume(); return new Atom("ident",  t.Value); }
            if (t.Type == EdgeType.Greek)  { Consume(); return new Atom("greek",  t.Value); }
            return null;
        }

        // ---------------- Argument / TightChain / Body ----------------

        // Argument = Atom | Group | TightChain | Unary
        private AstNode? ParseArgument()
        {
            var t = Peek();
            if (t == null) return null;
            if (t.Type == EdgeType.Op && t.Value == "(") return ParsePrimary();
            if (t.Type == EdgeType.Op && (t.Value == "-" || t.Value == "+")) return ParseTightChain();
            if (CanStartFactor()) return ParseTightChain();
            return null;
        }

        // TightChain = Postfix (TightOp Postfix | tightAdjacent Postfix)*
        private AstNode? ParseTightChain()
        {
            var lhs = ParsePostfix();
            if (lhs == null) return null;
            while (true)
            {
                var t = Peek();
                if (t == null) break;
                if (t.Type == EdgeType.Op
                    && (t.Value == "+" || t.Value == "-" || t.Value == "*" || t.Value == "/")
                    && t.Tight == true)
                {
                    var op = Consume();
                    var rhs = ParsePostfix() ?? (AstNode)Hole(1);
                    lhs = new Bin(op.Value, true, false, lhs, rhs);
                    continue;
                }
                if (CanStartFactor() && IsTightAdjacent())
                {
                    var rhs = ParsePostfix();
                    if (rhs == null) break;
                    lhs = new Bin("*", true, true, lhs, rhs);
                    continue;
                }
                break;
            }
            return lhs;
        }

        // Body = Postfix (TightOp Postfix | adjacent Postfix)*
        // S'arrête au premier opérateur binaire loose (cf. algorithm.md §4 « règle du body »).
        private AstNode? ParseBody()
        {
            var lhs = ParsePostfix();
            if (lhs == null) return null;
            while (true)
            {
                var t = Peek();
                if (t == null) break;
                // Stop au binop loose
                if (t.Type == EdgeType.Op
                    && (t.Value == "+" || t.Value == "-" || t.Value == "*" || t.Value == "/")
                    && t.Tight == false)
                    break;
                // Tight op
                if (t.Type == EdgeType.Op
                    && (t.Value == "+" || t.Value == "-" || t.Value == "*" || t.Value == "/")
                    && t.Tight == true)
                {
                    var op = Consume();
                    var rhs = ParsePostfix() ?? (AstNode)Hole(1);
                    lhs = new Bin(op.Value, true, false, lhs, rhs);
                    continue;
                }
                // Multiplication implicite (adjacence tight ou loose)
                if (CanStartFactor())
                {
                    var tight = IsTightAdjacent();
                    var rhs = ParsePostfix();
                    if (rhs == null) break;
                    lhs = new Bin("*", tight, true, lhs, rhs);
                    continue;
                }
                break;
            }
            return lhs;
        }

        // Atome simple uniquement, utilisé pour le var de Sum/Prod
        private AstNode? ParseAtomOnly()
        {
            var t = Peek();
            if (t == null) return null;
            if (t.Type == EdgeType.Number) { Consume(); return new Atom("number", t.Value); }
            if (t.Type == EdgeType.Ident)  { Consume(); return new Atom("ident",  t.Value); }
            if (t.Type == EdgeType.Greek)  { Consume(); return new Atom("greek",  t.Value); }
            return null;
        }

        // ---------------- Scopes ----------------

        private AstNode ParseScope()
        {
            var kw = Consume();

            switch (kw.Value)
            {
                case "lim":
                {
                    var v = ParseArgument() ?? (AstNode)Hole(1);
                    if (IsOp("->")) Consume();
                    var target = ParseArgument() ?? (AstNode)Hole(2);
                    var body = ParseBody() ?? (AstNode)Hole(3);
                    return new Lim(v, target, body);
                }
                case "sum":
                case "somme":
                case "prod":
                case "produit":
                {
                    var symbol = (kw.Value == "sum" || kw.Value == "somme") ? "sum" : "prod";
                    var v = ParseAtomOnly() ?? (AstNode)Hole(1);
                    if (IsOp("=")) Consume();
                    var start = ParseArgument() ?? (AstNode)Hole(2);
                    var end = ParseArgument() ?? (AstNode)Hole(3);
                    var body = ParseBody() ?? (AstNode)Hole(4);
                    return new Sum(symbol, v, start, end, body);
                }
                case "int":
                case "integrale":
                case "intégrale":
                {
                    var low = ParseArgument() ?? (AstNode)Hole(1);
                    var high = ParseArgument() ?? (AstNode)Hole(2);
                    var body = ParseBody() ?? (AstNode)Hole(3);
                    return new Int(low, high, body);
                }
                case "racine":
                case "sqrt":
                case "rac":
                {
                    var arg = ParseArgument() ?? (AstNode)Hole(1);
                    return new Sqrt(arg);
                }
                case "frac":
                {
                    var num = ParseArgument() ?? (AstNode)Hole(1);
                    var den = ParseArgument() ?? (AstNode)Hole(2);
                    return new Frac(num, den);
                }
                case "vec":
                case "vecteur":
                {
                    var name = string.Empty;
                    while (Peek() is { Type: EdgeType.Ident } id)
                    {
                        name += id.Value;
                        Consume();
                    }
                    return new Vec(name.Length > 0 ? name : null);
                }
                case "inf":
                case "infini":
                case "infinity":
                    return new Const("\\infty");
                case "forall":
                    return new Const("\\forall");
                case "exists":
                    return new Const("\\exists");
                case "in":
                case "appartient":
                    return new Const("\\in");
            }

            // Fallback : keyword inconnu, on l'expose comme atome inconnu
            return new Atom("unknown", kw.Value);
        }
    }
}
