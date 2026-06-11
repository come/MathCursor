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
        /// équivalent ASCII, et FOLD les lettres accentuées (« intégrale » →
        /// « integrale ») — décision 2026-06-11 : on strip les accents en
        /// amont du NER/moteur plutôt que d'apprendre les diacritiques au
        /// lexer (qui jette « caractère inattendu: é »). × et ÷ ne sont PAS
        /// foldés (opérateurs du vocabulaire moteur). NBSP est laissé intact
        /// (traité côté Lexer).
        /// </summary>
        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? string.Empty;

            // Fast-path : si aucun char ciblé, on renvoie tel quel.
            // Inclut les chars de contrôle Word `\a` (cell-end, U+0007),
            // `\v` (line break Word, U+000B), `\b` (backspace, U+0008) que
            // Range.Text injecte dans une cellule de tableau — invisibles
            // visuellement mais cassent la détection NER. Cf. bug
            // 2026-05-11 : popup ne se lève pas en cellule.
            bool needs = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '–' || c == '—' || c == '−'
                    || c == '‘' || c == '’' || c == '‚' || c == '′'
                    || c == '“' || c == '”' || c == '„' || c == '″'
                    || c == '\a' || c == '\b' || c == '\v' || c == '\f'
                    || FoldDiacritic(c) != c)
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
                    // Chars de contrôle Word internes (cellule de tableau,
                    // line break) → espace pour préserver l'invariant 1:1
                    // et permettre au NER de tokenizer normalement.
                    case '\a': sb.Append(' '); break; // cell-end marker
                    case '\b': sb.Append(' '); break; // backspace (rare)
                    case '\v': sb.Append(' '); break; // <w:br/> line break
                    case '\f': sb.Append(' '); break; // page break (rare)
                    default: sb.Append(FoldDiacritic(c)); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Lettre latine accentuée → lettre nue (1 char → 1 char, invariant
        /// d'offsets respecté). Les ligatures œ/æ ne sont PAS foldées
        /// (2 lettres, casserait l'invariant) — hors mots-clés math de toute
        /// façon. × (U+00D7) et ÷ (U+00F7) sont exclus volontairement.
        /// </summary>
        private static char FoldDiacritic(char c)
        {
            switch (c)
            {
                case 'à': case 'á': case 'â': case 'ã': case 'ä': case 'å': return 'a';
                case 'è': case 'é': case 'ê': case 'ë': return 'e';
                case 'ì': case 'í': case 'î': case 'ï': return 'i';
                case 'ò': case 'ó': case 'ô': case 'õ': case 'ö': return 'o';
                case 'ù': case 'ú': case 'û': case 'ü': return 'u';
                case 'ç': return 'c';
                case 'ñ': return 'n';
                case 'ý': case 'ÿ': return 'y';
                case 'À': case 'Á': case 'Â': case 'Ã': case 'Ä': case 'Å': return 'A';
                case 'È': case 'É': case 'Ê': case 'Ë': return 'E';
                case 'Ì': case 'Í': case 'Î': case 'Ï': return 'I';
                case 'Ò': case 'Ó': case 'Ô': case 'Õ': case 'Ö': return 'O';
                case 'Ù': case 'Ú': case 'Û': case 'Ü': return 'U';
                case 'Ç': return 'C';
                case 'Ñ': return 'N';
                case 'Ý': return 'Y';
                default: return c;
            }
        }
    }
}
