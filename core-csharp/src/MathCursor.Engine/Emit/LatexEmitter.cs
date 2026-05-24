using System.Collections.Generic;
using System.Text;
using MathCursor.Engine.Ast;

namespace MathCursor.Engine.Emit
{
    /// <summary>
    /// Rend un <see cref="AstNode"/> en LaTeX. Brief v4 §1.5 : les slots vides
    /// / placeholders deviennent <c>\square</c> sur entrée incomplète.
    ///
    /// <para>Le rendu est <b>direct depuis l'AST</b> — utilisé pour les
    /// expressions plates (operands flat de <see cref="MathEngine"/>, slots
    /// d'ancres). Les <see cref="AnchorNode"/> passent par
    /// <see cref="TemplateEmitter"/> qui applique le template d'emit de la
    /// règle YAML correspondante.</para>
    /// </summary>
    public sealed class LatexEmitter
    {
        private readonly bool _preferSubscript;

        public LatexEmitter() : this(false) { }

        /// <summary>
        /// P31 (2026-05-22) : <paramref name="preferSubscript"/> = true rend
        /// la juxtaposition letter+number en <c>_{}</c> au lieu de <c>^{}</c>.
        /// Utilisé par <see cref="Collision.Detectors.LetterSupSubDetector"/>
        /// pour générer l'alt indice sans scanner le LaTeX rendu (brief v5 §5).
        /// </summary>
        public LatexEmitter(bool preferSubscript)
        {
            _preferSubscript = preferSubscript;
        }

        public string Emit(AstNode? node)
        {
            if (node == null) return string.Empty;
            var sb = new StringBuilder();
            Render(node, sb);
            return sb.ToString();
        }

        private void Render(AstNode node, StringBuilder sb)
        {
            switch (node)
            {
                case AtomNode atom:
                    sb.Append(EscapeAtom(atom.Text, atom.AtomKind));
                    break;
                case PlaceholderNode:
                    sb.Append(@"\square");
                    break;
                case InfixNode infix:
                    RenderInfix(infix, sb);
                    break;
                case GroupNode group:
                    RenderGroup(group, sb);
                    break;
                case ListNode list:
                    RenderList(list, sb);
                    break;
                case MatrixNode matrix:
                    RenderMatrix(matrix, sb);
                    break;
                case LineNode line:
                    RenderLine(line, sb);
                    break;
                case UnaryPrefixNode unary:
                    // Op collé à l'opérande (conv math compact, cohérent avec
                    // IsArithmeticOp pour `+`/`-`). Cf. ADR
                    // 2026-05-23-Fix-engine-leading-unary-prefix.
                    sb.Append(unary.Op);
                    Render(unary.Operand, sb);
                    break;
                case MultiLineBlockNode mb:
                    RenderMultiLineBlock(mb, sb);
                    break;
            }
        }

        /// <summary>
        /// Rend un bloc multi-ligne : <c>\begin{align*}</c> avec préfixes
        /// par ligne, ou <c>\begin{cases}</c> avec lignes juxtaposées. Port
        /// direct du legacy <c>LatexRenderingVisitor.Visit(MultiLineBlock)</c>.
        /// Cf. ADR 2026-05-23-Feat-engine-v2-multiline-port.
        /// </summary>
        private void RenderMultiLineBlock(MultiLineBlockNode mb, StringBuilder sb)
        {
            if (mb.Mode == "align")
            {
                sb.Append("\\begin{align*} ");
                for (int i = 0; i < mb.Lines.Count; i++)
                {
                    if (i > 0) sb.Append(" \\\\ ");
                    string prefix = (i < mb.LinePrefix.Count) ? mb.LinePrefix[i] : "";
                    if (!string.IsNullOrEmpty(prefix)) sb.Append(prefix);
                    Render(mb.Lines[i], sb);
                }
                sb.Append(" \\end{align*}");
                return;
            }
            if (mb.Mode == "cases")
            {
                sb.Append("\\begin{cases} ");
                for (int i = 0; i < mb.Lines.Count; i++)
                {
                    if (i > 0) sb.Append(" \\\\ ");
                    Render(mb.Lines[i], sb);
                }
                sb.Append(" \\end{cases}");
            }
        }

        private static string EscapeAtom(string text, string atomKind)
        {
            // Pour les nombres FR : virgule décimale → garder telle quelle (LaTeX
            // l'accepte). Cas anglais peut nécessiter substitution → P12.
            return text;
        }

        private void RenderInfix(InfixNode infix, StringBuilder sb)
        {
            // Spécial : `\cdot` issu d'une juxtaposition (= produit implicite
            // brief §4). On le rend avec un espace fin discret pour rester
            // lisible mais sans symbole visible. Cf. golden : (a b) → ab.
            if (infix.Op == @"\cdotIM")
            {
                // Produit implicite (= juxtaposition `2x`, `ab`). Sans symbol.
                // Convention math : `<base><number>` collé → `<base>^{number}`
                // pour les bases exposables (= letter atom OU groupe). Couvre
                // `x2`, `(x+1)2`, `cos(x)2` (= user-report 2026-05-23).
                if (infix.Right is AtomNode rightAtom
                    && rightAtom.AtomKind == "number"
                    && MathCursor.Engine.Parsing.PrecedenceClimber.IsExponentBase(infix.Left))
                {
                    // P31 : alt indice via _preferSubscript.
                    string op = _preferSubscript ? "_" : "^";
                    Render(infix.Left, sb);
                    sb.Append(op).Append('{').Append(rightAtom.Text).Append('}');
                    return;
                }
                Render(infix.Left, sb);
                Render(infix.Right, sb);
                return;
            }

            if (infix.Op == @"\cdot")
            {
                // Produit explicite (`.` ou `*`). Avec espaces autour.
                Render(infix.Left, sb);
                sb.Append(" \\cdot ");
                Render(infix.Right, sb);
                return;
            }

            // Division : si les deux opérandes sont "simples" (atoms ou groupes)
            // on rend en `\frac{a}{b}`. Sinon `a / b` (fallback inline).
            if (infix.Op == "/")
            {
                sb.Append(@"\frac{");
                Render(infix.Left, sb);
                sb.Append("}{");
                Render(infix.Right, sb);
                sb.Append('}');
                return;
            }

            // Puissance : a^b → garde le format LaTeX natif (= b dans {} si non-atom).
            if (infix.Op == "^" || infix.Op == "_")
            {
                Render(infix.Left, sb);
                sb.Append(infix.Op);
                if (infix.Right is AtomNode)
                {
                    Render(infix.Right, sb);
                }
                else
                {
                    sb.Append('{');
                    Render(infix.Right, sb);
                    sb.Append('}');
                }
                return;
            }

            // Cas standard : a OP b.
            // Pas d'espace autour de +/- (= conv math compact).
            // Espaces autour des relations (= comp, rel, connecteurs).
            string sep = IsArithmeticOp(infix.Op) ? "" : " ";
            Render(infix.Left, sb);
            sb.Append(sep).Append(infix.Op).Append(sep);
            Render(infix.Right, sb);
        }

        private static bool IsArithmeticOp(string op)
        {
            return op == "+" || op == "-";
        }

        private void RenderGroup(GroupNode group, StringBuilder sb)
        {
            // Body matrix : on omet les délimiteurs car `\begin{pmatrix}` les fournit.
            if (group.Body is MatrixNode mat)
            {
                RenderMatrix(mat, sb);
                return;
            }
            // Brief §5 : utilise parenthèses courtes `(...)` (= matche les
            // golden cases). LaTeX `\left/\right` introduit du bruit visuel
            // sans valeur ajoutée pour les expressions courtes — réservé aux
            // grosses constructions (fractions imbriquées, etc.).
            sb.Append(group.Open);
            if (group.Body != null) Render(group.Body, sb);
            sb.Append(group.Close);
        }

        private void RenderList(ListNode list, StringBuilder sb)
        {
            string sep = list.Sep switch
            {
                "," => ",",
                ";" => " ; ",
                " " => " ",
                _   => list.Sep,
            };
            for (int i = 0; i < list.Items.Count; i++)
            {
                if (i > 0) sb.Append(sep);
                Render(list.Items[i], sb);
            }
        }

        private void RenderMatrix(MatrixNode matrix, StringBuilder sb)
        {
            sb.Append(@"\begin{pmatrix}");
            for (int r = 0; r < matrix.RowCount; r++)
            {
                if (r > 0) sb.Append(@" \\ ");
                for (int c = 0; c < matrix.ColCount; c++)
                {
                    if (c > 0) sb.Append(" & ");
                    Render(matrix.Rows[r][c], sb);
                }
            }
            sb.Append(@"\end{pmatrix}");
        }

        private void RenderLine(LineNode line, StringBuilder sb)
        {
            for (int i = 0; i < line.Cells.Count; i++)
            {
                if (i > 0) sb.Append(" & ");
                Render(line.Cells[i], sb);
            }
        }

    }
}
