using System.Collections.Generic;
using MathCursor.Engine.Vocabulary;

namespace MathCursor.Engine.Rewriting.Yaml
{
    /// <summary>
    /// Parse une <b>shape string</b> du format YAML actuel
    /// (<c>"somme {var} =? {from:bound} {body}"</c>) vers une suite de
    /// <see cref="PatternElement"/> consommable par <see cref="RewriteMatcher"/>.
    ///
    /// <para>Tokens supportés :</para>
    /// <list type="bullet">
    ///   <item><c>{name}</c> → <see cref="Slot"/> de catégorie <see cref="Category.Expr"/>.</item>
    ///   <item><c>{name:type}</c> → <see cref="Slot"/> de catégorie mappée
    ///     (= <c>var→Letter</c>, <c>const→Number</c>, autre→<see cref="Category.Expr"/>).</item>
    ///   <item><c>mot</c> → <see cref="Literal"/>.</item>
    ///   <item><c>mot?</c> → <see cref="Literal"/> optionnel (= <see cref="PatternElement.OptLit"/>).</item>
    ///   <item><c>&lt;classe&gt;?</c> → IGNORÉ en V1 (= filler/to qui réfèrent
    ///     une liste de mots du vocab — à étendre).</item>
    /// </list>
    ///
    /// <para>Migration Chantier 4 Phase C-2 (2026-05-25).</para>
    /// </summary>
    public static class ShapeParser
    {
        /// <summary>Parse une shape sans résolution de classes
        /// (= <c>&lt;classname&gt;?</c> ignoré). Pour résoudre les classes,
        /// utiliser la surcharge avec <see cref="LocaleVocabulary"/>.</summary>
        public static IReadOnlyList<PatternElement> Parse(string shape)
            => Parse(shape, vocab: null);

        /// <summary>Parse une shape avec résolution de <c>&lt;classname&gt;?</c>
        /// vers <see cref="AnyLiteral"/> via <paramref name="vocab"/>
        /// <see cref="LocaleVocabulary.Classes"/>. Phase C-3 (2026-05-25).</summary>
        public static IReadOnlyList<PatternElement> Parse(string shape, LocaleVocabulary? vocab)
        {
            var elements = new List<PatternElement>();
            if (string.IsNullOrWhiteSpace(shape)) return elements;
            int i = 0;
            while (i < shape.Length)
            {
                while (i < shape.Length && char.IsWhiteSpace(shape[i])) i++;
                if (i >= shape.Length) break;

                if (shape[i] == '{')
                {
                    int end = shape.IndexOf('}', i + 1);
                    if (end < 0) break;
                    var inner = shape.Substring(i + 1, end - i - 1);
                    elements.Add(ParseSlot(inner));
                    i = end + 1;
                    continue;
                }

                if (shape[i] == '<')
                {
                    int end = shape.IndexOf('>', i + 1);
                    if (end < 0) break;
                    int after = end + 1;
                    bool isOptional = after < shape.Length && shape[after] == '?';
                    if (isOptional) after++;
                    var className = shape.Substring(i + 1, end - i - 1).Trim();

                    // Résout via vocab.Classes si disponible. Sinon skip
                    // silencieusement (= retro-compat ChC-2).
                    if (vocab != null && vocab.Classes.TryGetValue(className, out var alts) && alts.Count > 0)
                    {
                        if (isOptional)
                            elements.Add(PatternElement.OptAnyLit(alts));
                        // V1 : si pas optional, on ignore aussi (= peu utilisé,
                        // à étendre quand un cas concret le réclame).
                    }
                    i = after;
                    continue;
                }

                // Mot literal (= jusqu'au prochain whitespace ou délimiteur).
                int wordStart = i;
                while (i < shape.Length
                       && !char.IsWhiteSpace(shape[i])
                       && shape[i] != '{'
                       && shape[i] != '<')
                    i++;
                var word = shape.Substring(wordStart, i - wordStart);
                if (word.EndsWith("?") && word.Length > 1)
                    elements.Add(PatternElement.OptLit(word.Substring(0, word.Length - 1)));
                else
                    elements.Add(PatternElement.Lit(word));
            }
            return elements;
        }

        private static PatternElement ParseSlot(string inner)
        {
            // `name` ou `name:type`
            int colon = inner.IndexOf(':');
            string name = colon < 0 ? inner : inner.Substring(0, colon);
            string type = colon < 0 ? "" : inner.Substring(colon + 1);
            return PatternElement.Slot(name.Trim(), MapType(type.Trim()));
        }

        /// <summary>Map les types YAML (<c>var</c>, <c>const</c>, <c>bound</c>,
        /// <c>body</c>, <c>term</c>, <c>expr</c>, <c>set</c>) vers les
        /// <see cref="Category"/> du rewriting.
        ///
        /// <para>V1 : tous les types compositifs (<c>bound</c>, <c>body</c>,
        /// <c>term</c>, <c>expr</c>, <c>set</c>) sont mappés sur
        /// <see cref="Category.Expr"/> — la composition bottom-up via règles
        /// primitives résout les expressions multi-Items. Phase B+ a démontré
        /// que c'est suffisant.</para></summary>
        public static Category MapType(string type)
        {
            return type switch
            {
                "var" => Category.Letter,
                "const" => Category.Number,
                "" or "bound" or "body" or "term" or "expr" or "set" => Category.Expr,
                _ => Category.Expr,
            };
        }
    }
}
