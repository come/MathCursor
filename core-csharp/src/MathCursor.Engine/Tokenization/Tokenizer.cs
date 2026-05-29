using System.Collections.Generic;
using System.Text;
using MathCursor.Engine.Vocabulary;

namespace MathCursor.Engine.Tokenization
{
    /// <summary>
    /// Tokenizer du brief v4 §1.1. Locale-aware via <see cref="LocaleVocabulary"/>.
    ///
    /// <para>Règles :</para>
    /// <list type="bullet">
    ///   <item>Séparateurs naturels = whitespace ∪ symboles maths auto-séparateurs.</item>
    ///   <item><c>=</c>, <c>-&gt;</c>, <c>tend vers</c> et autres glue selon vocab.</item>
    ///   <item><b>Virgule décimale FR</b> : virgule est <c>Number</c> si bordée
    ///     de chiffres des deux côtés (<c>3,14</c>). Sinon <c>Sep</c>.</item>
    ///   <item><c>;</c> est toujours <c>Sep</c> (rowsep).</item>
    ///   <item>Délimiteurs <c>( ) [ ] { }</c> = OpenDelim/CloseDelim individuels.</item>
    /// </list>
    /// </summary>
    public sealed class Tokenizer
    {
        private readonly LocaleVocabulary _vocab;

        public Tokenizer(LocaleVocabulary vocab)
        {
            _vocab = vocab ?? throw new System.ArgumentNullException(nameof(vocab));
        }

        public IReadOnlyList<Token> Tokenize(string source)
        {
            var rawTokens = TokenizeRaw(source);
            // L'espace est une frontière réelle : on insère un Sep(" ") entre
            // deux tokens séparés par du whitespace dans la source. Le
            // tokenizer ne fait QUE du découpage — l'aliasing (anchors,
            // fonctions) est centralisé dans AnchorExpander (= pré-pass moteur).
            var refined = new List<Token>(rawTokens.Count * 2);
            for (int i = 0; i < rawTokens.Count; i++)
            {
                var t = rawTokens[i];
                if (i > 0)
                {
                    var prev = rawTokens[i - 1];
                    // Skip le Sep(" ") boundary si un voisin est déjà Sep("\n")
                    // (= éviter doublons quand `\n` côtoie un espace).
                    bool nextToNewline = (t.Kind == TokenKind.Sep && t.Text == "\n")
                                       || (prev.Kind == TokenKind.Sep && prev.Text == "\n");
                    if (t.Start > prev.End && !nextToNewline)
                        refined.Add(new Token(" ", TokenKind.Sep, prev.End, t.Start));
                }
                refined.Add(t);
            }
            return refined;
        }

        private IReadOnlyList<Token> TokenizeRaw(string source)
        {
            var tokens = new List<Token>();
            if (string.IsNullOrEmpty(source)) return tokens;

            int i = 0;
            while (i < source.Length)
            {
                char c = source[i];

                // Newline = boundary multi-ligne (= align*/cases) — émet un
                // Sep dédié avec Text="\n" pour que MathEngine.Resolve puisse
                // détecter les frontières de ligne dans son pre-pass. Les
                // autres whitespaces restent skipped (le post-process
                // refined insère Sep(" ") entre tokens si besoin).
                // Cf. ADR 2026-05-23-Feat-engine-v2-multiline-port.
                if (c == '\n')
                {
                    tokens.Add(new Token("\n", TokenKind.Sep, i, i + 1));
                    i++;
                    continue;
                }
                if (c == '\r')
                {
                    // \r\n collapse en 1 Sep("\n"), \r seul aussi traité comme newline.
                    int len = (i + 1 < source.Length && source[i + 1] == '\n') ? 2 : 1;
                    tokens.Add(new Token("\n", TokenKind.Sep, i, i + len));
                    i += len;
                    continue;
                }

                if (char.IsWhiteSpace(c)) { i++; continue; }

                // Délimiteurs.
                if (c == '(' || c == '[' || c == '{')
                {
                    tokens.Add(new Token(c.ToString(), TokenKind.OpenDelim, i, i + 1));
                    i++; continue;
                }
                if (c == ')' || c == ']' || c == '}')
                {
                    tokens.Add(new Token(c.ToString(), TokenKind.CloseDelim, i, i + 1));
                    i++; continue;
                }
                if (c == ';')
                {
                    tokens.Add(new Token(";", TokenKind.Sep, i, i + 1));
                    i++; continue;
                }

                // Glue multi-char (-> →) avant les symboles individuels.
                if (TryReadGlueAhead(source, i, out var glueText, out var glueLen))
                {
                    tokens.Add(new Token(glueText, TokenKind.Glue, i, i + glueLen));
                    i += glueLen; continue;
                }

                // Nombre (chiffres seuls). La virgule décimale FR n'est PLUS
                // fusionnée ici : `,` est toujours un Sep, et la recombinaison
                // `0,5` → décimal se fait par la règle YAML `decimal` (= le `,`
                // est alors un séparateur dans `[0,1]`, un décimal dans `0,5`,
                // selon le contexte de réécriture, sans hack tokenizer).
                if (char.IsDigit(c))
                {
                    int s = i;
                    while (i < source.Length && char.IsDigit(source[i])) i++;
                    tokens.Add(new Token(source.Substring(s, i - s), TokenKind.Number, s, i));
                    continue;
                }

                // Virgule FR → toujours séparateur.
                if (c.ToString() == _vocab.Decimal)
                {
                    tokens.Add(new Token(c.ToString(), TokenKind.Sep, i, i + 1));
                    i++; continue;
                }

                // Symbole maths self-séparateur (multi-char : <= >= != <=>, etc.).
                if (TryReadSymbolAhead(source, i, _vocab, out var symText, out var symLen))
                {
                    // Re-classer comme Glue si listé comme tel (= "=" "->").
                    var kind = _vocab.IsGlue(symText) ? TokenKind.Glue : TokenKind.Symbol;
                    tokens.Add(new Token(symText, kind, i, i + symLen));
                    i += symLen; continue;
                }

                // Mot (ident/anchor/keyword). Lecture greedy lettres/chiffres mixés.
                if (IsWordStart(c))
                {
                    int s = i;
                    // Consomme le char de départ inconditionnellement (lettre,
                    // `\`, `∀`, `∃`), PUIS les continuations. Sinon un word-start
                    // non-lettre (= `\` de `\infty` ou nu, `∀`) ne s'auto-consomme
                    // pas — `IsWordContinuation` est false dessus — d'où mot vide
                    // et i figé → boucle infinie (`R \ {0}`).
                    i++;
                    while (i < source.Length && IsWordContinuation(source[i])) i++;
                    // P21 (2026-05-22) : absorber les primes collés (`'`, `''`,
                    // `"`, variants unicode) — c'est une notation Lagrange
                    // postfix `f'`, `f''`, `f"` (= dérivées primées).
                    int primesStart = i;
                    while (i < source.Length && Normalization.Normalizer.IsPrimeChar(source[i])) i++;
                    string rawWord = source.Substring(s, i - s);
                    string word = (i > primesStart)
                        ? Normalization.Normalizer.NormalizePrimes(rawWord)
                        : rawWord;

                    // Multi-mot pour glue/classes : "tend vers" doit être 1 token.
                    if (TryAbsorbMultiWordGlueOrClass(source, i, word, out var absorbed, out var absorbedEnd))
                    {
                        tokens.Add(new Token(absorbed, TokenKind.Glue, s, absorbedEnd));
                        i = absorbedEnd; continue;
                    }

                    tokens.Add(new Token(word, TokenKind.Word, s, i));
                    continue;
                }

                // Fallback : avale un caractère "inconnu" comme Symbol (sera
                // ignoré au parse si non géré).
                tokens.Add(new Token(c.ToString(), TokenKind.Symbol, i, i + 1));
                i++;
            }
            return tokens;
        }

        // ─── Helpers ──────────────────────────────────────────────────

        private bool TryReadGlueAhead(string src, int i, out string text, out int len)
        {
            // Match glue mono ou bi-caractère (-> / →).
            // Multi-word ("tend vers") géré séparément dans TryAbsorbMultiWordGlueOrClass.
            foreach (var g in _vocab.Glue)
            {
                if (g.Length == 0) continue;
                if (i + g.Length > src.Length) continue;
                if (string.CompareOrdinal(src, i, g, 0, g.Length) == 0)
                {
                    // Ne pas matcher la glue si elle s'enchaîne en autre symbole
                    // composé (=> est implies, pas glue =).
                    if (g == "=" && i + 1 < src.Length && src[i + 1] == '>') continue;
                    if (g == "=" && i + 1 < src.Length && src[i + 1] == '=') continue;
                    text = g; len = g.Length; return true;
                }
            }
            text = ""; len = 0; return false;
        }

        /// <summary>
        /// Lit un symbole math self-séparateur (single- ou multi-char). La
        /// liste des opérateurs est dérivée des <see cref="LocaleVocabulary.Relations"/>
        /// (= clés non-alphabétiques) plus une liste minimale de chars
        /// structurels (<c>+ - * / ^ _ &lt; &gt; |</c>) qui doivent toujours
        /// être tokenisés même s'ils ne sont pas dans Relations (= éviter
        /// que le tokenizer manque un opérateur si vocab incomplet).
        /// Migration Chantier 1 — 2026-05-25 : ex-hardcoded array.
        /// </summary>
        private static bool TryReadSymbolAhead(string src, int i,
            Vocabulary.LocaleVocabulary vocab, out string text, out int len)
        {
            // Multi-char d'abord (greedy). Les keys de Relations qui ne sont
            // pas alphabétiques (= "<=>", "=>", "→", "∪", "·", etc.) sont
            // candidates. On les trie par longueur décroissante pour le
            // greedy match.
            foreach (var key in MultiCharSymbolKeys(vocab))
            {
                if (i + key.Length > src.Length) continue;
                if (string.CompareOrdinal(src, i, key, 0, key.Length) == 0)
                {
                    text = key; len = key.Length; return true;
                }
            }
            // Mono-char : symboles structurels. Liste minimale qui DOIT
            // toujours être reconnue indépendamment du vocab (= les
            // opérateurs structurels arithmétiques + relations comp).
            char c = src[i];
            if (c == '+' || c == '-' || c == '*' || c == '/'
                || c == '^' || c == '_'
                || c == '=' || c == '<' || c == '>'
                || c == '|'
                || c == '.')  // P28 (2026-05-22) : `.` produit scalaire explicite
            {
                text = c.ToString(); len = 1; return true;
            }
            text = ""; len = 0; return false;
        }

        // Cache pour les keys multi-char (= calcul one-shot par vocab).
        // Tri par longueur décroissante pour le greedy match.
        private static readonly System.Collections.Generic.Dictionary<Vocabulary.LocaleVocabulary, string[]>
            _multiCharCache = new System.Collections.Generic.Dictionary<Vocabulary.LocaleVocabulary, string[]>();

        private static string[] MultiCharSymbolKeys(Vocabulary.LocaleVocabulary vocab)
        {
            if (_multiCharCache.TryGetValue(vocab, out var cached)) return cached;
            var list = new System.Collections.Generic.List<string>();
            foreach (var key in vocab.Relations.Keys)
            {
                // Keep non-alphabetic multi-char keys (= "<=>", "=>", "≤", "∪", …).
                if (key.Length < 2) continue;
                if (IsAlphabetic(key)) continue;
                list.Add(key);
            }
            list.Sort((a, b) => b.Length.CompareTo(a.Length));
            var arr = list.ToArray();
            _multiCharCache[vocab] = arr;
            return arr;
        }

        private static bool IsAlphabetic(string s)
        {
            foreach (var c in s) if (!char.IsLetter(c)) return false;
            return true;
        }

        private static bool IsWordStart(char c) =>
            char.IsLetter(c) || c == '\\' || c == '∀' || c == '∃';

        // P26 (2026-05-22) : digits NE continuent PAS un Word — on veut
        // que `x2` soit 2 tokens (= Word "x" + Number "2") pour que la
        // règle letter-sup-number puisse s'appliquer (= `x2` → `x^{2}`).
        private static bool IsWordContinuation(char c) =>
            char.IsLetter(c);

        // IsPrimeChar + NormalizePrimes migrés vers Normalization/
        // PrimeNormalizer.cs (= Chantier 2 — 2026-05-25).

        /// <summary>
        /// Si <paramref name="firstWord"/> est le début d'une glue multi-mot
        /// (ex. "tend" + " " + "vers"), l'absorbe en un seul token.
        /// </summary>
        private bool TryAbsorbMultiWordGlueOrClass(
            string src, int posAfterFirst, string firstWord,
            out string absorbed, out int newEnd)
        {
            // Cherche les classes/glue contenant un membre multi-mots commençant par firstWord.
            string[] multiCandidates = CollectMultiWordCandidates();
            int firstWordStart = posAfterFirst - firstWord.Length;
            foreach (var cand in multiCandidates)
            {
                if (!cand.StartsWith(firstWord + " ", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                int candEnd = firstWordStart + cand.Length;
                if (candEnd > src.Length) continue;
                if (string.Compare(src, firstWordStart, cand, 0, cand.Length,
                    System.StringComparison.OrdinalIgnoreCase) == 0)
                {
                    absorbed = src.Substring(firstWordStart, cand.Length);
                    newEnd = candEnd;
                    return true;
                }
            }
            absorbed = ""; newEnd = 0;
            return false;
        }

        private string[] CollectMultiWordCandidates()
        {
            // Collecte tous les membres multi-mots dans classes + glue.
            var list = new List<string>();
            foreach (var cls in _vocab.Classes.Values)
                foreach (var m in cls)
                    if (m.IndexOf(' ') >= 0) list.Add(m);
            foreach (var g in _vocab.Glue)
                if (g.IndexOf(' ') >= 0) list.Add(g);
            return list.ToArray();
        }
    }
}
