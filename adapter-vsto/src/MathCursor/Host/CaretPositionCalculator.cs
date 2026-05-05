namespace MathCursor.Host
{
    /// <summary>
    /// Logique pure de calcul de position caret après insertion d'un OMath.
    /// <para>
    /// Bug user 05-05 « soit f » Ctrl+Espace → cursor descend : quand l'OMath
    /// est en fin de ¶, <c>omEnd + 1</c> tombe sur la position du ¶ mark
    /// (= start du ¶ suivant) → caret saute à la ligne suivante. La fix :
    /// clamp à <c>paraContentEnd</c> (position juste avant le ¶ mark).
    /// </para>
    /// </summary>
    internal static class CaretPositionCalculator
    {
        /// <summary>
        /// Position où placer le caret juste après un OMath, sans déborder
        /// dans le ¶ suivant ni au-delà du document.
        /// </summary>
        /// <param name="omEnd">Position de fin de l'OMath (exclusive).</param>
        /// <param name="paraContentEnd">
        /// Position juste avant le ¶ mark du ¶ contenant l'OMath
        /// (= <c>paragraph.Range.End - 1</c> côté Word).
        /// </param>
        /// <param name="docContentEnd">
        /// Position de fin du document (= <c>doc.Content.End</c>).
        /// </param>
        /// <returns>Position absolue où SetRange peut placer le caret.</returns>
        public static int ClampAfterOMathToParagraph(int omEnd, int paraContentEnd, int docContentEnd)
        {
            int afterPos = omEnd + 1;
            // Clamp doc end (jamais au-delà du dernier char du doc)
            if (afterPos > docContentEnd) afterPos = docContentEnd;
            // Clamp ¶ end : si on déborde dans le ¶ suivant, rester sur le ¶ courant
            if (afterPos > paraContentEnd) afterPos = paraContentEnd;
            return afterPos;
        }
    }
}
