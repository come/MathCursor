namespace MathCursor.Core.Lattice
{
    /// <summary>
    /// Type d'arête (token candidat) dans le lattice de tokenisation.
    /// </summary>
    public enum EdgeType
    {
        /// <summary>Mot-clé qui déclenche un scope (lim, sum, frac…).</summary>
        Keyword,
        /// <summary>Fonction nommée (sin, cos, ln…).</summary>
        Function,
        /// <summary>Lettre grecque (alpha, pi…).</summary>
        Greek,
        /// <summary>Identifiant générique (variable, point, etc.).</summary>
        Ident,
        /// <summary>Nombre littéral (entier ou décimal).</summary>
        Number,
        /// <summary>Opérateur (+, -, *, /, ^, =, (, ), …).</summary>
        Op,
        /// <summary>Espace ou tabulation (séparateur).</summary>
        Space,
        /// <summary>Saut de ligne (\n source). Sépare les lignes d'un
        /// MultiLineBlock (système ou chaîne d'équivalences/égalités).
        /// Cf. brief 2026-04-30-multiline-systems-equivalences.md.</summary>
        LineBreak,
    }

    /// <summary>
    /// Une arête dans le lattice de tokenisation : un token candidat couvrant
    /// les positions [Start, End) du span d'entrée, avec un poids qui exprime
    /// sa plausibilité. Le top-K Dijkstra sélectionne les chemins de poids
    /// minimum à travers ce DAG d'arêtes.
    ///
    /// Le drapeau <see cref="Tight"/> est positionné sur les opérateurs binaires
    /// (+, -, *, /, ^) : <c>true</c> si l'opérateur n'a aucun espace adjacent
    /// dans la saisie ("n+1"), <c>false</c> sinon ("n + 1" ou "n +1"). Le parser
    /// l'utilise pour décider où se termine un argument vs un body.
    /// </summary>
    public sealed class LatticeEdge
    {
        public int Start { get; }
        public int End { get; }
        public EdgeType Type { get; }
        /// <summary>Texte capturé, ou nom canonique pour mot-clé/fonction/grec.</summary>
        public string Value { get; }
        public int Weight { get; }
        /// <summary>Pour Op binaires : true si pas d'espace adjacent (n+1), false sinon (n + 1). Null pour autres.</summary>
        public bool? Tight { get; }

        public LatticeEdge(int start, int end, EdgeType type, string value, int weight, bool? tight = null)
        {
            Start = start;
            End = end;
            Type = type;
            Value = value ?? "";
            Weight = weight;
            Tight = tight;
        }

        public override string ToString()
            => $"[{Start},{End}) {Type}={Value} w={Weight}{(Tight.HasValue ? (Tight.Value ? " tight" : " loose") : "")}";
    }
}
