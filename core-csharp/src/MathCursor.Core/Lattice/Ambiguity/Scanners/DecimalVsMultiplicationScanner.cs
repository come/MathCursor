using System.Collections.Generic;

namespace MathCursor.Core.Lattice.Ambiguity.Scanners
{
    /// <summary>
    /// Scanner Order=9. Décimal vs multiplication pour <c>\d+\.\d+</c>
    /// (ADR 30-04 Feat-dot-as-multiplier). Default = mult
    /// (<c>3 \cdot 4</c>), alt = décimal (<c>3{,}4</c>). Permet aux
    /// utilisateurs anglo qui tapent <c>3.4</c> pour "trois virgule
    /// quatre" de switcher rapidement.
    /// </summary>
    public sealed class DecimalVsMultiplicationScanner : IAmbiguityScanner
    {
        public int Order => 9;

        public void Scan(ScanContext ctx, List<AmbiguityMatch> output, bool[] consumed)
            => AlternativeGenerator.ScanDecimalVsMultiplication(ctx.Source, ctx.TopLatex, output, consumed);
    }
}
