namespace MathCursor.Core.Resolution
{
    /// <summary>
    /// Choix de désambiguïsation explicite pour un span précis dans la
    /// source brute. Mémorise « l'utilisateur a choisi l'alternative
    /// <paramref name="AltIdx"/> de la règle <paramref name="Rule"/> pour
    /// le sous-segment [<paramref name="Offset"/>, <paramref name="Offset"/>+
    /// <paramref name="Len"/>) ».
    ///
    /// Utilisé par <see cref="ResolutionSidecar"/> pour persister les
    /// résolutions accumulées au fil des choix popup, indépendamment du
    /// rendu LaTeX. Cf. brief 06-05 sidecar-de-resolutions.
    /// </summary>
    public sealed class SpanPin
    {
        /// <summary>Identifiant de la règle (ex. <c>two-uppercase</c>) qui a
        /// produit l'ambiguïté. Aligne avec <see cref="MathCursor.Core.Lattice.AlternativeGenerator.RuleTwoUppercase"/>
        /// et autres constantes <c>Rule*</c>.</summary>
        public string Rule { get; }

        /// <summary>Offset du début du span dans la source brute.</summary>
        public int Offset { get; }

        /// <summary>Longueur du span (en chars). <c>Offset + Len</c> est l'index
        /// exclusif de fin.</summary>
        public int Len { get; }

        /// <summary>Index de l'alternative choisie par l'utilisateur dans la
        /// liste <c>AmbiguitySpot.Alternatives</c> de la règle.</summary>
        public int AltIdx { get; }

        public SpanPin(string rule, int offset, int len, int altIdx)
        {
            Rule = rule ?? string.Empty;
            Offset = offset;
            Len = len;
            AltIdx = altIdx;
        }

        /// <summary>Décale l'offset de <paramref name="shift"/>. Utilisé au
        /// cross-merge quand un span de la ligne 2 doit être recalibré au
        /// niveau de la mergedSource (préfixée par la ligne 1 + un <c>\n</c>).</summary>
        public SpanPin WithOffsetShift(int shift)
            => new SpanPin(Rule, Offset + shift, Len, AltIdx);

        public override bool Equals(object obj)
            => obj is SpanPin o
               && Rule == o.Rule
               && Offset == o.Offset
               && Len == o.Len
               && AltIdx == o.AltIdx;

        public override int GetHashCode()
        {
            unchecked
            {
                int h = Rule?.GetHashCode() ?? 0;
                h = (h * 397) ^ Offset;
                h = (h * 397) ^ Len;
                h = (h * 397) ^ AltIdx;
                return h;
            }
        }

        public override string ToString()
            => $"SpanPin(rule={Rule}, [{Offset}..{Offset + Len}), alt={AltIdx})";
    }
}
