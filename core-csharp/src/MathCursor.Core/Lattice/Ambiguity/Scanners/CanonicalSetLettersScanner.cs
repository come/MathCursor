using System.Collections.Generic;

namespace MathCursor.Core.Lattice.Ambiguity.Scanners
{
    /// <summary>
    /// Scanner Order=5. Lettres canoniques R / N / Z / Q / C isolées en
    /// source : propose les alts "ensemble" (<c>\mathbb{R}</c>) vs
    /// "lettre variable" (R seul). Source-mutation <c>R</c> → <c>bbR</c>.
    /// </summary>
    public sealed class CanonicalSetLettersScanner : IAmbiguityScanner
    {
        public int Order => 5;

        public void Scan(ScanContext ctx, List<AmbiguityMatch> output, bool[] consumed)
            => AlternativeGenerator.ScanCanonicalSetLetters(ctx.Source, ctx.TopLatex, output, consumed);
    }
}
