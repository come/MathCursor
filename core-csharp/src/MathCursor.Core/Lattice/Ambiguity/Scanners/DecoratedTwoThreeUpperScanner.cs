using System.Collections.Generic;

namespace MathCursor.Core.Lattice.Ambiguity.Scanners
{
    /// <summary>
    /// Scanner Order=2. Détecte les Atom 2/3-majuscules déjà décorés par
    /// le parser (Group, Vec, Angle) et émet un match dont les bornes
    /// couvrent la décoration ENTIÈRE dans le topLatex — splice de l'alt
    /// courante = identité, plus de double-wrap (bug 11-05 commit
    /// <c>9ab248b</c>).
    /// </summary>
    public sealed class DecoratedTwoThreeUpperScanner : IAmbiguityScanner
    {
        public int Order => 2;

        public void Scan(ScanContext ctx, List<AmbiguityMatch> output, bool[] consumed)
            => AlternativeGenerator.ScanDecoratedTwoThreeUpper(ctx.TopAst, ctx.TopLatex, output, consumed);
    }
}
