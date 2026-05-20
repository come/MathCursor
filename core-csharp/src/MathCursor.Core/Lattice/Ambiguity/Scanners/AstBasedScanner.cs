using System.Collections.Generic;

namespace MathCursor.Core.Lattice.Ambiguity.Scanners
{
    /// <summary>
    /// Scanner Order=1. Parcourt l'AST top-1 et émet les patterns
    /// structurels (Sup d'une lettre par un nombre, etc.) via
    /// <c>MatchAmbiguity</c> en pré-ordre right-first.
    /// </summary>
    public sealed class AstBasedScanner : IAmbiguityScanner
    {
        public int Order => 1;

        public void Scan(ScanContext ctx, List<AmbiguityMatch> output, bool[] consumed)
            => AlternativeGenerator.CollectAllMatchesRec(ctx.TopAst, ctx.TopLatex, output, consumed);
    }
}
