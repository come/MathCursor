using System;

namespace MathCursor.Core.Resolution
{
    /// <summary>
    /// Pin de règle (= "pour cette rule, l'utilisateur préfère cette alt").
    /// Scope session, s'applique à toutes les ambig de cette rule dans la
    /// zone — pas d'attache à un span précis.
    ///
    /// <para>Remplace l'ancienne combinaison
    /// <see cref="ResolutionSidecar.ZoneVotes"/> + dépend du span pour les
    /// pins legacy. Cf. brief
    /// <c>2026-05-07-rule-pin-span-override-refactor</c>.</para>
    /// </summary>
    public sealed class RulePin : IEquatable<RulePin>
    {
        public string RuleId { get; }
        public int AltIdx { get; }

        public RulePin(string ruleId, int altIdx)
        {
            RuleId = ruleId ?? throw new ArgumentNullException(nameof(ruleId));
            if (altIdx < 0)
                throw new ArgumentOutOfRangeException(nameof(altIdx), "must be >= 0");
            AltIdx = altIdx;
        }

        public bool Equals(RulePin? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return RuleId == other.RuleId && AltIdx == other.AltIdx;
        }

        public override bool Equals(object? obj) => obj is RulePin p && Equals(p);

        public override int GetHashCode()
        {
            unchecked { return (RuleId.GetHashCode() * 397) ^ AltIdx; }
        }

        public override string ToString() => $"RulePin({RuleId} → alt {AltIdx})";
    }
}
