using System.Collections.Generic;

namespace MathCursor.Core.Lattice.Ambiguity.Scanners
{
    /// <summary>
    /// Scanner Order=3. Capture les séquences de 2 ou 3 majuscules dans
    /// le topLatex (passé par-dessus l'AST gauche-associatif qui ne
    /// regroupe pas toujours les lettres ensemble : <c>AB*CD</c> = <c>((A*B)*C)*D</c>).
    /// <para>S1 du refacto source-mutation (à venir) convertira ce
    /// scanner au scan source-based pour émettre des
    /// <see cref="SourceMutation"/> sur les alts vec et paren.</para>
    /// </summary>
    public sealed class UppercaseSequencesScanner : IAmbiguityScanner
    {
        public int Order => 3;

        public void Scan(ScanContext ctx, List<AmbiguityMatch> output, bool[] consumed)
            => AlternativeGenerator.ScanUppercaseSequences(ctx.TopLatex, output, consumed);
    }
}
