using System;
using System.Collections.Generic;
using MathCursor.Engine.Vocabulary;

namespace MathCursor.Engine.Rewriting
{
    /// <summary>
    /// Parse une <b>pattern string</b> YAML vers une suite de
    /// <see cref="PatternElement"/>.
    ///
    /// <para>Mini-langage :</para>
    /// <list type="bullet">
    ///   <item><c>mot</c> → <see cref="Literal"/> requis.</item>
    ///   <item><c>mot?</c> → <see cref="Literal"/> optionnel.</item>
    ///   <item><c>{name}</c> → <see cref="Slot"/> de catégorie <c>expr</c>.</item>
    ///   <item><c>{name:type}</c> → <see cref="Slot"/> typé.</item>
    ///   <item><c>{name:grid}</c> → <see cref="GridSlot"/> (= matrice).</item>
    ///   <item><c>&lt;classe&gt;</c> / <c>&lt;classe&gt;?</c> →
    ///     <see cref="AnyLiteral"/> résolu via <c>vocab.Classes</c>.</item>
    /// </list>
    ///
    /// <para><b>Glued par adjacence</b> : si un élément n'a PAS d'espace avant
    /// lui dans la pattern string, il est marqué <see cref="PatternElement.Glued"/>
    /// (= exige absence de Sep dans l'input). Ainsi <c>{a:number}{b:letter}</c>
    /// (collé) ≠ <c>{a:number} {b:letter}</c> (espacé). L'espacement de la
    /// pattern string EST la spec.</para>
    ///
    /// <para>Moteur V2 (2026-05-29).</para>
    /// </summary>
    public static class PatternParser
    {
        public static Pattern Parse(string pattern, LocaleVocabulary? vocab = null)
        {
            var elements = new List<PatternElement>();
            if (string.IsNullOrWhiteSpace(pattern)) return new Pattern(elements);

            int i = 0;
            bool firstElement = true;
            while (i < pattern.Length)
            {
                // Détecte s'il y a eu un espace avant cet élément.
                bool hadSpace = false;
                while (i < pattern.Length && char.IsWhiteSpace(pattern[i]))
                {
                    hadSpace = true;
                    i++;
                }
                if (i >= pattern.Length) break;

                // Glued = pas d'espace avant ET pas le 1er élément.
                bool glued = !hadSpace && !firstElement;
                firstElement = false;

                char c = pattern[i];
                // '{' ouvre un slot SEULEMENT s'il est suivi d'une lettre
                // (= nom de slot). Sinon (`{ ` espace, etc.) c'est une accolade
                // littérale (= délimiteur d'ensemble `{ … }`).
                if (c == '{' && i + 1 < pattern.Length && char.IsLetter(pattern[i + 1]))
                {
                    int end = pattern.IndexOf('}', i + 1);
                    if (end < 0) throw new FormatException(
                        $"Pattern '{pattern}' : accolade '{{' non fermée.");
                    var inner = pattern.Substring(i + 1, end - i - 1);
                    elements.Add(ParseSlot(inner, glued));
                    i = end + 1;
                }
                else if (c == '<' && i + 1 < pattern.Length && char.IsLetter(pattern[i + 1]))
                {
                    // <classname> : '<' suivi d'une LETTRE (= classe vocab).
                    // Sinon (= '<=', '<=>') c'est un literal opérateur.
                    int end = pattern.IndexOf('>', i + 1);
                    if (end < 0) throw new FormatException(
                        $"Pattern '{pattern}' : chevron '<' non fermé.");
                    var className = pattern.Substring(i + 1, end - i - 1).Trim();
                    i = end + 1;
                    bool optional = i < pattern.Length && pattern[i] == '?';
                    if (optional) i++;
                    elements.Add(ResolveClass(className, optional, pattern, vocab));
                }
                else
                {
                    // Literal : lit jusqu'au prochain whitespace, début de slot
                    // ('{' suivi d'une lettre), ou début de classe ('<' suivi
                    // d'une lettre). Un '{'/'}' nu reste dans le literal
                    // (= accolade d'ensemble). On lit au moins 1 char.
                    int start = i;
                    i++; // consomme au moins le 1er char (= cas '{' ou '}' nu)
                    while (i < pattern.Length
                           && !char.IsWhiteSpace(pattern[i])
                           && !(pattern[i] == '{' && i + 1 < pattern.Length
                                && char.IsLetter(pattern[i + 1]))
                           && !(pattern[i] == '<' && i + 1 < pattern.Length
                                && char.IsLetter(pattern[i + 1])))
                        i++;
                    var word = pattern.Substring(start, i - start);
                    bool optional = word.EndsWith("?") && word.Length > 1;
                    if (optional) word = word.Substring(0, word.Length - 1);
                    elements.Add(new Literal(word, optional, glued));
                }
            }
            return new Pattern(elements);
        }

        private static PatternElement ParseSlot(string inner, bool glued)
        {
            int colon = inner.IndexOf(':');
            string name = (colon < 0 ? inner : inner.Substring(0, colon)).Trim();
            string type = colon < 0 ? "expr" : inner.Substring(colon + 1).Trim();

            if (type == "grid")
                return new GridSlot(name);
            if (type == "list")
                return new ListSlot(name);

            return new Slot(name, Categories.Parse(type), glued);
        }

        private static PatternElement ResolveClass(string className, bool optional,
            string pattern, LocaleVocabulary? vocab)
        {
            if (vocab == null || !vocab.Classes.TryGetValue(className, out var alts)
                || alts.Count == 0)
            {
                throw new FormatException(
                    $"Pattern '{pattern}' : classe <{className}> introuvable " +
                    "dans le vocab (section classes:).");
            }
            return new AnyLiteral(className, alts, optional);
        }
    }
}
