namespace MathCursor.Core.Resolution
{
    /// <summary>
    /// Niveau de zoom contextuel pour les signaux <see cref="IContextSignal"/>.
    /// Du plus chaud (L0 = token courant) au plus froid (L5 = document entier).
    /// Cf. brief <c>2026-05-07-global-context-multi-zoom-ranking</c>.
    /// </summary>
    public enum ZoomLevel
    {
        /// <summary>Token courant : NER, voisins immédiats.</summary>
        L0_Token = 0,

        /// <summary>Bloc multi-ligne courant (cases, équivalences,
        /// chaîne en cours de cross-merge).</summary>
        L1_Block = 1,

        /// <summary>Paragraphe courant : résolutions précédentes du ¶ +
        /// mots-clés du ¶.</summary>
        L2_Paragraph = 2,

        /// <summary>Paragraphes voisins (N précédents). Décay linéaire avec
        /// la distance.</summary>
        L3_NeighborParas = 3,

        /// <summary>Section Word (entre <c>Heading*</c>).</summary>
        L4_Section = 4,

        /// <summary>Document entier : freq globale et choix utilisateur cumulés.</summary>
        L5_Document = 5,
    }
}
