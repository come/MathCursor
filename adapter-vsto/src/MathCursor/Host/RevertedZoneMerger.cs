using System.Collections.Generic;

namespace MathCursor.Host
{
    /// <summary>
    /// Logique pure (sans dépendance Word) pour construire le source mergé
    /// d'une zone reverted multi-ligne lors d'un commit cascade Mode 2.
    /// <para>
    /// Extraite de <see cref="SuggestionService"/> pour être unit-testable
    /// sans dépendance Word/VSTO.
    /// </para>
    /// </summary>
    internal static class RevertedZoneMerger
    {
        /// <summary>
        /// Construit le source mergé qui sera envoyé au lattice à partir des
        /// textes des paragraphes de la zone reverted.
        /// <para>
        /// La ligne où l'utilisateur commit est identifiée par <paramref name="absStart"/>
        /// (= début de la NER zone) comparé aux positions de début de chaque
        /// paragraphe (<paramref name="paragraphRangeStarts"/>). Cette ligne
        /// est remplacée par <paramref name="currentSource"/> (= ce que l'user
        /// a tapé/édité). Les autres lignes sont préservées telles quelles.
        /// </para>
        /// <para>
        /// Cf. bug user 05-05 : multi-ligne 3 lignes, revert, modifier ligne 1,
        /// commit ligne 1 → on doit produire <c>"newLine1\nline2\nline3"</c>,
        /// pas <c>"line1\nline2\nnewLine1"</c> (= ancien comportement
        /// hardcodé sur dernier index).
        /// </para>
        /// </summary>
        /// <param name="paragraphTexts">Texte de chaque paragraphe de la zone reverted.</param>
        /// <param name="paragraphRangeStarts">Position absolue de début de chaque paragraphe (même longueur que paragraphTexts).</param>
        /// <param name="absStart">Position absolue où le commit est déclenché (start de la NER zone).</param>
        /// <param name="currentSource">Source du paragraphe courant (= zone NER, peut être édité par l'user).</param>
        /// <returns>Source mergé, lignes séparées par <c>\n</c>, prêt pour le lattice.</returns>
        public static string BuildMergedSource(
            IReadOnlyList<string> paragraphTexts,
            IReadOnlyList<int> paragraphRangeStarts,
            int absStart,
            string currentSource)
        {
            if (paragraphTexts == null || paragraphTexts.Count == 0)
                return currentSource ?? string.Empty;
            if (paragraphRangeStarts == null || paragraphRangeStarts.Count != paragraphTexts.Count)
                return currentSource ?? string.Empty;

            // Trouve l'index du paragraphe contenant absStart : le dernier
            // paragraphe dont le start est <= absStart.
            int currentIdx = 0;
            for (int i = paragraphRangeStarts.Count - 1; i >= 0; i--)
            {
                if (paragraphRangeStarts[i] <= absStart)
                {
                    currentIdx = i;
                    break;
                }
            }

            var lines = new List<string>(paragraphTexts.Count);
            for (int i = 0; i < paragraphTexts.Count; i++)
            {
                lines.Add(i == currentIdx ? (currentSource ?? string.Empty) : paragraphTexts[i]);
            }
            return string.Join("\n", lines);
        }
    }
}
