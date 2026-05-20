using System.Collections.Generic;

namespace MathCursor.Core.Lattice.Ambiguity.Scanners
{
    /// <summary>
    /// Scanner Order=0. Émet l'ambig pour les angles 2-lettres avec
    /// placeholder (<c>^AB</c> en saisie → <c>\widehat{AB\square}</c>).
    /// DOIT tourner AVANT <see cref="UppercaseSequencesScanner"/> sinon
    /// <c>AB</c> à l'intérieur de <c>\widehat{AB\square}</c> serait
    /// capturé comme alt vec.
    /// </summary>
    public sealed class AngleTwoLetterPlaceholderScanner : IAmbiguityScanner
    {
        public int Order => 0;

        public void Scan(ScanContext ctx, List<AmbiguityMatch> output, bool[] consumed)
            => AlternativeGenerator.ScanAngleTwoLetterPlaceholder(ctx.TopAst, ctx.TopLatex, output, consumed);
    }
}
