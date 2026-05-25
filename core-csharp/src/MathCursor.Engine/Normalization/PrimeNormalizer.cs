using System.Text;

namespace MathCursor.Engine.Normalization
{
    /// <summary>
    /// Normalisation des caractères primes Lagrange. Word autocorrige
    /// l'apostrophe ASCII <c>'</c> en typographique <c>’</c> (U+2019), et
    /// le double-quote <c>"</c> en <c>”</c> (U+201D). Plus les variants
    /// math <c>′ ″ ‴ ⁗</c>. Cette classe les canonicalise en ASCII <c>'</c>
    /// répétés (= <c>''</c> pour double, <c>'''</c> pour triple, etc.) ce
    /// qui est la convention LaTeX standard pour <c>f'</c> / <c>f''</c>.
    ///
    /// <para>Migration Chantier 2 (2026-05-25) : extrait du <c>Tokenizer</c>
    /// dans un module Normalization dédié pour respecter le principe « le
    /// tokenizer ne fait que char→Token, les transformations sont isolées ».</para>
    /// </summary>
    public static class PrimeNormalizer
    {
        /// <summary>True si <paramref name="c"/> est un caractère prime
        /// (= apostrophe ASCII, quote ASCII, variants Unicode math/typographiques).</summary>
        public static bool IsPrimeChar(char c) =>
            c == '\''      // U+0027 apostrophe ASCII
            || c == '"'    // U+0022 quote ASCII (= '' = 2 primes)
            || c == '’'    // U+2019 right single quote
            || c == '‘'    // U+2018 left single quote
            || c == '′'    // U+2032 math prime
            || c == '”'    // U+201D right double quote
            || c == '“'    // U+201C left double quote
            || c == '″'    // U+2033 math double prime
            || c == '‴'    // U+2034 triple prime
            || c == '⁗';   // U+2057 quadruple prime

        /// <summary>
        /// Normalise tous les caractères primes en <c>'</c> ASCII répétés.
        /// Quote double → <c>''</c>, math double prime → <c>''</c>, triple
        /// → <c>'''</c>, quadruple → <c>''''</c>.
        /// </summary>
        public static string Normalize(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            var sb = new StringBuilder(raw!.Length);
            foreach (var c in raw)
            {
                if (!IsPrimeChar(c)) { sb.Append(c); continue; }
                int count = PrimeCount(c);
                for (int i = 0; i < count; i++) sb.Append('\'');
            }
            return sb.ToString();
        }

        /// <summary>Nombre de primes ASCII représentés par un caractère.</summary>
        public static int PrimeCount(char c) => c switch
        {
            '\'' => 1, '’' => 1, '‘' => 1, '′' => 1,
            '"' => 2, '”' => 2, '“' => 2, '″' => 2,
            '‴' => 3,
            '⁗' => 4,
            _ => 1,
        };
    }
}
