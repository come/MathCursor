using System.Collections.Generic;

namespace MathCursor.Core.Resolution
{
    /// <summary>
    /// Logique pure de fusion de plusieurs <see cref="ResolutionSidecar"/>
    /// concaténés au cross-merge multi-ligne. Pour chaque part :
    /// <list type="bullet">
    ///   <item><b>Legacy v1</b> : <see cref="SpanPin"/>s shiftés (offset
    ///     ligne dans mergedSource), <c>ZoneVotes</c> sommés.</item>
    ///   <item><b>v2</b> (cf. brief 2026-05-07-rule-pin-span-override-refactor) :
    ///     <see cref="RulePin"/>s union avec last-write-wins par RuleId
    ///     (last part dans <paramref name="parts"/> gagne), <see cref="SpanOverride"/>s
    ///     concaténés avec shift sur le <c>RawSourcePos</c> de la signature.</item>
    /// </list>
    ///
    /// <para>Une part peut être <c>null</c> ou <see cref="ResolutionSidecar.Empty"/> :
    /// elle est ignorée silencieusement.</para>
    ///
    /// <para><b>Note suppression différée</b> : le brief 2026-05-07 prévoit
    /// la suppression effective de <c>SidecarMerger</c> + <c>IntraMergeSidecarBuilder</c>
    /// quand les SpanPins legacy seront retirés du flow. Pour l'instant
    /// on étend pour fusionner aussi les nouveaux types — la suppression
    /// effective viendra dans une PR ultérieure.</para>
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
            // v2 : RulePins last-write-wins par RuleId (le dernier sidecar
            // gagne), SpanOverrides concaténés (shift sur RawSourcePos).
            var mergedRulePinsByRule = new Dictionary<string, int>();
            var mergedSpanOverrides = new List<SpanOverride>();

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

                // v2 : RulePins last-write-wins.
                foreach (var rp in part.RulePins)
                    mergedRulePinsByRule[rp.RuleId] = rp.AltIdx;

                // v2 : SpanOverrides shifted + concaténés.
                foreach (var ov in part.SpanOverrides)
                    mergedSpanOverrides.Add(shift == 0 ? ov : ov.WithSignatureShift(shift));
            }

            // Cast en IReadOnlyDictionary nested
            var votesReadonly = new Dictionary<string, IReadOnlyDictionary<int, int>>();
            foreach (var kv in mergedVotes) votesReadonly[kv.Key] = kv.Value;

            var mergedRulePins = new List<RulePin>(mergedRulePinsByRule.Count);
            foreach (var kv in mergedRulePinsByRule)
                mergedRulePins.Add(new RulePin(kv.Key, kv.Value));

            return new ResolutionSidecar(
                spanPins: mergedPins,
                zoneVotes: votesReadonly,
                rulePins: mergedRulePins,
                spanOverrides: mergedSpanOverrides);
        }
    }
}
