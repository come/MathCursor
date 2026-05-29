using System;
using System.Collections.Generic;
using MathCursor.Engine.Tokenization;
using MathCursor.Engine.Vocabulary;

namespace MathCursor.Engine.Rewriting
{
    /// <summary>
    /// Pré-pass d'expansion des alias (vocab-driven, pas de hardcoded). Pour
    /// chaque token Word :
    /// <list type="bullet">
    ///   <item><b>Anchor</b> : alias exact (<c>somme</c> → <c>sum</c>) ou
    ///     préfixe ≥3 chars unique (<c>som</c> → <c>sum</c>, <c>inte</c> →
    ///     <c>int</c>).</item>
    ///   <item><b>Fonction</b> : lookup case-tolérant (<c>cos</c> / <c>Cos</c>
    ///     → <c>\cos</c>), qui devient catégorie Function.</item>
    /// </list>
    /// Centralise tout l'aliasing en un seul endroit (= le tokenizer ne fait
    /// plus QUE du découpage char→Token). Préfixe ambigu (≥2 canoniques) →
    /// laissé tel quel (départagé par le multi-chains à venir).
    ///
    /// <para>Phase 6-7 + cleanup tokenizer (2026-05-29).</para>
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
                    // 1. Anchor (alias exact ou préfixe).
                    var canon = ResolveAnchor(t.Text, vocab.Anchors);
                    if (canon != null && canon != t.Text)
                    {
                        result.Add(new Token(canon, TokenKind.Word, t.Start, t.End));
                        continue;
                    }
                    // 2. Fonction (case-tolérant) : cos/Cos → \cos.
                    if (Normalization.Normalizer.TryLookupCaseTolerant(vocab.Functions, t.Text, out var fn)
                        && fn != t.Text)
                    {
                        result.Add(new Token(fn, TokenKind.Word, t.Start, t.End));
                        continue;
                    }
                }
                result.Add(t);
            }
            return result;
        }

        private static string? ResolveAnchor(string word, IReadOnlyDictionary<string, string> anchors)
        {
            if (anchors.TryGetValue(word, out var exact)) return exact;

            if (word.Length < MinPrefix) return null;
            string? found = null;
            foreach (var kv in anchors)
            {
                if (kv.Key.Length <= word.Length) continue;
                if (!kv.Key.StartsWith(word, StringComparison.OrdinalIgnoreCase)) continue;
                if (found == null) found = kv.Value;
                else if (found != kv.Value) return null; // ambigu
            }
            return found;
        }
    }
}
