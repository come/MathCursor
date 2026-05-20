using System.Collections.Generic;

namespace MathCursor.Core.Lattice.Ambiguity.Scanners
{
    /// <summary>
    /// Scanner Order=8. Élargissement tight de la chaîne (cf. ADR
    /// 30-04 Feat-tight-implicit-mult-grouping). Default : <c>1/x+1</c>
    /// → <c>\frac{1}{x}+1</c> (PEMDAS). Alt : <c>\frac{1}{x+1}</c>
    /// (chaîne tight élargie aux ops). Re-parse avec
    /// <c>TightExtendsToOps=true</c> et compare.
    /// </summary>
    public sealed class TightChainExtensionScanner : IAmbiguityScanner
    {
        public int Order => 8;

        public void Scan(ScanContext ctx, List<AmbiguityMatch> output, bool[] consumed)
            => AlternativeGenerator.ScanTightChainExtension(ctx.Source, ctx.TopLatex, output, consumed);
    }
}
