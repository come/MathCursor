using System.Collections.Generic;
using System.Linq;

namespace MathCursor.Core.Lattice
{
    /// <summary>
    /// Segment du span où plusieurs interprétations divergent dans le top-K.
    /// </summary>
    public sealed class AmbiguousSegment
    {
        public int Start { get; }
        public int End { get; }
        /// <summary>Sous-chemins (extraits des chemins top-K) qui couvrent ce segment, dédupliqués.</summary>
        public IReadOnlyList<IReadOnlyList<LatticeEdge>> Variants { get; }
        public IReadOnlyList<int> Costs { get; }

        public AmbiguousSegment(int start, int end,
            IReadOnlyList<IReadOnlyList<LatticeEdge>> variants,
            IReadOnlyList<int> costs)
        {
            Start = start;
            End = end;
            Variants = variants;
            Costs = costs;
        }
    }

    /// <summary>
    /// Détection des segments ambigus entre chemins du top-K, par convergence
    /// d'arêtes (cf. algorithm.md §3).
    ///
    /// Principe : pour chaque chemin, on calcule l'ensemble de ses frontières
    /// d'arêtes (positions où une arête se termine). Une "position de
    /// convergence" est une position où TOUS les chemins ont une frontière.
    /// Entre deux convergences consécutives, si les sous-chemins diffèrent,
    /// c'est un segment ambigu. On garde le PLUS À DROITE (= dernière
    /// ambiguïté en cours, celle que l'utilisateur vient de créer).
    /// </summary>
    public static class AmbiguityDetector
    {
        /// <summary>
        /// Renvoie le dernier segment ambigu du top-K, ou null si aucun
        /// (chemins identiques ou un seul candidat).
        /// </summary>
        public static AmbiguousSegment? FindLastAmbiguous(
            IReadOnlyList<LatticePath> paths, int totalLength)
        {
            if (paths == null || paths.Count < 2) return null;

            // Frontières par chemin
            var boundaries = paths.Select(p =>
            {
                var s = new HashSet<int> { 0, totalLength };
                foreach (var e in p.Edges)
                {
                    s.Add(e.Start);
                    s.Add(e.End);
                }
                return s;
            }).ToList();

            // Convergences = positions où TOUS les chemins ont une frontière
            var convergences = new List<int>();
            for (int p = 0; p <= totalLength; p++)
            {
                if (boundaries.All(s => s.Contains(p))) convergences.Add(p);
            }

            // Parcours des intervalles entre convergences ; on retient le
            // dernier qui contient des sous-chemins différents.
            AmbiguousSegment? last = null;
            for (int i = 0; i < convergences.Count - 1; i++)
            {
                int start = convergences[i];
                int end = convergences[i + 1];

                var subPaths = paths
                    .Select(p => (IReadOnlyList<LatticeEdge>)p.Edges
                        .Where(e => e.Start >= start && e.End <= end)
                        .ToList())
                    .ToList();

                // Signatures pour dédup : type/valeur joints
                var signatures = subPaths
                    .Select(sp => string.Join("|", sp.Select(e => $"{e.Type}/{e.Value}")))
                    .ToList();
                if (signatures.Distinct().Count() <= 1) continue; // pas d'ambiguïté

                // Dédup les variantes par signature
                var seen = new HashSet<string>();
                var uniqueVariants = new List<IReadOnlyList<LatticeEdge>>();
                var uniqueCosts = new List<int>();
                for (int j = 0; j < subPaths.Count; j++)
                {
                    if (seen.Add(signatures[j]))
                    {
                        uniqueVariants.Add(subPaths[j]);
                        uniqueCosts.Add(subPaths[j].Sum(e => e.Weight));
                    }
                }
                last = new AmbiguousSegment(start, end, uniqueVariants, uniqueCosts);
            }
            return last;
        }
    }
}
