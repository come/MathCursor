using System.Collections.Generic;

namespace MathCursor.Engine.Normalization
{
    /// <summary>
    /// Lookup case-tolerant dans un dict <c>name → latex</c> avec stratégie
    /// d'essais successifs :
    /// <list type="number">
    ///   <item>Match exact (= conserve la casse pour distinguer
    ///     <c>omega</c> vs <c>Omega</c>).</item>
    ///   <item>Si all-uppercase (≥ 2 chars), retry avec Capitalized
    ///     (= 1er char haut, reste bas) → matcher les majuscules grecques
    ///     <c>Omega</c>, <c>Sigma</c>, etc. User-report 2026-05-25
    ///     « OMEGA → \Omega ».</item>
    ///   <item>Fallback lowercase (= tolérance Word autocapitalize
    ///     <c>Cos</c> → <c>cos</c>).</item>
    /// </list>
    ///
    /// <para>Migration Chantier 2 (2026-05-25) : extrait du
    /// <c>Tokenizer.TryLookupFunction</c> dans un module Normalization dédié.
    /// Utilisable sur n'importe quel dict (Functions, Anchors, etc.).</para>
    /// </summary>
    public static class CaseToleranceLookup
    {
        /// <summary>
        /// Tente <paramref name="word"/> dans <paramref name="dict"/> avec la
        /// stratégie ci-dessus. Retourne <c>true</c> + valeur si trouvé.
        /// </summary>
        public static bool TryLookup(IReadOnlyDictionary<string, string> dict, string word, out string value)
        {
            // 1. Exact match (= préserve la casse).
            if (dict.TryGetValue(word, out value)) return true;
            // 2. All-upper retry → Capitalized first char.
            if (word.Length >= 2 && word == word.ToUpperInvariant())
            {
                var capitalized = char.ToUpperInvariant(word[0]) + word.Substring(1).ToLowerInvariant();
                if (capitalized != word && dict.TryGetValue(capitalized, out value)) return true;
            }
            // 3. Fallback lowercase.
            var lower = word.ToLowerInvariant();
            if (lower != word && dict.TryGetValue(lower, out value)) return true;
            value = string.Empty;
            return false;
        }
    }
}
