using System.Collections.Generic;
using MathCursor.Engine.Ast;
using MathCursor.Engine.Tokenization;
using MathCursor.Engine.Vocabulary;

namespace MathCursor.Engine.Parsing
{
    /// <summary>
    /// Précédence climbing (= Pratt parsing simplifié) sur une séquence
    /// d'<see cref="AstNode"/> et de tokens infixes intercalés. Brief v4 §1.2 :
    /// <c>fonctions/puissance > × · / > + - > opérateurs d'ensemble (∪ ∩)
    /// > comparaison &amp; relations (= &lt; &gt; ≤ ≥ ≠ ∈ ⊂ ∥ ⊥ ≡) > ∧ ∨
    /// > ⇒ ⇔</c>.
    ///
    /// <para>Stratégie : on reçoit une liste plate <c>[operand, op, operand,
    /// op, operand, ...]</c> et on la replie en arbre selon les tiers.</para>
    /// </summary>
    public static class PrecedenceClimber
    {
        public static AstNode? Climb(IReadOnlyList<AstNode> operands, IReadOnlyList<Relation> ops)
        {
            if (operands == null || operands.Count == 0) return null;
            if (operands.Count == 1) return operands[0];
            if (ops == null || ops.Count != operands.Count - 1)
                throw new System.ArgumentException(
                    $"Mismatch operands ({operands?.Count}) vs ops ({ops?.Count}). "
                    + "Expected ops.Count == operands.Count - 1.");

            // Trouve l'op de plus faible précédence (= plus grand tier) — il
            // sera la racine. Si plusieurs au même tier, prend le plus à droite
            // (= left-associativity standard).
            int pivot = -1;
            int weakest = -1;
            for (int i = 0; i < ops.Count; i++)
            {
                int t = (int)ops[i].Tier;
                if (t >= weakest) { weakest = t; pivot = i; }
            }
            if (pivot < 0) return operands[0];

            var leftOperands = SubList(operands, 0, pivot + 1);
            var leftOps = SubList(ops, 0, pivot);
            var rightOperands = SubList(operands, pivot + 1, operands.Count);
            var rightOps = SubList(ops, pivot + 1, ops.Count);

            var left = Climb(leftOperands, leftOps) ?? PlaceholderNode.Instance;
            var right = Climb(rightOperands, rightOps) ?? PlaceholderNode.Instance;
            return new InfixNode(ops[pivot].Tex, left, right);
        }

        private static IReadOnlyList<T> SubList<T>(IReadOnlyList<T> src, int start, int end)
        {
            var dst = new List<T>(end - start);
            for (int i = start; i < end; i++) dst.Add(src[i]);
            return dst;
        }
    }
}
