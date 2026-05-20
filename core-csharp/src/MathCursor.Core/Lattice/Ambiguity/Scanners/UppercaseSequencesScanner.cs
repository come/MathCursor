using System.Collections.Generic;

namespace MathCursor.Core.Lattice.Ambiguity.Scanners
{
    /// <summary>
    /// Scanner Order=3. Capture les séquences de 2 ou 3 majuscules dans
    /// la SOURCE (passé par-dessus l'AST gauche-associatif qui ne
    /// regroupe pas toujours les lettres ensemble : <c>AB*CD</c> = <c>((A*B)*C)*D</c>).
    /// <para>S1 du refacto source-mutation (ADR
    /// <c>2026-05-13-Refactor-source-mutation-pins-sidecar</c>) : scan
    /// source-based, les alts vec et paren du length==2 portent une
    /// <see cref="SourceMutation"/> stable.</para>
    /// </summary>
    public sealed class UppercaseSequencesScanner : IAmbiguityScanner
    {
        public int Order => 3;

        public void Scan(ScanContext ctx, List<AmbiguityMatch> output, bool[] consumed)
            => AlternativeGenerator.ScanUppercaseSequences(ctx.Source, ctx.TopLatex, output, consumed);
    }
}
