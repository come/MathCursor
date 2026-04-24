using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FuzzySharp;

namespace MathCursor.Core.PatternEngine
{
    /// <summary>
    /// Découpe un span en pièces (mots/symboles) puis canonicalise chaque pièce
    /// via un lookup exact ou fuzzy dans la table des aliases.
    ///
    /// Règles de split :
    /// - caractères alphanum (unicode lettres + chiffres + '_') forment un mot
    /// - chaque autre caractère non-espace forme son propre token (opérateur, paren, ponctuation)
    /// - whitespace = séparateur, non conservé
    /// - sous-séquences d'opérateurs connues (->, <=, == etc.) sont greedy-matchées
    ///   pour correspondre aux aliases multi-caractères.
    /// </summary>
    public sealed class Tokenizer
    {
        private readonly PatternRepository _repo;
        private readonly Dictionary<string, TokenDef> _exactAlias;
        private readonly List<TokenDef> _fuzzyCandidates;
        // Opérateurs multi-caractères triés par longueur décroissante pour greedy match
        private readonly string[] _multiCharOps;

        public Tokenizer(PatternRepository repo)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));

            _exactAlias = new Dictionary<string, TokenDef>(StringComparer.Ordinal);
            _fuzzyCandidates = new List<TokenDef>();
            var ops = new HashSet<string>(StringComparer.Ordinal);

            // Priorité haute gagne : on itère du plus bas au plus haut, chaque priorité
            // écrase la précédente pour un même alias.
            foreach (var t in _repo.Tokens.OrderBy(tt => tt.Priority))
            {
                foreach (var alias in t.Aliases)
                {
                    if (_exactAlias.TryGetValue(alias, out var existing) && existing.Priority > t.Priority) continue;
                    _exactAlias[alias] = t;
                    if (IsOperatorOrPunct(alias))
                        ops.Add(alias);
                }
                if (t.FuzzyMaxDistance > 0)
                    _fuzzyCandidates.Add(t);
            }

            _multiCharOps = ops.Where(o => o.Length >= 2).OrderByDescending(o => o.Length).ToArray();
        }

        private static bool IsGreekLetter(char c)
        {
            return c >= 0x0370 && c <= 0x03FF;
        }

        private static bool ContainsBracket(string s)
        {
            foreach (var c in s)
                if (c == '[' || c == ']' || c == '(' || c == ')') return true;
            return false;
        }

        private static bool IsOperatorOrPunct(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var c in s) if (char.IsLetterOrDigit(c)) return false;
            return true;
        }

        /// <summary>Découpe et canonicalise le span en tokens (avec offsets).</summary>
        public List<CanonicalToken> Tokenize(string span)
        {
            // Préprocessing :
            // - "..."  → '' (guillemets doubles tapés comme seconde dérivée)
            // - "–"    → "-" (tiret demi-cadratin U+2013, auto-corrigé par Word)
            // - "—"    → "-" (tiret cadratin U+2014)
            // - "−"    → "-" (signe moins unicode U+2212)
            // L'espace insécable U+00A0 est déjà traité comme whitespace par
            // char.IsWhiteSpace dans le Split.
            span = span
                .Replace("\"", "''")
                .Replace('–', '-')
                .Replace('—', '-')
                .Replace('−', '-');

            var pieces = Split(span);
            // Merge des paires "lettre+digit" collées (Q1 → "Q1" single piece),
            // pour permettre leur canonicalisation en Q_1. Convention notations
            // indicées courtes (quartiles Q1/Q3, coordonnées x1/y2...).
            pieces = MergeLetterDigitPairs(pieces);
            // Merge de phrases multi-mots ("for all" → FORALL) avant canonicalisation
            pieces = MergePhrases(pieces);

            var result = new List<CanonicalToken>(pieces.Count);
            int prevEnd = 0;
            foreach (var (piece, start, end) in pieces)
            {
                var ct = Canonicalize(piece);
                ct.Start = start;
                ct.End = end;
                // HadSpaceBefore : vrai si au moins un whitespace sépare ce token
                // du précédent (ou s'il y a quelque chose avant le tout premier).
                ct.HadSpaceBefore = start > prevEnd;
                prevEnd = end;
                result.Add(ct);
            }
            return result;
        }

        // Fusionne "Q" + "1" adjacents (pas d'espace) en une seule pièce "Q1"
        // si la 1re est une seule lettre et la 2e est un nombre. Permet de
        // reconnaître Q1 comme Q_1 via canonicalisation.
        private static List<(string piece, int start, int end)> MergeLetterDigitPairs(List<(string piece, int start, int end)> pieces)
        {
            if (pieces.Count < 2) return pieces;
            var result = new List<(string, int, int)>(pieces.Count);
            int i = 0;
            while (i < pieces.Count)
            {
                if (i + 1 < pieces.Count)
                {
                    var a = pieces[i];
                    var b = pieces[i + 1];
                    bool adjacent = a.end == b.start;
                    bool aIsLetter = a.piece.Length == 1 && char.IsLetter(a.piece[0]);
                    bool bIsDigits = b.piece.Length >= 1;
                    if (bIsDigits)
                        foreach (var ch in b.piece) if (!char.IsDigit(ch)) { bIsDigits = false; break; }
                    if (adjacent && aIsLetter && bIsDigits)
                    {
                        result.Add((a.piece + b.piece, a.start, b.end));
                        i += 2;
                        continue;
                    }
                }
                result.Add(pieces[i]);
                i++;
            }
            return result;
        }

        private List<(string piece, int start, int end)> MergePhrases(List<(string piece, int start, int end)> pieces)
        {
            if (_repo.Phrases.Count == 0) return pieces;

            // Phrases triées par nombre de mots décroissant (match le plus long en premier)
            var phraseKeys = _repo.Phrases.Keys
                .Select(p => p.Split(' ').Where(w => w.Length > 0).ToArray())
                .Where(words => words.Length >= 2)
                .OrderByDescending(w => w.Length)
                .ToList();

            var result = new List<(string, int, int)>();
            int i = 0;
            while (i < pieces.Count)
            {
                bool merged = false;
                foreach (var words in phraseKeys)
                {
                    if (i + words.Length > pieces.Count) continue;
                    bool match = true;
                    for (int k = 0; k < words.Length; k++)
                    {
                        if (!string.Equals(pieces[i + k].piece, words[k], StringComparison.OrdinalIgnoreCase))
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                    {
                        var joined = string.Join(" ", words);
                        int s = pieces[i].start;
                        int e = pieces[i + words.Length - 1].end;
                        result.Add((joined, s, e));
                        i += words.Length;
                        merged = true;
                        break;
                    }
                }
                if (!merged)
                {
                    result.Add(pieces[i]);
                    i++;
                }
            }
            return result;
        }

        private List<(string piece, int start, int end)> Split(string s)
        {
            var pieces = new List<(string, int, int)>();
            int i = 0;
            // Règle pragmatique pour la virgule décimale FR : on l'accepte UNIQUEMENT
            // si l'input ne contient AUCUN crochet / parenthèse. Les intervalles FR
            // utilisent des brackets dans les deux sens (]a;b], [a;b[) donc un
            // simple tracking de profondeur ne suffit pas — mieux vaut désactiver
            // le mode décimal en présence de crochets (où la virgule est alors
            // toujours un séparateur d'intervalle ou de liste).
            bool allowCommaDecimal = !ContainsBracket(s);
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }

                // Séquence de digits. '.' toujours décimal, ',' décimal seulement
                // si allowCommaDecimal ET entre deux chiffres.
                //   "10,5"   → NUMBER (décimal FR, sans brackets)
                //   "[0,1]"  → LBRACKET NUMBER COMMA NUMBER RBRACKET (intervalle)
                if (char.IsDigit(c))
                {
                    int start = i;
                    while (i < s.Length)
                    {
                        if (char.IsDigit(s[i])) { i++; continue; }
                        if (s[i] == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1]))
                        { i++; continue; }
                        if (s[i] == ',' && allowCommaDecimal
                            && i + 1 < s.Length && char.IsDigit(s[i + 1]))
                        { i++; continue; }
                        break;
                    }
                    pieces.Add((s.Substring(start, i - start), start, i));
                    continue;
                }

                // Mot alphabétique (underscore est un token séparé). Les lettres
                // grecques (U+0370..U+03FF) forment leur propre token isolé :
                // "λu" → 2 tokens (LAMBDA, u), pas un mot "λu" unique.
                if (char.IsLetter(c))
                {
                    int start = i;
                    if (IsGreekLetter(c))
                    {
                        i++; // une lettre grecque = un token court
                    }
                    else
                    {
                        while (i < s.Length && char.IsLetter(s[i]) && !IsGreekLetter(s[i])) i++;
                    }
                    pieces.Add((s.Substring(start, i - start), start, i));
                    continue;
                }

                // Opérateur multi-caractères connu
                bool matched = false;
                foreach (var op in _multiCharOps)
                {
                    if (i + op.Length <= s.Length && s.Substring(i, op.Length) == op)
                    {
                        pieces.Add((op, i, i + op.Length));
                        i += op.Length;
                        matched = true;
                        break;
                    }
                }
                if (matched) continue;

                // Caractère unique
                pieces.Add((c.ToString(), i, i + 1));
                i++;
            }
            return pieces;
        }

        private CanonicalToken Canonicalize(string piece)
        {
            // Phrase multi-mots mergée (ex. "for all" → FORALL)
            if (piece.Contains(' ') && _repo.Phrases.TryGetValue(piece, out var mapped))
            {
                if (mapped != null && _repo.Tokens.FirstOrDefault(t => t.Name == mapped) is { } tokenDef)
                {
                    return new CanonicalToken
                    {
                        Name = tokenDef.Name,
                        Raw = piece,
                        Canonical = tokenDef.Canonical.Length > 0 ? tokenDef.Canonical : piece,
                    };
                }
                // Phrase sans mapping → connector silencieux
                return new CanonicalToken { Generic = "CONNECTOR", Raw = piece, Canonical = "", IsConnector = true };
            }

            // Match exact alias (prioritaire : si un mot est à la fois alias ET connector,
            // on garde la sémantique math. Ex: "dans" est connector ET alias de IN)
            if (_exactAlias.TryGetValue(piece, out var exact))
            {
                bool isConnector = exact.Role == "connector";
                return new CanonicalToken
                {
                    Name = exact.Name,
                    Raw = piece,
                    Canonical = exact.Canonical.Length > 0 ? exact.Canonical : piece,
                    IsConnector = isConnector,
                };
            }

            // Fallback case-insensitive pour les mots-clés multi-char (≥ 3 lettres).
            // Permet "Lim" / "LIM" → LIMIT. On évite les single/deux lettres pour ne
            // pas capturer "P"/"p", "R"/"r" etc. qui sont des variables vs tokens.
            if (piece.Length >= 3 && piece.All(char.IsLetter))
            {
                var lower = piece.ToLowerInvariant();
                if (lower != piece && _exactAlias.TryGetValue(lower, out var lowExact))
                {
                    bool isConnector2 = lowExact.Role == "connector";
                    return new CanonicalToken
                    {
                        Name = lowExact.Name,
                        Raw = piece,
                        Canonical = lowExact.Canonical.Length > 0 ? lowExact.Canonical : piece,
                        IsConnector = isConnector2,
                    };
                }
            }

            // Connector / stop word (silencieux)
            if (_repo.Connectors.Contains(piece) || _repo.StopWords.Contains(piece))
                return new CanonicalToken { Generic = "CONNECTOR", Raw = piece, Canonical = piece, IsConnector = true };

            // Nombre littéral. Si la virgule décimale française est présente, on
            // la wrap en {,} pour que LaTeX préserve l'espacement (convention FR :
            // 10{,}5 plutôt que 10,5 qui insérerait un espace après la virgule).
            if (IsNumberLiteral(piece))
            {
                string canonical = piece.Contains(',') ? piece.Replace(",", "{,}") : piece;
                return new CanonicalToken { Generic = "NUMBER", Raw = piece, Canonical = canonical };
            }

            // Fuzzy match (si mot alphanum uniquement — pas sur les symboles)
            if (piece.All(ch => char.IsLetterOrDigit(ch)))
            {
                var fuzzy = FindFuzzy(piece);
                if (fuzzy != null)
                    return new CanonicalToken
                    {
                        Name = fuzzy.Name,
                        Raw = piece,
                        Canonical = fuzzy.Canonical.Length > 0 ? fuzzy.Canonical : piece,
                        IsConnector = fuzzy.Role == "connector",
                    };
            }

            // Suite compacte "un", "vn", "wn" → canonical "u_n", "v_n", "w_n".
            // Permet aux EXPR slots capturant ces tokens de les rendre correctement.
            if (IsSequenceIdentFormat(piece))
                return new CanonicalToken { Generic = "IDENT", Raw = piece, Canonical = piece[0] + "_" + piece[1] };

            // Pair lettre+digit (Q1, Q3, x1, y2…) → "X_n" : convention des notations
            // indicées courtes (quartiles, coordonnées indexées, etc.). Fusionné
            // en amont par MergeLetterDigitPairs.
            if (IsLetterDigitPair(piece))
                return new CanonicalToken
                {
                    Generic = "IDENT",
                    Raw = piece,
                    Canonical = piece[0] + "_" + piece.Substring(1)
                };

            // Sinon : identifiant libre
            return new CanonicalToken { Generic = "IDENT", Raw = piece, Canonical = piece };
        }

        private static bool IsLetterDigitPair(string raw)
        {
            if (raw.Length < 2) return false;
            if (!char.IsLetter(raw[0])) return false;
            for (int i = 1; i < raw.Length; i++)
                if (!char.IsDigit(raw[i])) return false;
            return true;
        }

        private static bool IsSequenceIdentFormat(string raw)
        {
            if (raw.Length != 2) return false;
            if (raw[1] != 'n' && raw[1] != 'N') return false;
            return "uvwstUVWST".IndexOf(raw[0]) >= 0;
        }

        private static bool IsNumberLiteral(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            bool hasDigit = false;
            foreach (var c in s)
            {
                if (char.IsDigit(c)) { hasDigit = true; continue; }
                if (c == '.' || c == ',') continue;
                return false;
            }
            return hasDigit;
        }

        private TokenDef? FindFuzzy(string piece)
        {
            // Pas de fuzzy sur les mots courts : "to"↔"oo", "a"↔"2" etc. sont trop
            // permissifs. Les abréviations courantes (int, lim, sum) sont exactes.
            if (piece.Length < 3) return null;

            TokenDef? best = null;
            int bestDist = int.MaxValue;
            foreach (var td in _fuzzyCandidates)
            {
                foreach (var alias in td.Aliases)
                {
                    if (alias.Length < 3) continue;
                    int d = LevenshteinDistance(piece, alias);
                    if (d > td.FuzzyMaxDistance) continue;
                    // Garde-fou : distance ne doit pas dépasser la moitié de la plus
                    // courte des deux chaînes, pour bloquer les matches délirants.
                    int minLen = Math.Min(piece.Length, alias.Length);
                    if (d * 2 > minLen) continue;
                    if (d < bestDist) { bestDist = d; best = td; }
                }
            }
            return best;
        }

        // Distance de Levenshtein simple (pas besoin de FuzzySharp pour ça,
        // mais on pourrait basculer dessus si on veut des ratios pondérés).
        private static int LevenshteinDistance(string a, string b)
        {
            if (a.Length == 0) return b.Length;
            if (b.Length == 0) return a.Length;
            var prev = new int[b.Length + 1];
            var curr = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                curr[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    curr[j] = Math.Min(
                        Math.Min(curr[j - 1] + 1, prev[j] + 1),
                        prev[j - 1] + cost);
                }
                (prev, curr) = (curr, prev);
            }
            return prev[b.Length];
        }
    }
}
