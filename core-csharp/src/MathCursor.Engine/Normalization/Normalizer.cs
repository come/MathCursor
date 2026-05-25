namespace MathCursor.Engine.Normalization
{
    /// <summary>
    /// Façade du pipeline de normalisation. Sépare les transformations
    /// déterministes (= primes Lagrange, case-tolerance, etc.) du
    /// <c>Tokenizer</c> qui ne fait plus que char→Token.
    ///
    /// <para>Architecture cible (= cf. plan Chantier 2 du 2026-05-25) :
    /// <list type="bullet">
    ///   <item><b>Pre-tokenize</b> : transformations <c>string → string</c>
    ///     (= NBSP→space, NFC, synonymes Unicode optionnels…). Pour l'instant
    ///     aucune passe active (= NBSP géré nativement par
    ///     <c>char.IsWhiteSpace</c>, synonymes via Glue YAML).</item>
    ///   <item><b>Pendant tokenize</b> : <see cref="PrimeNormalizer"/> appelé
    ///     pour absorber les primes après un Word (= <c>f''</c>).</item>
    ///   <item><b>Post-tokenize</b> : <see cref="CaseToleranceLookup"/>
    ///     appelé pour reclasser un Word en function known (=
    ///     <c>Cos→\cos</c>, <c>OMEGA→\Omega</c>).</item>
    /// </list></para>
    ///
    /// <para>Les helpers <c>PrimeNormalizer</c> et <c>CaseToleranceLookup</c>
    /// sont des classes statiques pures, testables individuellement. Cette
    /// classe est leur façade pour qu'un caller (= Tokenizer ou autre)
    /// invoque <c>Normalizer.NormalizePrimes(text)</c> au lieu de
    /// <c>PrimeNormalizer.Normalize(text)</c>, ce qui rend explicite le scope
    /// « entre dans Normalization ».</para>
    /// </summary>
    public static class Normalizer
    {
        /// <summary>Normalise les caractères primes Unicode/typographiques
        /// vers <c>'</c> ASCII répétés. Cf. <see cref="PrimeNormalizer"/>.</summary>
        public static string NormalizePrimes(string? raw) => PrimeNormalizer.Normalize(raw);

        /// <summary>True si <paramref name="c"/> est un caractère prime
        /// (= apostrophe ou variant). Cf. <see cref="PrimeNormalizer"/>.</summary>
        public static bool IsPrimeChar(char c) => PrimeNormalizer.IsPrimeChar(c);

        /// <summary>Lookup case-tolerant. Cf. <see cref="CaseToleranceLookup"/>.</summary>
        public static bool TryLookupCaseTolerant(
            System.Collections.Generic.IReadOnlyDictionary<string, string> dict,
            string word, out string value)
            => CaseToleranceLookup.TryLookup(dict, word, out value);
    }
}
