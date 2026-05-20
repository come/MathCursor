namespace MathCursor.Host
{
    /// <summary>
    /// Logique pure (sans Word interop) qui décide si un ¶ peut être nettoyé
    /// par <c>StripListModeMarkerFromCurrentLine</c> sans risque de perdre
    /// du contenu utilisateur.
    ///
    /// <para>Garde-fou contre le bug 2026-05-13 (« perte complète de la
    /// formule après cross-merge + Escape ») : un ¶ qui contient une OMath
    /// ne doit JAMAIS être stripé — l'opération `Range.Text = ""` détruirait
    /// la formule en plus de tout texte alentour.</para>
    ///
    /// <para>Le strip n'est légitime que pour un ¶ qui contient
    /// uniquement le marker auto-injecté (typiquement <c>"= "</c> /
    /// <c>"{ "</c> / <c>"&amp;= "</c>, courts par construction).</para>
    /// </summary>
    internal static class ListModeStripGuard
    {
        /// <summary>Longueur max d'un marker auto-injecté. Couvre <c>"= "</c>,
        /// <c>"{ "</c>, <c>"&amp;= "</c> avec marge. Au-delà = c'est du texte
        /// utilisateur, on refuse de strip.</summary>
        private const int MaxMarkerLength = 4;

        /// <summary>
        /// Décide si un ¶ candidat peut être nettoyé. Refuse si le ¶
        /// contient une OMath ou si son contenu est trop long pour être
        /// un simple marker (= du texte utilisateur à préserver).
        /// </summary>
        public static bool CanStripMarkerFromLine(int omathsInPara, int contentLength)
        {
            if (omathsInPara > 0) return false;          // jamais effacer une formule
            if (contentLength <= 0) return false;        // rien à strip
            if (contentLength > MaxMarkerLength) return false; // texte user, pas un marker
            return true;
        }
    }
}
