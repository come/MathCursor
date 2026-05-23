using System.Collections.Generic;
using MathCursor.Engine.Ast;

namespace MathCursor.Engine.Parsing.List
{
    /// <summary>
    /// Combinateur de liste — brief v4 §1.3. Primitive unique
    /// <c>liste(X, sep) = X (sep X)*</c> avec types composés :
    /// <list type="bullet">
    ///   <item><c>line</c> = <c>expr (colsep expr)*</c></item>
    ///   <item><c>matrix</c> = <c>line (rowsep line)*</c></item>
    /// </list>
    ///
    /// <para>Force croissante vers l'extérieur : <c>expr &lt; colsep &lt; rowsep</c>.
    /// Déclenchement par présence de séparateur : type le plus riche dont le
    /// sep est présent.</para>
    ///
    /// <para>Opère sur les <see cref="ListNode"/> produits par
    /// <see cref="Parsing.StackParser"/> et les promeut en
    /// <see cref="MatrixNode"/> 2D quand le rowsep apparaît.</para>
    /// </summary>
    public static class ListCombinator
    {
        /// <summary>
        /// Promeut récursivement les <see cref="ListNode"/> en <see cref="MatrixNode"/>
        /// quand on rencontre un rowsep (<c>;</c>). Les cells deviennent des
        /// <see cref="LineNode"/> elles-mêmes (= colsep). Les autres nœuds sont
        /// retournés tels quels.
        /// </summary>
        public static AstNode? Promote(AstNode? node)
        {
            if (node == null) return null;
            switch (node)
            {
                case GroupNode g:
                    return new GroupNode(g.Open, g.Close, Promote(g.Body));
                case InfixNode i:
                    return new InfixNode(i.Op, Promote(i.Left)!, Promote(i.Right)!);
                case ListNode l when l.Sep == ";":
                    return PromoteMatrix(l);
                case ListNode l:
                    // Liste à séparateur ',' ou ' ' (non-rowsep) → reste liste,
                    // mais les enfants sont récursivement promus.
                    var promotedItems = new List<AstNode>(l.Items.Count);
                    foreach (var it in l.Items) promotedItems.Add(Promote(it) ?? PlaceholderNode.Instance);
                    return new ListNode(l.Sep, promotedItems);
                default:
                    return node;
            }
        }

        private static AstNode PromoteMatrix(ListNode rowList)
        {
            // Chaque item de rowList est une "ligne". Si l'item est lui-même
            // un ListNode de sep "," → c'est déjà une cell list ; sinon on
            // l'enveloppe en LineNode singleton.
            // Cas pratique brief : `(a b ; c d)` → item1=ListNode-space [a,b],
            // item2=ListNode-space [c,d]. On veut MatrixNode(rows=[[a,b],[c,d]]).
            var rows = new List<IReadOnlyList<AstNode>>(rowList.Items.Count);
            int maxCols = 0;
            foreach (var item in rowList.Items)
            {
                var cells = ExtractCells(item);
                rows.Add(cells);
                if (cells.Count > maxCols) maxCols = cells.Count;
            }

            // Padding rectangulaire : ajoute des Placeholder si une ligne est
            // plus courte que la plus longue (= cohérence dimensionnelle au
            // sens validation séparée du parse, brief §1.4).
            for (int r = 0; r < rows.Count; r++)
            {
                if (rows[r].Count < maxCols)
                {
                    var padded = new List<AstNode>(rows[r]);
                    while (padded.Count < maxCols) padded.Add(PlaceholderNode.Instance);
                    rows[r] = padded;
                }
            }

            return new MatrixNode(rows);
        }

        private static IReadOnlyList<AstNode> ExtractCells(AstNode rowItem)
        {
            // Si rowItem est une expression simple (= 1 cell), retourne [it].
            // Si rowItem encode une multiplication implicite (= ce qu'a produit
            // `a b` au StackParser via cdotIM), on déplie le produit en cells.
            if (rowItem is InfixNode infix && infix.Op == @"\cdotIM")
            {
                var cells = new List<AstNode>();
                FlattenImplicitProduct(infix, cells);
                return cells;
            }
            // ListNode (cas hypothétique d'un sep "," explicite intra-ligne).
            if (rowItem is ListNode l)
                return l.Items;

            return new[] { Promote(rowItem) ?? PlaceholderNode.Instance };
        }

        private static void FlattenImplicitProduct(InfixNode node, List<AstNode> cells)
        {
            void Walk(AstNode n)
            {
                if (n is InfixNode ix && ix.Op == @"\cdotIM")
                {
                    Walk(ix.Left);
                    Walk(ix.Right);
                }
                else
                {
                    cells.Add(Promote(n) ?? PlaceholderNode.Instance);
                }
            }
            Walk(node);
        }
    }
}
