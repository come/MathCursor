using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.Merging
{
    /// <summary>
    /// Voisin adjacent d'une zone d'insertion : une OMath déjà commitée
    /// qui touche la zone (à gauche, à droite, ou au-dessus pour le
    /// cross-merge multi-ligne), à absorber dans le commit en cours.
    /// </summary>
    public sealed class Neighbor
    {
        /// <summary>L'OMath Word brute (pour récupérer Range / méthodes
        /// natives si besoin).</summary>
        public Word.OMath OMath { get; set; }

        /// <summary>Bornes de l'OMath dans le doc (= <c>OMath.Range.Start/End</c>
        /// capturées au moment du probe, stable même si Word ré-itère).</summary>
        public int RangeStart { get; set; }
        public int RangeEnd { get; set; }

        /// <summary>Handle MathCursor (= ID interne du store, suffixe du
        /// bookmark <c>mcEq_*</c>). Null si l'OMath n'est pas à nous.</summary>
        public string Handle { get; set; }

        /// <summary>Source originelle (texte tapé par l'utilisateur avant
        /// conversion), récupérée depuis l'<c>IEquationStore</c> par handle.
        /// Sert à reconstruire le <c>mergedSource</c> avec un texte
        /// re-typable.</summary>
        public string Source { get; set; }
    }

    /// <summary>Conteneur résultat du probe « voisins adjacents
    /// gauche/droite » de <see cref="NeighborFinder.FindAdjacent"/>.
    /// Chaque champ peut être <c>null</c> si aucun voisin de ce côté.</summary>
    public sealed class AdjacentNeighbors
    {
        public Neighbor Left { get; set; }
        public Neighbor Right { get; set; }
    }
}
