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
        /// <summary>
        /// Décide si un ¶ candidat peut être nettoyé. Refuse si le ¶
        /// contient une OMath ou si son contenu est trop long pour être
        /// un simple marker (= du texte utilisateur à préserver).
        /// </summary>
        public static bool CanStripMarkerFromLine(int omathsInPara, int contentLength)
        {
            // BUG INERTE (phase 1 TDD) : autorise tout, sera fixé en phase 2
            // après commit RED. Cf. ADR
            // 2026-05-13-Fix-list-mode-strip-guard-omath.
            return true;
        }
    }
}
