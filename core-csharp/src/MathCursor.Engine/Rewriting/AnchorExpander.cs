using System;
using System.Collections.Generic;
using System.Linq;
using MathCursor.Engine.Tokenization;
using MathCursor.Engine.Vocabulary;

namespace MathCursor.Engine.Rewriting
{
    /// <summary>
    /// Pré-pass d'expansion des anchors : remplace un token Word qui est
    /// <list type="bullet">
    ///   <item>un <b>alias exact</b> (= <c>somme</c> → <c>sum</c>), OU</item>
    ///   <item>un <b>préfixe ≥3 chars unique</b> (= <c>som</c> → <c>sum</c>,
    ///     <c>inte</c> → <c>int</c>),</item>
    /// </list>
    /// par le mot-clé canonique. Vocab-driven (= <c>vocab.Anchors</c>), pas de
    /// hardcoded. Si le préfixe est ambigu (≥2 canoniques distincts), on
    /// laisse le token tel quel (= sera départagé par le multi-chains à venir).
    ///
    /// <para>Cf. ADR anchor unifié + prefix-match 3-chars (Phase 6-7).</para>
    /// </summary>
    public static class AnchorExpander
    {
        private const int MinPrefix = 3;

        public static List<Token> Expand(IReadOnlyList<Token> tokens, LocaleVocabulary vocab)
        {
            var result = new List<Token>(tokens.Count);
            foreach (var t in tokens)
            {
                if (t.Kind == TokenKind.Word)
                {
                    var canon = ResolveAnchor(t.Text, vocab.Anchors);
                    if (canon != null && canon != t.Text)
                    {
                        result.Add(new Token(canon, TokenKind.Word, t.Start, t.End));
                        continue;
                    }
                }
                result.Add(t);
            }
            return result;
        }

        private static string? ResolveAnchor(string word, IReadOnlyDictionary<string, string> anchors)
        {
            // 1. Alias exact.
            if (anchors.TryGetValue(word, out var exact)) return exact;

            // 2. Préfixe ≥3 chars. Collecte les canoniques des alias préfixés.
            if (word.Length < MinPrefix) return null;
            string? found = null;
            foreach (var kv in anchors)
            {
                if (kv.Key.Length <= word.Length) continue;
                if (!kv.Key.StartsWith(word, StringComparison.OrdinalIgnoreCase)) continue;
                if (found == null) found = kv.Value;
                else if (found != kv.Value) return null; // ambigu → laisse tel quel
            }
            return found;
        }
    }
}
