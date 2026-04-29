using System.Collections.Generic;
using System.Linq;

namespace MathCursor.Core.Lattice
{
    /// <summary>
    /// Un chemin à travers le lattice : séquence d'arêtes du début à la fin
    /// du span, avec son coût total (somme des poids).
    /// </summary>
    public sealed class LatticePath
    {
        public int Cost { get; }
        public IReadOnlyList<LatticeEdge> Edges { get; }

        public LatticePath(int cost, IReadOnlyList<LatticeEdge> edges)
        {
            Cost = cost;
            Edges = edges ?? new List<LatticeEdge>();
        }
    }

    /// <summary>
    /// Top-K plus courts chemins dans un DAG d'arêtes. Dijkstra mémoïsé qui
    /// garde les K meilleurs chemins par nœud, avec troncature à 4·K en
    /// intermédiaire pour éviter l'explosion combinatoire (cf. algorithm.md §2).
    ///
    /// Coût typique : pour un span de N caractères et ~3N arêtes, K=3 →
    /// quelques milliers d'opérations. Largement temps réel.
    /// </summary>
    public static class LatticePathFinder
    {
        public static List<LatticePath> TopK(List<LatticeEdge> edges, int totalLength, int k = 3)
        {
            // memo[i] = liste des meilleurs chemins atteignant la position i
            var memo = new List<LatticePath>[totalLength + 1];
            for (int i = 0; i <= totalLength; i++) memo[i] = new List<LatticePath>();
            memo[0].Add(new LatticePath(0, new List<LatticeEdge>()));

            for (int i = 0; i < totalLength; i++)
            {
                if (memo[i].Count == 0) continue;
                // Toutes les arêtes sortant de la position i
                foreach (var edge in edges.Where(e => e.Start == i))
                {
                    foreach (var p in memo[i])
                    {
                        var newEdges = new List<LatticeEdge>(p.Edges) { edge };
                        memo[edge.End].Add(new LatticePath(p.Cost + edge.Weight, newEdges));
                    }
                }
                // Troncature à 4·K en cours de route (heuristique du brief)
                for (int j = i + 1; j <= totalLength; j++)
                {
                    if (memo[j].Count > k * 4)
                    {
                        memo[j] = memo[j].OrderBy(p => p.Cost).Take(k * 4).ToList();
                    }
                }
            }

            return memo[totalLength].OrderBy(p => p.Cost).Take(k).ToList();
        }
    }
}
