using System.Collections.Generic;

namespace MathCursor.Core.Resolution.Signals
{
    /// <summary>
    /// Signal contextuel <c>L1_Block</c> qui consomme un
    /// <see cref="ResolutionSidecar"/>.
    ///
    /// <para>Reproduit la logique des <c>ZoneVotes</c> de l'ancien overload
    /// <c>ZoneResolver.Resolve(rawSource, sidecar)</c> : chaque vote pour
    /// <c>(rule, alt)</c> contribue proportionnellement à son <c>count</c>.
    /// Les pins (<see cref="SpanPin"/>) ne passent PAS par ce signal — ils
    /// dominent les votes et sont appliqués span-level dans le ZoneResolver,
    /// ce qui préserve la précision <c>(offset, len)</c>.</para>
    ///
    /// <para>Cf. brief <c>2026-05-07-global-context-multi-zoom-ranking</c>.</para>
    /// </summary>
    public sealed class SidecarSignal : IContextSignal
    {
        // Score de base par vote dans le sidecar. Choisi pour qu'un seul vote
        // ne domine pas tout (les votes sont cumulables, l'argmax tranche).
        // Valeur calibrable empiriquement.
        private const double VoteWeight = 0.3;

        public string Name => "Sidecar";

        public ZoomLevel Level => ZoomLevel.L1_Block;

        public IReadOnlyDictionary<string, double> Score(ContextSnapshot? ctx)
        {
            var deltas = new Dictionary<string, double>();
            if (ctx?.Sidecar == null || ctx.Sidecar.IsEmpty) return deltas;

            foreach (var ruleVotes in ctx.Sidecar.ZoneVotes)
            {
                if (ruleVotes.Value == null) continue;
                foreach (var altVote in ruleVotes.Value)
                {
                    if (altVote.Value <= 0) continue;
                    string key = ScoringHints.Key(ruleVotes.Key, altVote.Key);
                    deltas[key] = altVote.Value * VoteWeight;
                }
            }
            return deltas;
        }
    }
}
