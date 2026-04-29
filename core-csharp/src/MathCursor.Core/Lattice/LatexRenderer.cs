using MathCursor.Core.Lattice.Ast;

namespace MathCursor.Core.Lattice
{
    /// <summary>
    /// Renderer AST → LaTeX. Récursion triviale, une branche par type de nœud.
    /// Port direct du proto JSX (cf. algorithm.md §5).
    ///
    /// Les <see cref="Hole"/> sont rendus en <c>\square</c> (carré vide) :
    /// universel, supporté par WpfMath (popup) ET par Word OMath BuildUp
    /// (rend les boîtes vides natives de l'éditeur d'équation). On perd la
    /// numérotation visuelle ① vs ② mais on n'a pas de Tab autocomplete dans
    /// MathCursor (les espaces servent de séparateurs entre slots), donc
    /// l'idx du Hole n'a qu'une valeur informative qu'on accepte de sacrifier
    /// pour avoir un rendu qui marche partout.
    /// </summary>
    public static class LatexRenderer
    {
        // Carré vide standard, rendu cohérent par WpfMath (popup) et Word OMath
        // BuildUp (boîte vide native d'éditeur d'équation).
        private const string HoleLatex = "\\square ";

        public static string Render(AstNode? node) => node switch
        {
            null => string.Empty,
            Hole _ => HoleLatex,
            Atom a => RenderAtom(a),
            Const c => c.Value,
            Unary u => $"{u.Op}{Render(u.Arg)}",
            Bin b => RenderBin(b),
            Sup s => $"{Render(s.Base)}^{{{Render(Unwrap(s.Exp))}}}",
            Sub s => $"{Render(s.Base)}_{{{Render(Unwrap(s.Idx))}}}",
            Group g => $"\\left({Render(g.Expr)}\\right)",
            Frac f => $"\\frac{{{Render(Unwrap(f.Num))}}}{{{Render(Unwrap(f.Den))}}}",
            Sqrt sq => $"\\sqrt{{{Render(Unwrap(sq.Arg))}}}",
            Vec v => v.Name != null ? $"\\vec{{{v.Name}}}" : $"\\vec{{{HoleLatex}}}",
            Func fn => RenderFunc(fn),
            Sum sum => RenderSum(sum),
            Lim lim => $"\\lim_{{{Render(lim.Var)} \\to {Render(Unwrap(lim.Target))}}} {Render(lim.Body)}",
            Int it => $"\\int_{{{Render(Unwrap(it.Low))}}}^{{{Render(Unwrap(it.High))}}} {Render(it.Body)}",
            Interval iv => RenderInterval(iv),
            FuncDef fd => RenderFuncDef(fd),
            VectorCoordinates vc => RenderVectorCoordinates(vc),
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
            // Division explicite "/" → fraction empilée typographique. C'est la
            // convention math : un slash au clavier produit une fraction visuelle
            // (Word et WpfMath rendent \frac empilé, pas inline). On déballe les
            // Group autour des opérandes pour éviter les parens redondantes
            // (la barre de fraction joue déjà le rôle de regroupement visuel).
            if (b.Op == "/")
                return $"\\frac{{{Render(Unwrap(b.Lhs))}}}{{{Render(Unwrap(b.Rhs))}}}";
            // Relations multi-char : Word et WpfMath veulent les commandes LaTeX.
            // \leq, \geq, \neq sont rendus avec des espaces de chaque côté pour
            // la lisibilité (LaTeX gère l'espace contextuel mais Word non).
            if (b.Op == "<=") return $"{lhs} \\leq {rhs}";
            if (b.Op == ">=") return $"{lhs} \\geq {rhs}";
            if (b.Op == "!=" || b.Op == "<>") return $"{lhs} \\neq {rhs}";
            // Convention française : parallèle = deux barres OBLIQUES (//),
            // pas verticales (\parallel = ∥). On émet les slashes ASCII bruts,
            // WpfMath et Word OMath les rendent comme tels (visuellement
            // obliques, conformes à la notation lycée FR).
            if (b.Op == "//") return $"{lhs} // {rhs}";
            // Composition d'intervalles / ensembles
            if (b.Op == "union") return $"{lhs} \\cup {rhs}";
            if (b.Op == "inter") return $"{lhs} \\cap {rhs}";
            // Liste de variables / arguments (forall x,y, f(a,b)…)
            if (b.Op == ",") return $"{lhs},{rhs}";
            // Implication / équivalence (ADR 29-04). Toutes variantes ASCII et
            // Unicode (incl. Word AutoCorrect ↔ ⟺ ⟹ ⟸) mappées vers les
            // macros standards \Rightarrow, \Leftarrow, \Leftrightarrow.
            if (b.Op == "=>" || b.Op == "==>" || b.Op == "⇒" || b.Op == "⟹") return $"{lhs} \\Rightarrow {rhs}";
            if (b.Op == "<=>" || b.Op == "<==>" || b.Op == "⇔" || b.Op == "↔" || b.Op == "⟺") return $"{lhs} \\Leftrightarrow {rhs}";
            if (b.Op == "<==" || b.Op == "⇐" || b.Op == "⟸") return $"{lhs} \\Leftarrow {rhs}";
            return $"{lhs}{b.Op}{rhs}";
        }

        private static string RenderFunc(Func fn)
        {
            // Convention typographique française : exp(x), exp(x+1) sont
            // rendus en notation puissance e^{x}, e^{x+1}. Le Group autour
            // de l'arg est unwrappé (la barre d'exposant joue le rôle de
            // regroupement visuel comme pour Frac/Sup).
            if (fn.Name == "exp")
                return $"e^{{{Render(Unwrap(fn.Arg))}}}";

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

        // Définition de fonction : f: x ↦ expr (1 var) ou f: (x,y) ↦ expr
        // (n-uplet auto-parenthésé). Convention lycée FR avec \mapsto distinct
        // de = (égalité simple). Espacement serré : pas d'espace avant `:`.
        private static string RenderFuncDef(FuncDef fd)
        {
            var body = Render(Unwrap(fd.Body));
            if (fd.Vars.Count == 1)
                return $"{fd.Name}: {Render(fd.Vars[0])} \\mapsto {body}";
            var parts = new System.Collections.Generic.List<string>();
            foreach (var v in fd.Vars) parts.Add(Render(v));
            return $"{fd.Name}: ({string.Join(",", parts)}) \\mapsto {body}";
        }

        // Intervalle français : brackets bruts (pas \left/\right) parce que
        // \left] / \right[ ne sont pas universellement supportés (WpfMath,
        // Word OMath BuildUp). Le rendu reste lisible pour les cas typiques
        // (bornes numériques ou identifiants courts).
        private static string RenderInterval(Interval iv)
        {
            var leftBr = iv.LeftClosed ? "[" : "]";
            var rightBr = iv.RightClosed ? "]" : "[";
            return $"{leftBr}{Render(Unwrap(iv.Low))},{Render(Unwrap(iv.High))}{rightBr}";
        }

        // VectorCoordinates : 4 combinaisons (col/row × vec/point).
        //   layout=column, isPoint=false : \vec{name} \begin{pmatrix} v1 \\ v2 [\\ v3] \end{pmatrix}
        //   layout=column, isPoint=true  : name \begin{pmatrix} v1 \\ v2 [\\ v3] \end{pmatrix}
        //   layout=row, isPoint=false    : \vec{name}(v1, v2[, v3])
        //   layout=row, isPoint=true     : name(v1, v2[, v3])
        // Cf. brief 2026-04-29-vector-coordinates-shorthand §4.4.
        private static string RenderVectorCoordinates(VectorCoordinates vc)
        {
            var prefix = vc.IsPoint ? vc.Name : $"\\vec{{{vc.Name}}}";
            var rendered = new System.Collections.Generic.List<string>(vc.Values.Count);
            foreach (var v in vc.Values) rendered.Add(Render(Unwrap(v)));
            if (vc.Layout == "column")
            {
                var body = string.Join(" \\\\ ", rendered);
                return $"{prefix} \\begin{{pmatrix}} {body} \\end{{pmatrix}}";
            }
            // layout = "row"
            var inline = string.Join(", ", rendered);
            return $"{prefix}({inline})";
        }
    }
}
