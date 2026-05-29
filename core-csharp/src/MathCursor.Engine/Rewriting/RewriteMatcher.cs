using System.Collections.Generic;
using System.Text;

namespace MathCursor.Engine.Rewriting
{
    /// <summary>Résultat d'un match : règle + range [Start, End) + slots
    /// capturés + flag partiel.</summary>
    public sealed class RewriteMatch
    {
        public RewriteRule Rule { get; }
        public int Start { get; }
        public int End { get; }
        public IReadOnlyDictionary<string, Item> Slots { get; }
        public bool IsPartial { get; }

        public RewriteMatch(RewriteRule rule, int start, int end,
            IReadOnlyDictionary<string, Item> slots, bool isPartial)
        {
            Rule = rule;
            Start = start;
            End = end;
            Slots = slots;
            IsPartial = isPartial;
        }

        public int Span => End - Start;

        /// <summary>Nombre de slots effectivement remplis (= non-\square).
        /// Sert au scoring « max slots pleins ».</summary>
        public int FilledSlots => Slots.Count;
    }

    /// <summary>
    /// Tente de matcher une <see cref="RewriteRule"/> contre la séquence
    /// d'<see cref="Item"/> à partir d'une position. Gère literals, classes,
    /// slots typés (subsumption), glued (= absence de Sep), et match partiel
    /// (= slots manquants si <see cref="RewriteRule.AllowPartial"/>).
    ///
    /// <para>Moteur V2 (2026-05-29).</para>
    /// </summary>
    public static class RewriteMatcher
    {
        public static RewriteMatch? TryMatch(RewriteRule rule, IReadOnlyList<Item> items, int start)
        {
            var slots = new Dictionary<string, Item>();
            bool anyLiteralMatched = false;
            bool anySlotMissing = false;
            int i = start;

            foreach (var elem in rule.Pattern.Elements)
            {
                // Glued : exige absence de Sep AVANT (= avant le skip).
                bool glued = elem.Glued;
                if (glued && i < items.Count && IsWsSep(items[i]))
                    return null;
                if (!glued)
                    while (i < items.Count && IsWsSep(items[i])) i++;

                switch (elem)
                {
                    case Literal lit:
                    {
                        if (i < items.Count && items[i].SourceText == lit.Text)
                        {
                            i++;
                            anyLiteralMatched = true;
                        }
                        else if (lit.Optional)
                        {
                            // skip sans consommer
                        }
                        else if (rule.AllowPartial)
                        {
                            anySlotMissing = true;
                        }
                        else return null;
                        break;
                    }

                    case AnyLiteral any:
                    {
                        bool matched = i < items.Count && Contains(any.Alternatives, items[i].SourceText);
                        if (matched)
                        {
                            i++;
                            anyLiteralMatched = true;
                        }
                        else if (any.Optional)
                        {
                            // skip
                        }
                        else if (rule.AllowPartial)
                        {
                            anySlotMissing = true;
                        }
                        else return null;
                        break;
                    }

                    case Slot slot:
                    {
                        if (i < items.Count && Categories.Subsumes(slot.Category, items[i].Category))
                        {
                            slots[slot.Name] = items[i];
                            i++;
                        }
                        else if (rule.AllowPartial)
                        {
                            anySlotMissing = true; // → \square dans l'emit
                        }
                        else return null;
                        break;
                    }

                    case GridSlot:
                    case RepeatGroup:
                        // Phase 5 : non implémenté en Phase 1.
                        return null;
                }
            }

            // Partial autorisé seulement si ≥ 1 literal a matché (= l'anchor
            // identifie la règle). Évite les partials sur règles sans anchor.
            bool isPartial = anySlotMissing;
            if (isPartial && !anyLiteralMatched) return null;

            return new RewriteMatch(rule, start, i, slots, isPartial);
        }

        /// <summary>Applique le template emit : <c>$name</c> → Latex du slot,
        /// slot manquant → <c>\square</c>.</summary>
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
                    sb.Append(slots.TryGetValue(name, out var item) ? item.Latex : @"\square");
                    i = j;
                    continue;
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        private static bool IsWsSep(Item item)
            => item is TokenItem t && t.Category == Category.Sep && t.Token.Text == " ";

        private static bool Contains(IReadOnlyList<string> list, string value)
        {
            for (int k = 0; k < list.Count; k++)
                if (list[k] == value) return true;
            return false;
        }

        // Noms alphanumériques only (= PAS '_', sinon "$a_$b" lirait le nom
        // "a_" au lieu de "$a" + literal "_" + "$b").
        private static bool IsNameStart(char c) => char.IsLetter(c);
        private static bool IsNameCont(char c) => char.IsLetterOrDigit(c);
    }
}
