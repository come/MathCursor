using System.Collections.Generic;

namespace MathCursor.Engine.Ast
{
    /// <summary>
    /// Une ligne d'une matrice (= séquence de cells au sens combinateur
    /// <c>line = expr (colsep expr)*</c> du brief §1.3). Utilisé par
    /// <see cref="MatrixNode"/>.
    /// </summary>
    public sealed class LineNode : AstNode
    {
        public override string Kind => "line";
        public IReadOnlyList<AstNode> Cells { get; }
        public LineNode(IReadOnlyList<AstNode> cells) { Cells = cells; }
    }

    /// <summary>
    /// Matrice 2D (= grille rectangulaire après promotion par le combinateur
    /// liste). Brief §1.3 : <c>matrix = line (rowsep line)*</c>. Le nombre
    /// max de cells est garanti uniforme (= padding placeholder par
    /// <c>ListCombinator</c>).
    /// </summary>
    public sealed class MatrixNode : AstNode
    {
        public override string Kind => "matrix";
        public IReadOnlyList<IReadOnlyList<AstNode>> Rows { get; }
        public int RowCount => Rows.Count;
        public int ColCount => Rows.Count == 0 ? 0 : Rows[0].Count;

        public MatrixNode(IReadOnlyList<IReadOnlyList<AstNode>> rows) { Rows = rows; }
    }
}
