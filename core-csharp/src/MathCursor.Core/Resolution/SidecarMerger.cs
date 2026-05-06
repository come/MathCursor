using System.Collections.Generic;

namespace MathCursor.Core.Resolution
{
    /// <summary>
    /// Logique pure de fusion de plusieurs <see cref="ResolutionSidecar"/>
    /// concaténés au cross-merge multi-ligne. Pour chaque part :
    /// <list type="bullet">
    ///   <item>Les <see cref="SpanPin"/>s sont décalés de l'offset auquel la
    ///     ligne commence dans la mergedSource (ligne 1 = shift 0, ligne 2
    ///     = shift = len(ligne1) + 1 pour le \n, etc.).</item>
    ///   <item>Les votes par <c>rule.altIdx</c> sont sommés.</item>
    /// </list>
    ///
    /// Une part peut être <c>null</c> ou <see cref="ResolutionSidecar.Empty"/> :
    /// elle est ignorée silencieusement (cas dégradé : OMath ancien créé avant
    /// l'introduction des sidecars).
    /// </summary>
    public static class SidecarMerger
    {
        /// <summary>
        /// Fusionne <paramref name="parts"/> avec leurs <paramref name="offsetShifts"/>
        /// respectifs. Les 2 listes doivent avoir la même longueur.
        /// </summary>
        public static ResolutionSidecar Merge(
            IReadOnlyList<ResolutionSidecar> parts,
            IReadOnlyList<int> offsetShifts)
        {
            if (parts == null || parts.Count == 0) return ResolutionSidecar.Empty;
            if (offsetShifts == null || offsetShifts.Count != parts.Count)
                throw new System.ArgumentException(
                    "parts et offsetShifts doivent avoir la même longueur",
                    nameof(offsetShifts));

            var mergedPins = new List<SpanPin>();
            var mergedVotes = new Dictionary<string, Dictionary<int, int>>();

            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part == null || part.IsEmpty) continue;
                int shift = offsetShifts[i];

                foreach (var p in part.SpanPins)
                    mergedPins.Add(p.WithOffsetShift(shift));

                foreach (var ruleEntry in part.ZoneVotes)
                {
                    if (!mergedVotes.TryGetValue(ruleEntry.Key, out var byAlt))
                    {
                        byAlt = new Dictionary<int, int>();
                        mergedVotes[ruleEntry.Key] = byAlt;
                    }
                    foreach (var altEntry in ruleEntry.Value)
                    {
                        byAlt.TryGetValue(altEntry.Key, out var existing);
                        byAlt[altEntry.Key] = existing + altEntry.Value;
                    }
                }
            }

            // Cast en IReadOnlyDictionary nested
            var votesReadonly = new Dictionary<string, IReadOnlyDictionary<int, int>>();
            foreach (var kv in mergedVotes) votesReadonly[kv.Key] = kv.Value;

            return new ResolutionSidecar(mergedPins, votesReadonly);
        }
    }
}
