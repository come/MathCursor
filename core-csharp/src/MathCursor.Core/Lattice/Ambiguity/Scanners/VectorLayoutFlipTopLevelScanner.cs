using System.Collections.Generic;

namespace MathCursor.Core.Lattice.Ambiguity.Scanners
{
    /// <summary>
    /// Scanner Order=7. Col↔ligne pour <c>VectorCoordinates</c> top-level
    /// (cas <c>u (1 2)</c>, <c>u(1, 2)</c>, <c>OM (x y z)</c>) : propose
    /// le layout opposé en alt. Limité au top-level pour éviter les
    /// conflits avec d'autres ambig nested.
    /// </summary>
    public sealed class VectorLayoutFlipTopLevelScanner : IAmbiguityScanner
    {
        public int Order => 7;

        public void Scan(ScanContext ctx, List<AmbiguityMatch> output, bool[] consumed)
            => AlternativeGenerator.ScanVectorLayoutFlipTopLevel(ctx.TopAst, ctx.TopLatex, output, consumed);
    }
}
