using System;

namespace MathCursor.Core.Resolution
{
    /// <summary>
    /// Identifiant léger et stable d'une <see cref="MathCursor.Core.Lattice.AmbiguityMatch"/>
    /// dans une zone résolue. Sert de clé pour les <see cref="SpanOverride"/>
    /// (= "pour CE match précis, override l'alt"), à la place du
    /// <c>(offset, len)</c> du legacy <see cref="SpanPin"/> qui était sensible
    /// au splice LaTeX.
    ///
    /// <para>Les 4 champs ensemble discriminent une ambig de manière unique
    /// dans une zone, et restent stables au cross-merge (le rawSource
    /// fusionné préserve les positions ; l'OccurrenceIdx les rang).</para>
    ///
    /// <para>Cf. brief <c>2026-05-07-rule-pin-span-override-refactor</c>.</para>
    /// </summary>
    public sealed class MatchSignature : IEquatable<MatchSignature>
    {
        /// <summary>RuleId de l'AmbiguitySpot (ex. <c>"two-uppercase"</c>).</summary>
        public string RuleId { get; }

        /// <summary>Le DefaultLatex de l'ambig (ex. <c>"AB"</c>) — partie
        /// stable de la signature : si l'utilisateur édite le rawSource au
        /// point de changer ce default, le SpanOverride ne match plus
        /// (sain : le contexte sémantique a changé).</summary>
        public string DefaultLatex { get; }

        /// <summary>Position de début dans le rawSource (= zone source brute,
        /// pas le LaTeX rendu). Stable au splice LaTeX puisque le rawSource
        /// ne bouge pas pendant la résolution.</summary>
        public int RawSourcePos { get; }

        /// <summary>Index de cette occurrence du <see cref="DefaultLatex"/>
        /// dans la zone (0-based). Cas typique <c>"AB+CD=AB"</c> : deux
        /// occurrences distinctes de "AB", la 1ʳᵉ a OccurrenceIdx=0, la 2ᵉ
        /// =1. Filet de sécurité si la position bouge mais l'occurrence
        /// reste identifiable par son rang.</summary>
        public int OccurrenceIdx { get; }

        public MatchSignature(string ruleId, string defaultLatex, int rawSourcePos, int occurrenceIdx)
        {
            RuleId = ruleId ?? throw new ArgumentNullException(nameof(ruleId));
            DefaultLatex = defaultLatex ?? throw new ArgumentNullException(nameof(defaultLatex));
            if (rawSourcePos < 0)
                throw new ArgumentOutOfRangeException(nameof(rawSourcePos), "must be >= 0");
            if (occurrenceIdx < 0)
                throw new ArgumentOutOfRangeException(nameof(occurrenceIdx), "must be >= 0");
            RawSourcePos = rawSourcePos;
            OccurrenceIdx = occurrenceIdx;
        }

        public bool Equals(MatchSignature? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return RuleId == other.RuleId
                && DefaultLatex == other.DefaultLatex
                && RawSourcePos == other.RawSourcePos
                && OccurrenceIdx == other.OccurrenceIdx;
        }

        public override bool Equals(object? obj) => obj is MatchSignature s && Equals(s);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = RuleId.GetHashCode();
                h = (h * 397) ^ DefaultLatex.GetHashCode();
                h = (h * 397) ^ RawSourcePos;
                h = (h * 397) ^ OccurrenceIdx;
                return h;
            }
        }

        public override string ToString()
            => $"MatchSig({RuleId}, \"{DefaultLatex}\", pos={RawSourcePos}, occ={OccurrenceIdx})";

        public static bool operator ==(MatchSignature? a, MatchSignature? b)
            => a is null ? b is null : a.Equals(b);

        public static bool operator !=(MatchSignature? a, MatchSignature? b) => !(a == b);
    }
}
