using System.Text;

namespace MathCursor.Host
{
    /// <summary>
    /// Logique pure (sans dépendance Word) de normalisation des caractères
    /// "smart" insérés par Word AutoCorrect. À appliquer en amont du NER et
    /// du lexer pour ne pas perdre la détection math.
    /// <para>
    /// Cas typique : `g(x) - g(x+1) = 1/x` tapé devient `g(x) – g(x+1) = 1/x`
    /// après AutoCorrect (en-dash U+2013 entre 2 mots), ce que ni le NER ni
    /// le lexer ne reconnaissent comme `-`.
    /// </para>
    /// <para>
    /// <b>Invariant non négociable</b> : 1 char d'entrée → 1 char de sortie.
    /// Si la longueur change, les offsets entre Word doc et notre texte
    /// interne désynchronisent (caret, zones NER, etc.).
    /// </para>
    /// </summary>
    internal static class AutocorrectNormalizer
    {
        /// <summary>
        /// Normalise les caractères "smart" Word AutoCorrect vers leur
        /// équivalent ASCII. NBSP est laissé intact (traité côté Lexer).
        /// </summary>
        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? string.Empty;

            // Fast-path : si aucun char ciblé, on renvoie tel quel.
            bool needs = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '–' || c == '—' || c == '−'
                    || c == '‘' || c == '’' || c == '‚' || c == '′'
                    || c == '“' || c == '”' || c == '„' || c == '″')
                { needs = true; break; }
            }
            if (!needs) return s;

            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '–': sb.Append('-'); break;  // –  en-dash
                    case '—': sb.Append('-'); break;  // —  em-dash
                    case '−': sb.Append('-'); break;  // −  minus sign
                    case '‘': sb.Append('\''); break; // '  left single
                    case '’': sb.Append('\''); break; // '  right single (apostrophe typo)
                    case '‚': sb.Append('\''); break; // ‚  low single
                    case '′': sb.Append('\''); break; // ′  prime
                    case '“': sb.Append('"'); break;  // "  left double
                    case '”': sb.Append('"'); break;  // "  right double
                    case '„': sb.Append('"'); break;  // „  low double
                    case '″': sb.Append('"'); break;  // ″  double prime
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}
