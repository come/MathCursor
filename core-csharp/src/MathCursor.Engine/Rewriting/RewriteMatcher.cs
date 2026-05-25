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
            var lists = new Dictionary<string, IReadOnlyList<Item>>();
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

                    case RepeatGroup rep:
                    {
                        var captured = new List<Item>();
                        // Première occurrence (= obligatoire si MinCount ≥ 1).
                        while (i < items.Count && IsWhitespaceSep(items[i])) i++;
                        if (i >= items.Count) return null;
                        if (!CategoryMatches(items[i].Category, rep.InnerCategory)) return null;
                        captured.Add(items[i]);
                        i++;

                        // Occurrences suivantes : sep + inner.
                        while (true)
                        {
                            int probe = i;
                            while (probe < items.Count && IsWhitespaceSep(items[probe])) probe++;
                            if (probe >= items.Count) break;
                            // Séparateur attendu : si défini, exiger ; sinon tenter
                            // direct (= variantes sans séparateur écrites espacées).
                            if (rep.Separator != null)
                            {
                                if (items[probe].SourceText != rep.Separator) break;
                                probe++;
                                while (probe < items.Count && IsWhitespaceSep(items[probe])) probe++;
                                if (probe >= items.Count) break;
                            }
                            if (!CategoryMatches(items[probe].Category, rep.InnerCategory)) break;
                            captured.Add(items[probe]);
                            i = probe + 1;
                            if (rep.MaxCount >= 0 && captured.Count >= rep.MaxCount) break;
                        }

                        if (captured.Count < rep.MinCount) return null;
                        lists[rep.Name] = captured;
                        break;
                    }
                }
            }
            return new RewriteMatch(rule, startIndex, i, slots, lists);
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
        /// <see cref="Item.Latex"/> du slot capturé. Supporte aussi le filtre
        /// <c>$name | join: "STRING"</c> qui itère un slot capturé par
        /// <see cref="RepeatGroup"/> et concatène les <see cref="Item.Latex"/>
        /// séparés par <c>STRING</c>. Slot manquant → <c>\square</c>.</summary>
        public static string ApplyTemplate(string template,
            IReadOnlyDictionary<string, Item> slots,
            IReadOnlyDictionary<string, IReadOnlyList<Item>>? lists = null)
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

                    // Tente filtre `| join: "STRING"` si présent juste après.
                    int k = j;
                    while (k < template.Length && template[k] == ' ') k++;
                    if (k < template.Length && template[k] == '|')
                    {
                        var filter = TryParseJoinFilter(template, k, out int filterEnd);
                        if (filter != null && lists != null && lists.TryGetValue(name, out var items))
                        {
                            for (int ii = 0; ii < items.Count; ii++)
                            {
                                if (ii > 0) sb.Append(filter);
                                sb.Append(items[ii].Latex);
                            }
                            i = filterEnd;
                            continue;
                        }
                    }

                    if (slots.TryGetValue(name, out var item))
                        sb.Append(item.Latex);
                    else if (lists != null && lists.TryGetValue(name, out var rep))
                        sb.Append(string.Concat(rep)); // fallback sans filtre
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

        /// <summary>Tente de parser <c>| join: "STRING"</c> à partir de
        /// <paramref name="start"/>. Retourne <c>STRING</c> + position après
        /// le filtre, ou <c>null</c> si pas un filtre valide.</summary>
        private static string? TryParseJoinFilter(string template, int start, out int end)
        {
            end = start;
            int i = start;
            if (i >= template.Length || template[i] != '|') return null;
            i++;
            while (i < template.Length && template[i] == ' ') i++;
            if (i + 4 > template.Length || template.Substring(i, 4) != "join") return null;
            i += 4;
            while (i < template.Length && template[i] == ' ') i++;
            if (i >= template.Length || template[i] != ':') return null;
            i++;
            while (i < template.Length && template[i] == ' ') i++;
            if (i >= template.Length || template[i] != '"') return null;
            i++;
            int sepStart = i;
            while (i < template.Length && template[i] != '"') i++;
            if (i >= template.Length) return null;
            var sep = template.Substring(sepStart, i - sepStart);
            end = i + 1;
            return sep;
        }

        private static bool IsNameStart(char c) => char.IsLetter(c) || c == '_';
        private static bool IsNameCont(char c) => char.IsLetterOrDigit(c) || c == '_';
    }

    /// <summary>Résultat d'un match : règle + range + slots capturés (= dict
    /// pour <see cref="Slot"/> simple, dict de listes pour <see cref="RepeatGroup"/>).</summary>
    public sealed class RewriteMatch
    {
        public RewriteRule Rule { get; }
        public int Start { get; }
        public int End { get; }
        public IReadOnlyDictionary<string, Item> Slots { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<Item>> Lists { get; }

        public RewriteMatch(RewriteRule rule, int start, int end,
            IReadOnlyDictionary<string, Item> slots,
            IReadOnlyDictionary<string, IReadOnlyList<Item>>? lists = null)
        {
            Rule = rule;
            Start = start;
            End = end;
            Slots = slots;
            Lists = lists ?? new Dictionary<string, IReadOnlyList<Item>>();
        }

        public int Span => End - Start;
        public override string ToString() => $"Match[{Rule.Id} @ {Start}..{End}, {Slots.Count} slots, {Lists.Count} lists]";
    }
}
