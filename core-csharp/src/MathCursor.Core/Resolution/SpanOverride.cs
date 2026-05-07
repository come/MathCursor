using System;

namespace MathCursor.Core.Resolution
{
    /// <summary>
    /// Override local par span (= "pour CE match précis, applique cette alt
    /// au lieu du <see cref="RulePin"/> ou du défaut"). Le match est
    /// identifié par sa <see cref="MatchSignature"/>, plus stable que le
    /// couple <c>(offset, len)</c> du legacy <see cref="SpanPin"/>.
    ///
    /// <para>Cas typique : l'utilisateur a un RulePin <c>two-uppercase: vec</c>
    /// session-wide, mais veut <c>AB</c> brut (sans vec) pour un span précis
    /// → SpanOverride avec <see cref="AltIdxRevert"/> = revert au default.</para>
    ///
    /// <para>Cf. brief <c>2026-05-07-rule-pin-span-override-refactor</c>.</para>
    /// </summary>
    public sealed class SpanOverride : IEquatable<SpanOverride>
    {
        /// <summary>Sentinel <see cref="AltIdx"/> qui représente « revert
        /// au DefaultLatex de la rule » (= ne pas appliquer d'alt). Choisi
        /// <c>-1</c> car les altIdx valides sont &gt;= 0.</summary>
        public const int AltIdxRevert = -1;

        public MatchSignature Signature { get; }

        /// <summary>Alt à appliquer pour ce span. <see cref="AltIdxRevert"/>
        /// (= -1) = revert au default (pas d'alt appliquée, le defaultLatex
        /// reste tel quel dans le LaTeX rendu).</summary>
        public int AltIdx { get; }

        public SpanOverride(MatchSignature signature, int altIdx)
        {
            Signature = signature ?? throw new ArgumentNullException(nameof(signature));
            if (altIdx < AltIdxRevert)
                throw new ArgumentOutOfRangeException(nameof(altIdx),
                    $"must be >= {AltIdxRevert} ({nameof(AltIdxRevert)} = revert)");
            AltIdx = altIdx;
        }

        /// <summary>True si ce SpanOverride est un revert au default.</summary>
        public bool IsRevert => AltIdx == AltIdxRevert;

        public bool Equals(SpanOverride? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Signature.Equals(other.Signature) && AltIdx == other.AltIdx;
        }

        public override bool Equals(object? obj) => obj is SpanOverride o && Equals(o);

        public override int GetHashCode()
        {
            unchecked { return (Signature.GetHashCode() * 397) ^ AltIdx; }
        }

        public override string ToString()
            => IsRevert
                ? $"SpanOverride({Signature} → revert)"
                : $"SpanOverride({Signature} → alt {AltIdx})";
    }
}
