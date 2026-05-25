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
            // Brief v5 §1 (2026-05-22) : l'espace est une frontière réelle.
            // On émet un token Sep(" ") entre chaque pair de tokens séparés
            // par du whitespace dans la source. Reclassement post-tokenize :
            // Word ↔ Symbol pour les relations connues (= "U", "in", …).
            var refined = new List<Token>(rawTokens.Count * 2);
            for (int i = 0; i < rawTokens.Count; i++)
            {
                var t = rawTokens[i];
                if (i > 0)
                {
                    var prev = rawTokens[i - 1];
                    // Insère Sep(" ") boundary si gap réel. Skip uniquement
                    // quand un des deux voisins est déjà un Sep("\n") (= éviter
                    // doublons Sep+Sep("\n") quand un `\n` côtoie un espace,
                    // ex. "a \n b"). Pour les autres Sep (`;`, `,`), continuer
                    // à insérer le Sep(" ") boundary autour (= comportement
                    // historique nécessaire au test Matrix_source_tokenizes_with_semi).
                    bool nextToNewline = (t.Kind == TokenKind.Sep && t.Text == "\n")
                                       || (prev.Kind == TokenKind.Sep && prev.Text == "\n");
                    if (t.Start > prev.End && !nextToNewline)
                    {
                        refined.Add(new Token(" ", TokenKind.Sep, prev.End, t.Start));
                    }
                }
                if (t.Kind == TokenKind.Word
                    && _vocab.Relations.TryGetValue(t.Text, out var relForReclass)
                    && MatchesRelationContext(relForReclass.Context, rawTokens, i))
                {
                    refined.Add(new Token(t.Text, TokenKind.Symbol, t.Start, t.End));
                }
                else if (t.Kind == TokenKind.Word
                    && TryLookupFunction(t.Text, out var funcLatex))
                {
                    // P16 : Word qui est une function known (sin, cos, ln…)
                    // est reclassé avec son rendu LaTeX (= \sin, \cos, \ln).
                    // Tolérance casse (2026-05-23) : `Cos` → `\cos` aussi
                    // (Word autocapitalize after `.` ou début de phrase).
                    refined.Add(new Token(funcLatex, TokenKind.Word, t.Start, t.End));
                }
                else
                {
                    refined.Add(t);
                }
            }
            // P30 (2026-05-22) : post-process angle `^<word>` au tout début
            // → token unique Word("\widehat{<word>}"). Évite un `if` hardcoded
            // dans MathEngine.Resolve. Brief : si on a un usage de `^` en
            // milieu de source, c'est un exposant (= traité par parser standard).
            return MergeLeadingCaretAngle(refined);
        }

        /// <summary>
        /// Vérifie qu'une relation peut être activée à la position
        /// <paramref name="i"/> dans le flux <paramref name="rawTokens"/>
        /// selon son <paramref name="context"/>. Utilisé au reclasse
        /// Word→Symbol pour les relations conditionnelles déclarées en
        /// YAML (= <c>context: 'isolated_between_brackets'</c> pour
        /// <c>u</c> entre intervalles).
        /// </summary>
        private static bool MatchesRelationContext(RelationContext context, IReadOnlyList<Token> rawTokens, int i)
        {
            switch (context)
            {
                case RelationContext.None:
                    return true;
                case RelationContext.IsolatedBetweenBrackets:
                {
                    // Sep blanc immédiatement avant ET après + voisins
                    // non-Sep sont des bracket chars. Note : rawTokens
                    // n'a pas encore les Sep boundaries injectés, donc
                    // « isolé » = positions Start/End espacées des voisins.
                    if (i - 1 < 0 || i + 1 >= rawTokens.Count) return false;
                    var prev = rawTokens[i - 1];
                    var next = rawTokens[i + 1];
                    var self = rawTokens[i];
                    bool sepBefore = self.Start > prev.End;
                    bool sepAfter = next.Start > self.End;
                    if (!sepBefore || !sepAfter) return false;
                    return IsBracketChar(prev) && IsBracketChar(next);
                }
                default:
                    return true;
            }
        }

        private static bool IsBracketChar(Token t)
            => (t.Kind == TokenKind.OpenDelim || t.Kind == TokenKind.CloseDelim)
               && (t.Text == "[" || t.Text == "]"
                   || t.Text == "(" || t.Text == ")"
                   || t.Text == "{" || t.Text == "}");

        /// <summary>Lookup function avec tolérance casse intelligente.
        /// Délègue à <see cref="Normalization.Normalizer.TryLookupCaseTolerant"/>
        /// (= Chantier 2 — extraction du Tokenizer vers module Normalization).</summary>
        private bool TryLookupFunction(string word, out string latex)
            => Normalization.Normalizer.TryLookupCaseTolerant(_vocab.Functions, word, out latex);

        private static IReadOnlyList<Token> MergeLeadingCaretAngle(List<Token> tokens)
        {
            if (tokens.Count < 2) return tokens;
            if (tokens[0].Kind != TokenKind.Symbol || tokens[0].Text != "^") return tokens;
            if (tokens[1].Kind != TokenKind.Word) return tokens;
            if (tokens[0].End != tokens[1].Start) return tokens; // doit être collé
            var merged = new Token(
                "\\widehat{" + tokens[1].Text + "}",
                TokenKind.Word, tokens[0].Start, tokens[1].End);
            var newList = new List<Token>(tokens.Count - 1);
            newList.Add(merged);
            for (int i = 2; i < tokens.Count; i++) newList.Add(tokens[i]);
            return newList;
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

                // Nombre (incluant virgule décimale FR).
                if (char.IsDigit(c))
                {
                    int s = i;
                    while (i < source.Length && char.IsDigit(source[i])) i++;
                    // Virgule décimale FR : ',' bordée de chiffres des deux côtés.
                    if (i < source.Length
                        && source[i].ToString() == _vocab.Decimal
                        && i + 1 < source.Length
                        && char.IsDigit(source[i + 1]))
                    {
                        i++;
                        while (i < source.Length && char.IsDigit(source[i])) i++;
                    }
                    tokens.Add(new Token(source.Substring(s, i - s), TokenKind.Number, s, i));
                    continue;
                }

                // Virgule isolée FR (= pas bordée chiffres) → séparateur (= colsep
                // dans une grille délimitée, résolu au parse cf. brief §1.1).
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
