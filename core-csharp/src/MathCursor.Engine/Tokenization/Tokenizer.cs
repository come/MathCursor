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
                if (t.Kind == TokenKind.Word && _vocab.Relations.ContainsKey(t.Text))
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

        /// <summary>Lookup function avec tolérance casse : <c>Cos</c>, <c>COS</c>
        /// matchent <c>cos</c>. Évite que Word autocapitalize ne mange le
        /// reclasse function (= user-report 2026-05-23 « Cos x »).</summary>
        private bool TryLookupFunction(string word, out string latex)
        {
            if (_vocab.Functions.TryGetValue(word, out latex)) return true;
            // Tolérance casse : lowercase, alors retry.
            var lower = word.ToLowerInvariant();
            if (lower != word && _vocab.Functions.TryGetValue(lower, out latex)) return true;
            latex = string.Empty;
            return false;
        }

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
                if (TryReadSymbolAhead(source, i, out var symText, out var symLen))
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
                    while (i < source.Length && IsPrimeChar(source[i])) i++;
                    string rawWord = source.Substring(s, i - s);
                    string word = (i > primesStart) ? NormalizePrimes(rawWord) : rawWord;

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

        private static bool TryReadSymbolAhead(string src, int i, out string text, out int len)
        {
            // Multi-char d'abord (greedy).
            string[] multiCharOps = new[]
            {
                "<=>", "<=", ">=", "!=", "=>", "≤", "≥", "≠", "≡",
                "//", "⊥", "∈", "⊂", "∪", "∩", "∧", "∨", "⇒", "⇔", "→", "·",
            };
            foreach (var op in multiCharOps)
            {
                if (i + op.Length > src.Length) continue;
                if (string.CompareOrdinal(src, i, op, 0, op.Length) == 0)
                {
                    text = op; len = op.Length; return true;
                }
            }
            // Mono-char : symboles maths classiques.
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

        private static bool IsWordStart(char c) =>
            char.IsLetter(c) || c == '\\' || c == '∀' || c == '∃';

        // P26 (2026-05-22) : digits NE continuent PAS un Word — on veut
        // que `x2` soit 2 tokens (= Word "x" + Number "2") pour que la
        // règle letter-sup-number puisse s'appliquer (= `x2` → `x^{2}`).
        private static bool IsWordContinuation(char c) =>
            char.IsLetter(c);

        /// <summary>
        /// P21 : caractères prime (= notation Lagrange dérivées). ASCII +
        /// variants Unicode (Word auto-corrige <c>'</c> en <c>'</c>).
        /// </summary>
        private static bool IsPrimeChar(char c) =>
            c == '\''     // U+0027 apostrophe ASCII
            || c == '"'   // U+0022 quote ASCII (= '' = 2 primes)
            || c == '’'   // U+2019 right single
            || c == '‘'   // U+2018 left single
            || c == '′'   // U+2032 math prime
            || c == '”'   // U+201D right double
            || c == '“'   // U+201C left double
            || c == '″'   // U+2033 math double prime
            || c == '‴'   // U+2034 triple prime
            || c == '⁗';  // U+2057 quadruple prime

        /// <summary>
        /// Normalise les primes en ASCII LaTeX standard : <c>'</c> = 1 prime,
        /// <c>"</c> ou variants double = <c>''</c>, etc.
        /// </summary>
        private static string NormalizePrimes(string raw)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in raw)
            {
                if (!IsPrimeChar(c)) { sb.Append(c); continue; }
                int count = c switch
                {
                    '\'' => 1, '’' => 1, '‘' => 1, '′' => 1,
                    '"' => 2, '”' => 2, '“' => 2, '″' => 2,
                    '‴' => 3, '⁗' => 4,
                    _ => 1,
                };
                for (int i = 0; i < count; i++) sb.Append('\'');
            }
            return sb.ToString();
        }

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
