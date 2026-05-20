using System.Collections.Generic;

namespace MathCursor.Core.Lattice.Ambiguity.Scanners
{
    /// <summary>
    /// Scanner Order=6. Fonction-typique + parens avec 2/3 args virgule
    /// (<c>f(1, 2)</c>). Default = function call (top-1), alt = vec coords
    /// ligne via mutation source <c>f</c> → <c>u</c> (un ident vec-typique).
    /// Cf. brief 2026-04-29-vector-coordinates-shorthand §3.1.
    /// </summary>
    public sealed class FunctionTypicalCommaCoordsScanner : IAmbiguityScanner
    {
        public int Order => 6;

        public void Scan(ScanContext ctx, List<AmbiguityMatch> output, bool[] consumed)
            => AlternativeGenerator.ScanFunctionTypicalWithCommaCoords(ctx.Source, ctx.TopLatex, output, consumed);
    }
}
