using System.Collections.Generic;
using System.Text;

namespace MathCursor.Engine.Rewriting
{
    /// <summary>
    /// Tente de matcher une <see cref="RewriteRule"/> contre la séquence
    /// d'<see cref="Item"/> à partir d'une position donnée. Retourne
    /// <see cref="RewriteMatch"/> si OK, sinon <c>null</c>.
    ///
    /// <para>Migration Chantier 4 Phase A (2026-05-25) — POC rewriting-based.</para>
    /// </summary>
    public static class RewriteMatcher
    {
        /// <summary>Match unique (= 1 règle à 1 position).</summary>
        public static RewriteMatch? TryMatch(RewriteRule rule, IReadOnlyList<Item> items, int startIndex)
        {
            var slots = new Dictionary<string, Item>();
            int i = startIndex;
            foreach (var elem in rule.Pattern.Elements)
            {
                // Skip whitespace Sep entre éléments — pragmatique pour V1.
                while (i < items.Count && IsWhitespaceSep(items[i])) i++;
                if (i >= items.Count) return null;

                switch (elem)
                {
                    case Literal lit:
                        if (items[i].SourceText != lit.Text) return null;
                        i++;
                        break;

                    case Slot slot:
                        if (!CategoryMatches(items[i].Category, slot.Category)) return null;
                        slots[slot.Name] = items[i];
                        i++;
                        break;
                }
            }
            return new RewriteMatch(rule, startIndex, i, slots);
        }

        /// <summary>True si <paramref name="actual"/> satisfait une demande
        /// de catégorie <paramref name="requested"/>. Règles de subsumption :
        /// <see cref="Category.Any"/> accepte tout ; <see cref="Category.Expr"/>
        /// accepte tout Item « valeur » (= Letter, Number, Var, Expr,
        /// Interval, Set, Function, Vector) ; sinon match strict.</summary>
        private static bool CategoryMatches(Category actual, Category requested)
        {
            if (requested == Category.Any) return true;
            if (requested == actual) return true;
            if (requested == Category.Expr)
            {
                return actual == Category.Letter
                    || actual == Category.Number
                    || actual == Category.Var
                    || actual == Category.Expr
                    || actual == Category.Interval
                    || actual == Category.Set
                    || actual == Category.Function
                    || actual == Category.Vector;
            }
            return false;
        }

        private static bool IsWhitespaceSep(Item item)
        {
            return item is TokenItem t
                && t.Category == Category.Sep
                && t.Token.Text == " ";
        }

        /// <summary>Substitue les <c>$name</c> du template par
        /// <see cref="Item.Latex"/> du slot capturé. Slot manquant →
        /// <c>\square</c> (= popup guidée).</summary>
        public static string ApplyTemplate(string template, IReadOnlyDictionary<string, Item> slots)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;
            var sb = new StringBuilder(template.Length * 2);
            int i = 0;
            while (i < template.Length)
            {
                char c = template[i];
                if (c == '$' && i + 1 < template.Length && IsNameStart(template[i + 1]))
                {
                    int j = i + 1;
                    while (j < template.Length && IsNameCont(template[j])) j++;
                    var name = template.Substring(i + 1, j - (i + 1));
                    if (slots.TryGetValue(name, out var item))
                        sb.Append(item.Latex);
                    else
                        sb.Append(@"\square");
                    i = j;
                    continue;
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        private static bool IsNameStart(char c) => char.IsLetter(c) || c == '_';
        private static bool IsNameCont(char c) => char.IsLetterOrDigit(c) || c == '_';
    }

    /// <summary>Résultat d'un match : règle + range + slots capturés.</summary>
    public sealed class RewriteMatch
    {
        public RewriteRule Rule { get; }
        public int Start { get; }
        public int End { get; }
        public IReadOnlyDictionary<string, Item> Slots { get; }

        public RewriteMatch(RewriteRule rule, int start, int end, IReadOnlyDictionary<string, Item> slots)
        {
            Rule = rule;
            Start = start;
            End = end;
            Slots = slots;
        }

        public int Span => End - Start;
        public override string ToString() => $"Match[{Rule.Id} @ {Start}..{End}, {Slots.Count} slots]";
    }
}
