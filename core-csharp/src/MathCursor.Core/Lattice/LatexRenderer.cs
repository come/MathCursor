using MathCursor.Core.Lattice.Ast;

namespace MathCursor.Core.Lattice
{
    /// <summary>
    /// Renderer AST → LaTeX. Récursion triviale, une branche par type de nœud.
    /// Port direct du proto JSX (cf. algorithm.md §5).
    ///
    /// Les <see cref="Hole"/> sont rendus en glyphe Unicode brut (① ② ③ …) :
    /// pas de <c>\color</c> pour ne pas polluer l'insertion Word/OMath. Si
    /// l'utilisateur valide une formule incomplète, le glyphe apparaît tel quel
    /// dans le document — c'est un choix produit assumé.
    ///
    /// Pas de Tab autocomplete : les espaces dans la saisie servent de
    /// séparateurs entre slots ; pas besoin de naviguer entre Holes.
    /// </summary>
    public static class LatexRenderer
    {
        public static string Render(AstNode? node) => node switch
        {
            null => string.Empty,
            Hole h => Circled(h.Idx),
            Atom a => RenderAtom(a),
            Const c => c.Value,
            Unary u => $"{u.Op}{Render(u.Arg)}",
            Bin b => RenderBin(b),
            Sup s => $"{Render(s.Base)}^{{{Render(Unwrap(s.Exp))}}}",
            Sub s => $"{Render(s.Base)}_{{{Render(Unwrap(s.Idx))}}}",
            Group g => $"\\left({Render(g.Expr)}\\right)",
            Frac f => $"\\frac{{{Render(Unwrap(f.Num))}}}{{{Render(Unwrap(f.Den))}}}",
            Sqrt sq => $"\\sqrt{{{Render(Unwrap(sq.Arg))}}}",
            Vec v => v.Name != null ? $"\\vec{{{v.Name}}}" : $"\\vec{{{Circled(1)}}}",
            Func fn => RenderFunc(fn),
            Sum sum => RenderSum(sum),
            Lim lim => $"\\lim_{{{Render(lim.Var)} \\to {Render(Unwrap(lim.Target))}}} {Render(lim.Body)}",
            Int it => $"\\int_{{{Render(Unwrap(it.Low))}}}^{{{Render(Unwrap(it.High))}}} {Render(it.Body)}",
            _ => string.Empty,
        };

        // Si l'argument est un Group, on renvoie son contenu sans les parens.
        // Le contexte structurel (les {} de KaTeX, la barre de fraction, etc.)
        // groupe déjà visuellement.
        private static AstNode Unwrap(AstNode node) => node is Group g ? g.Expr : node;

        private static string RenderAtom(Atom a) => a.Kind switch
        {
            "greek" => $"\\{a.Value}",
            _ => a.Value,
        };

        private static string RenderBin(Bin b)
        {
            var lhs = Render(b.Lhs);
            var rhs = Render(b.Rhs);
            if (b.Op == "*" && b.Implicit) return $"{lhs}{rhs}";
            if (b.Op == "*") return $"{lhs}\\cdot {rhs}";
            return $"{lhs}{b.Op}{rhs}";
        }

        private static string RenderFunc(Func fn)
        {
            var a = fn.Arg;
            // Atome / greek / hole : un seul espace entre la fonction et l'argument.
            if (a is Atom || a is Hole) return $"\\{fn.Name} {Render(a)}";
            // Bin implicit tight (ex: 2x dans cos2x) : pas besoin de parens
            if (a is Bin b && b.Implicit && b.Tight) return $"\\{fn.Name} {Render(a)}";
            // Group : on garde tel quel (les parens font partie du Group)
            if (a is Group) return $"\\{fn.Name}{Render(a)}";
            // Sinon on enrobe dans des parens pour clarifier
            return $"\\{fn.Name}\\left({Render(a)}\\right)";
        }

        private static string RenderSum(Sum sum)
        {
            var sym = sum.Symbol == "sum" ? "\\sum" : "\\prod";
            return $"{sym}_{{{Render(sum.Var)}={Render(Unwrap(sum.Start))}}}^{{{Render(Unwrap(sum.End))}}} {Render(sum.Body)}";
        }

        // ① ② ③ … pour les holes. Au-delà de 9, fallback texte (rare).
        private static readonly string[] CircledGlyphs =
        {
            "①", "②", "③", "④", "⑤", "⑥", "⑦", "⑧", "⑨",
        };

        private static string Circled(int idx)
        {
            if (idx >= 1 && idx <= 9) return CircledGlyphs[idx - 1];
            return $"({idx})";
        }
    }
}
