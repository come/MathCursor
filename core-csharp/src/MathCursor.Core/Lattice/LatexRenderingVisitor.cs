using MathCursor.Core.Lattice.Ast;

namespace MathCursor.Core.Lattice
{
    /// <summary>
    /// Visiteur AST → LaTeX. Implémente <see cref="IAstVisitor{TResult}"/>
    /// avec <c>TResult = string</c>. Remplace le <c>switch (node)</c>
    /// exhaustif de l'ancien <see cref="LatexRenderer.Render"/> par un
    /// dispatch virtuel typé (étape 4 du refacto extensibilité).
    ///
    /// <para>Bénéfice : l'ajout d'un nouveau type AST devient une erreur de
    /// compilation tant que ce visiteur (et les autres) n'a pas implémenté
    /// la méthode <c>Visit</c> correspondante. Fini les <c>case</c> manquants
    /// qui retombent silencieusement sur <c>_ =&gt; string.Empty</c>.</para>
    ///
    /// <para>Cf. ADR <c>2026-05-13-Refactor-ast-visitor.md</c>.</para>
    /// </summary>
    internal sealed class LatexRenderingVisitor : IAstVisitor<string>
    {
        // Carré vide standard, rendu cohérent par WpfMath (popup) et Word OMath
        // BuildUp (boîte vide native d'éditeur d'équation).
        private const string HoleLatex = "\\square ";

        private readonly RenderOptions _options;

        public LatexRenderingVisitor(RenderOptions options)
        {
            _options = options ?? new RenderOptions();
        }

        /// <summary>Render un sous-AST (null-safe). Utilisé par tous les
        /// Visit qui doivent rendre récursivement leurs enfants.</summary>
        private string Render(AstNode? node) => node?.Accept(this) ?? string.Empty;

        // Si l'argument est un Group, on renvoie son contenu sans les parens.
        // Le contexte structurel (les {} de KaTeX, la barre de fraction, etc.)
        // groupe déjà visuellement.
        private static AstNode Unwrap(AstNode node) => node is Group g ? g.Expr : node;

        // ============================================================
        // Visitor dispatch — un Visit par type AST (alphabétique grosso modo)
        // ============================================================

        public string Visit(Atom node) => node.Kind switch
        {
            "greek" => $"\\{node.Value}",
            _ => node.Value,
        };

        public string Visit(Hole node) => HoleLatex;

        public string Visit(Const node) => node.Value;

        public string Visit(Unary node) => $"{node.Op}{Render(node.Arg)}";

        public string Visit(Bin node) => RenderBin(node);

        public string Visit(Sup node)
            => $"{Render(node.Base)}^{{{Render(Unwrap(node.Exp))}}}";

        public string Visit(Sub node)
            => $"{Render(node.Base)}_{{{Render(Unwrap(node.Idx))}}}";

        public string Visit(Group node)
            => $"\\left({Render(node.Expr)}\\right)";

        public string Visit(Frac node)
            => $"\\frac{{{Render(Unwrap(node.Num))}}}{{{Render(Unwrap(node.Den))}}}";

        public string Visit(Sqrt node)
            => $"\\sqrt{{{Render(Unwrap(node.Arg))}}}";

        public string Visit(Vec node)
            => node.Name != null ? $"\\vec{{{node.Name}}}" : $"\\vec{{{HoleLatex}}}";

        /// <summary>
        /// Rendu d'un angle (notation chapeau française) :
        /// <list type="bullet">
        /// <item>1 lettre → <c>\hat{X}</c></item>
        /// <item>2+ lettres → <c>\widehat{XYZ}</c></item>
        /// <item><c>HasPlaceholder=true</c> → on ajoute un <c>\square</c>
        /// dans le nom rendu, signalant à l'utilisateur qu'une lettre
        /// manque (ex. <c>^AB</c> → <c>\widehat{AB\square}</c>).</item>
        /// </list>
        /// Cf. ADR <c>2026-05-11-Feat-angle-notation-caret-and-keyword</c>.
        /// </summary>
        public string Visit(Angle node)
        {
            string name = node.Name ?? string.Empty;
            string rendered = node.HasPlaceholder ? name + "\\square " : name;
            // 1 lettre (sans placeholder) → \hat. Sinon \widehat (cas
            // multi-lettres OU 1 lettre + placeholder = 2 chars visuels).
            bool useWide = node.HasPlaceholder || name.Length >= 2;
            string cmd = useWide ? "widehat" : "hat";
            return $"\\{cmd}{{{rendered}}}";
        }

        public string Visit(Func node)
        {
            // Convention typographique française : exp(x), exp(x+1) sont
            // rendus en notation puissance e^{x}, e^{x+1}. Le Group autour
            // de l'arg est unwrappé (la barre d'exposant joue le rôle de
            // regroupement visuel comme pour Frac/Sup).
            if (node.Name == "exp")
                return $"e^{{{Render(Unwrap(node.Arg))}}}";

            var a = node.Arg;
            // Atome / greek / hole : un seul espace entre la fonction et l'argument.
            if (a is Atom || a is Hole) return $"\\{node.Name} {Render(a)}";
            // Bin implicit tight (ex: 2x dans cos2x) : pas besoin de parens
            if (a is Bin b && b.Implicit && b.Tight) return $"\\{node.Name} {Render(a)}";
            // Group : on garde tel quel (les parens font partie du Group)
            if (a is Group) return $"\\{node.Name}{Render(a)}";
            // Sinon on enrobe dans des parens pour clarifier
            return $"\\{node.Name}\\left({Render(a)}\\right)";
        }

        public string Visit(Sum node)
        {
            var sym = node.Symbol == "sum" ? "\\sum" : "\\prod";
            return $"{sym}_{{{Render(node.Var)}={Render(Unwrap(node.Start))}}}^{{{Render(Unwrap(node.End))}}} {Render(node.Body)}";
        }

        public string Visit(Lim node)
            => $"\\lim_{{{Render(node.Var)} \\to {Render(Unwrap(node.Target))}}} {Render(node.Body)}";

        public string Visit(Int node)
            => $"\\int_{{{Render(Unwrap(node.Low))}}}^{{{Render(Unwrap(node.High))}}} {Render(node.Body)}";

        // Intervalle français : brackets bruts (pas \left/\right) parce que
        // \left] / \right[ ne sont pas universellement supportés (WpfMath,
        // Word OMath BuildUp). Le rendu reste lisible pour les cas typiques
        // (bornes numériques ou identifiants courts).
        public string Visit(Interval node)
        {
            var leftBr = node.LeftClosed ? "[" : "]";
            var rightBr = node.RightClosed ? "]" : "[";
            return $"{leftBr}{Render(Unwrap(node.Low))},{Render(Unwrap(node.High))}{rightBr}";
        }

        // Définition de fonction : f: x ↦ expr (1 var) ou f: (x,y) ↦ expr
        // (n-uplet auto-parenthésé). Convention lycée FR avec \mapsto distinct
        // de = (égalité simple). Espacement serré : pas d'espace avant `:`.
        public string Visit(FuncDef node)
        {
            var body = Render(Unwrap(node.Body));
            if (node.Vars.Count == 1)
                return $"{node.Name}: {Render(node.Vars[0])} \\mapsto {body}";
            var parts = new System.Collections.Generic.List<string>();
            foreach (var v in node.Vars) parts.Add(Render(v));
            return $"{node.Name}: ({string.Join(",", parts)}) \\mapsto {body}";
        }

        // VectorCoordinates : 4 combinaisons (col/row × vec/point).
        //   layout=column, isPoint=false : \vec{name} \begin{pmatrix} v1 \\ v2 [\\ v3] \end{pmatrix}
        //   layout=column, isPoint=true  : name \begin{pmatrix} v1 \\ v2 [\\ v3] \end{pmatrix}
        //   layout=row, isPoint=false    : \vec{name}(v1, v2[, v3])
        //   layout=row, isPoint=true     : name(v1, v2[, v3])
        // Cf. brief 2026-04-29-vector-coordinates-shorthand §4.4.
        public string Visit(VectorCoordinates node)
        {
            var prefix = node.IsPoint ? node.Name : $"\\vec{{{node.Name}}}";
            var rendered = new System.Collections.Generic.List<string>(node.Values.Count);
            foreach (var v in node.Values) rendered.Add(Render(Unwrap(v)));
            if (node.Layout == "column")
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

        // MultiLineBlock : système (\begin{cases}) ou chaîne d'équivalences/
        // égalités (\begin{align*}). Cf. brief 30-04 multiline-systems.
        public string Visit(MultiLineBlock node)
        {
            if (node.Mode == "align")
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("\\begin{align*} ");
                for (int i = 0; i < node.Lines.Count; i++)
                {
                    if (i > 0) sb.Append(" \\\\ ");
                    string prefix = (i < node.LinePrefix.Count) ? node.LinePrefix[i].TrimEnd() : "";
                    sb.Append(RenderAlignLineWithPrefix(prefix, node.Lines[i], i));
                }
                sb.Append(" \\end{align*}");
                return sb.ToString();
            }
            // Phase 2 : cases (système d'équations) avec alignement intra-bloc
            // sur la relation `=` (cf. user feedback 05-05 « manque juste un
            // alignement à l'interieur »). Format Word matrix avec 2 colonnes :
            //   Col 1 (r) : lhs (ou expression complète si pas de relation)
            //   Col 2 (l) : `op rhs` (= alignement vertical sur `=`)
            // Ligne sans relation : col2 vide pour cohérence matricielle.
            if (node.Mode == "cases")
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("\\begin{cases} ");
                for (int i = 0; i < node.Lines.Count; i++)
                {
                    if (i > 0) sb.Append(" \\\\ ");
                    sb.Append(RenderCasesLine(node.Lines[i]));
                }
                sb.Append(" \\end{cases}");
                return sb.ToString();
            }
            return string.Empty;
        }

        // ============================================================
        // Helpers privés (logique métier des Visit complexes)
        // ============================================================

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
        private string RenderBin(Bin b)
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
                    return $"{lhs}{_options.MultSymbol}{rhs}";
                return $"{lhs}{rhs}";
            }
            // `*` explicite : Vec*Vec forcé `\cdot` (convention produit scalaire,
            // indépendamment du setting). Sinon symbole selon GlobalOptions
            // (`\times` FR par défaut, `\cdot` autres cultures).
            if (b.Op == "*")
            {
                if (b.Lhs is Vec && b.Rhs is Vec) return $"{lhs}\\cdot {rhs}";
                return $"{lhs}{_options.MultSymbol}{rhs}";
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

        /// <summary>
        /// Rendu d'une ligne de bloc cases avec 2 colonnes (1 <c>&amp;</c>) :
        ///   {lhs} &amp; {op} {rhs}
        /// pour aligner les <c>=</c> verticalement. Lignes sans relation : col1
        /// porte l'expression complète, col2 reste vide. Cf. ADR 05-05 cases.
        /// </summary>
        private string RenderCasesLine(AstNode line)
        {
            if (line is Bin b && IsAlignRelationOp(b.Op))
            {
                return $"{Render(b.Lhs)} & {b.Op} {Render(b.Rhs)}";
            }
            // Pas de relation : col2 vide pour matrice uniforme à 2 colonnes
            return $"{Render(line)} &";
        }

        /// <summary>
        /// Rendu d'une ligne de bloc align* avec 4 colonnes (3 `&`) :
        ///   {prefix} &amp; &amp; {lhs} &amp; {op} {rhs}
        /// Cf. doc dans <see cref="LatexRenderer"/>.
        /// </summary>
        private string RenderAlignLineWithPrefix(string prefix, AstNode line, int lineIndex)
        {
            if (line is Bin b && IsAlignRelationOp(b.Op))
            {
                return $"{prefix} & & {Render(b.Lhs)} & {b.Op} {Render(b.Rhs)}";
            }
            // Ligne sans relation. Cas chaîne `=` : marker consommé, lineIndex>0
            // et prefix vide → c'est le rhs d'une chaîne d'égalités, on préfixe
            // col4 avec `= ` pour que le `=` soit visible.
            if (lineIndex > 0 && string.IsNullOrEmpty(prefix))
            {
                return $"{prefix} & & & = {Render(line)}";
            }
            // Sinon (1re ligne, ou marker non-`=` mais pas de relation) :
            // col3 vide, expression brute en col4.
            return $"{prefix} & & & {Render(line)}";
        }

        private static bool IsAlignRelationOp(string op)
            => op == "=" || op == "<" || op == ">"
               || op == "<=" || op == ">=" || op == "!=" || op == "<>"
               || op == "≤" || op == "≥" || op == "≠";
    }
}
