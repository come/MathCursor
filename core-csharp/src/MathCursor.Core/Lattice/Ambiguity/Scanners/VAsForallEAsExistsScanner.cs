using System.Collections.Generic;

namespace MathCursor.Core.Lattice.Ambiguity.Scanners
{
    /// <summary>
    /// Scanner Order=4. Détecte <c>V</c> / <c>E</c> isolés en source
    /// (suivis d'espace ou EOF) et propose les alternatives :
    /// V → V identity / ∀ scope / √ scope ; E → E identity / ∃ scope.
    /// Alts avec <see cref="SourceMutation"/> (V → forall, V → racine,
    /// E → exists).
    /// </summary>
    public sealed class VAsForallEAsExistsScanner : IAmbiguityScanner
    {
        public int Order => 4;

        public void Scan(ScanContext ctx, List<AmbiguityMatch> output, bool[] consumed)
            => AlternativeGenerator.ScanVAsForallEAsExists(ctx.Source, ctx.TopLatex, output, consumed);
    }
}
