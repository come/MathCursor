namespace MathCursor.Host.Session
{
    /// <summary>
    /// Zone reverted multi-ligne (Mode 2 du cross-merge, cf. ADR
    /// 2026-05-04-Feat-multiline-edit-cascade-merge). Quand l'user fait
    /// « Revenir à la saisie » sur un OMath multi-ligne, on mémorise les
    /// bornes pour que <see cref="MathCursor.Host.Merging.RevertedMultiLineMerger"/>
    /// absorbe TOUS les paragraphes au prochain commit.
    /// Remplace les champs <c>_revertedMultiLineZoneStart</c> /
    /// <c>_revertedMultiLineZoneEnd</c> de <c>SuggestionService</c>.
    /// </summary>
    internal sealed class RevertedMultiLineZone
    {
        public int AbsStart { get; }
        public int AbsEnd { get; }

        public RevertedMultiLineZone(int absStart, int absEnd)
        {
            AbsStart = absStart;
            AbsEnd = absEnd;
        }

        /// <summary>Vrai si <paramref name="absPos"/> est dans la zone reverted
        /// (ou juste après — tolérance +1 pour le commit en fin).</summary>
        public bool ContainsCommit(int absPos)
            => absPos >= AbsStart && absPos <= AbsEnd + 1;
    }
}
