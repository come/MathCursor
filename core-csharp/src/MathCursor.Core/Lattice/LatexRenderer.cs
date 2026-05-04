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

        /// <summary>
        /// Options de rendu globales (configurées par l'adapter au démarrage).
        /// Cf. <see cref="MathCursor.Core.RenderOptions"/>. Notamment :
        /// <see cref="RenderOptions.MultSymbol"/> (`\times` ou `\cdot` selon
        /// culture/Registry) appliqué au rendu de Bin("*") explicite.
        /// </summary>
        public static RenderOptions GlobalOptions { get; set; } = new RenderOptions();

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
            MultiLineBlock mb => RenderMultiLineBlock(mb),
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

        /// <summary>
        /// Rendu LaTeX d'un noeud <see cref="Bin"/>.
        /// <para>
        /// Contrat parser → renderer pour le cas <c>Hole LHS + opérateur de
        /// relation</c> (cf. ADR 2026-05-04 cross-merge pipeline) :
        /// </para>
        /// <list type="bullet">
        /// <item>Le parser injecte un <see cref="Hole"/> en LHS quand une zone
        /// ouvre par un RelOp (`&lt;=&gt; 2x`, `=&gt; x+1`, `= X = 2`...) — sans
        /// ça <see cref="Parser.ParseRelation"/> retournerait null et on tomberait
        /// sur <c>\square</c> seul.</item>
        /// <item>Le renderer DOIT omettre ce Hole — sinon la popup affiche
        /// <c>\square \Leftrightarrow 2x</c>, polluant. Le rendu correct est
        /// la relation orpheline : <c>\Leftrightarrow 2x</c>, qui reflète
        /// l'intention user (« je tape la suite d'une chaîne d'équivalences »).</item>
        /// <item>Cas chaîné <c>&lt;=&gt; X = 2</c> = <c>Bin(=, Bin(&lt;=&gt;, Hole, X), 2)</c> :
        /// seul le Hole DIRECT sous le Bin interne est strippé. Le Bin externe
        /// voit son LHS = un Bin (pas un Hole), pas de strip à ce niveau.</item>
        /// </list>
        /// Tests : <c>LatexRendererTests.RenderBin_*_with_hole_lhs_strips_hole</c>.
        /// </summary>
        private static string RenderBin(Bin b)
        {
            // Hole LHS + RelOp : relation orpheline (cf. doc XML ci-dessus).
            if (b.Lhs is Hole && IsRelationOp(b.Op))
                return $"{RelationOpAlone(b.Op)} {Render(b.Rhs)}";

            var lhs = Render(b.Lhs);
            var rhs = Render(b.Rhs);
            // `.` saisi par l'utilisateur → toujours `\cdot` (lecture littérale,
            // pas configurable). Cf. ADR Feat-dot-as-multiplier.
            if (b.Op == ".") return $"{lhs}\\cdot {rhs}";
            // Mult implicite (juxtaposition `2x`, `ab`) :
            // - Number-Number (`2 3`) : insère le symbole explicite (sinon `23`
            //   collé est mathématiquement faux). Cf. brief §5.ter.
            // - Sinon : concaténation pure (comportement existant).
            if (b.Op == "*" && b.Implicit)
            {
                if (b.Lhs is Atom la && la.Kind == "number"
                    && b.Rhs is Atom ra && ra.Kind == "number")
                    return $"{lhs}{GlobalOptions.MultSymbol}{rhs}";
                return $"{lhs}{rhs}";
            }
            // `*` explicite : Vec*Vec forcé `\cdot` (convention produit scalaire,
            // indépendamment du setting). Sinon symbole selon GlobalOptions
            // (`\times` FR par défaut, `\cdot` autres cultures).
            if (b.Op == "*")
            {
                if (b.Lhs is Vec && b.Rhs is Vec) return $"{lhs}\\cdot {rhs}";
                return $"{lhs}{GlobalOptions.MultSymbol}{rhs}";
            }
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

        private static bool IsRelationOp(string op) => op switch
        {
            "=" or "<" or ">" or "<=" or ">=" or "!=" or "<>" or
            "=>" or "==>" or "⇒" or "⟹" or
            "<=>" or "<==>" or "⇔" or "↔" or "⟺" or
            "<==" or "⇐" or "⟸" => true,
            _ => false,
        };

        private static string RelationOpAlone(string op) => op switch
        {
            "<=" => "\\leq",
            ">=" => "\\geq",
            "!=" or "<>" => "\\neq",
            "=>" or "==>" or "⇒" or "⟹" => "\\Rightarrow",
            "<=>" or "<==>" or "⇔" or "↔" or "⟺" => "\\Leftrightarrow",
            "<==" or "⇐" or "⟸" => "\\Leftarrow",
            _ => op,
        };

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

        // MultiLineBlock : système (\begin{cases}) ou chaîne d'équivalences/
        // égalités (\begin{align*}). Cf. brief 30-04 multiline-systems.
        // Phase 1 : uniquement le mode "align". Phase 2 ajoutera "cases".
        //
        // Format `\eqarray`-like à 3 `&` par ligne (4 cellules) :
        //   {prefix} & & {lhs} & {op} {rhs}
        // - Col 1 (r) : préfixe (flèche logique) ou vide
        //   → r-aligned, mais comme tous les préfixes ont la même largeur
        //     ils s'alignent visuellement (équivalent gauche-aligné)
        // - Col 2 (l) : vide (padding entre préfixe et lhs)
        // - Col 3 (r) : lhs de la relation
        //   → r-aligned, finit JUSTE AVANT le `=`
        // - Col 4 (l) : opérateur + rhs (`= ...`)
        //   → l-aligned, commence par `=` à la même position pour toutes
        //     les lignes
        // Cf. validation user 02-05 (« \eqarray(&&f(x)&=...@⇒&&g(x)&=...) c'est
        // ca que tu as fait ? ») — on aligne sur ce format.
        private static string RenderMultiLineBlock(MultiLineBlock mb)
        {
            if (mb.Mode == "align")
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("\\begin{align*} ");
                for (int i = 0; i < mb.Lines.Count; i++)
                {
                    if (i > 0) sb.Append(" \\\\ ");
                    string prefix = (i < mb.LinePrefix.Count) ? mb.LinePrefix[i].TrimEnd() : "";
                    sb.Append(RenderAlignLineWithPrefix(prefix, mb.Lines[i]));
                }
                sb.Append(" \\end{align*}");
                return sb.ToString();
            }
            // Phase 2 : cases. Pour l'instant fallback simple.
            if (mb.Mode == "cases")
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("\\begin{cases} ");
                for (int i = 0; i < mb.Lines.Count; i++)
                {
                    if (i > 0) sb.Append(" \\\\ ");
                    sb.Append(Render(mb.Lines[i]));
                }
                sb.Append(" \\end{cases}");
                return sb.ToString();
            }
            return string.Empty;
        }

        /// <summary>
        /// Rendu d'une ligne de bloc align* avec 4 colonnes (3 `&`) :
        ///   {prefix} &amp; &amp; {lhs} &amp; {op} {rhs}
        /// - Col 1 (r) : préfixe (flèche logique) ou vide pour 1re ligne
        /// - Col 2 (l) : vide (padding)
        /// - Col 3 (r) : lhs de la relation (finit juste avant `=`)
        /// - Col 4 (l) : `op rhs` (commence par `=` aligné en colonne)
        /// Si la ligne n'a pas de relation, col3 vide et expression en col4.
        /// </summary>
        private static string RenderAlignLineWithPrefix(string prefix, AstNode line)
        {
            if (line is Bin b && IsAlignRelationOp(b.Op))
            {
                return $"{prefix} & & {Render(b.Lhs)} & {b.Op} {Render(b.Rhs)}";
            }
            // Ligne sans relation : col3 vide, expression en col4
            return $"{prefix} & & & {Render(line)}";
        }

        private static bool IsAlignRelationOp(string op)
            => op == "=" || op == "<" || op == ">"
               || op == "<=" || op == ">=" || op == "!=" || op == "<>"
               || op == "≤" || op == "≥" || op == "≠";

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
            // Convention française (cf. ADR 30-04 Feat-french-semicolon-coordinates) :
            // séparateur de coordonnées = ` ; ` pour réserver la virgule au décimal.
            var inline = string.Join(" ; ", rendered);
            return $"{prefix}({inline})";
        }
    }
}
