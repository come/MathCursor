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
            var blocks = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, Item>>>();
            int i = startIndex;
            foreach (var elem in rule.Pattern.Elements)
            {
                // Si l'élément est Literal avec NoSepBefore, on VÉRIFIE
                // qu'il n'y a pas de Sep avant (= avant le skip).
                if (elem is Literal litCheck && litCheck.NoSepBefore
                    && i < items.Count && IsWhitespaceSep(items[i]))
                {
                    return null;  // attendu collé, mais Sep présent
                }
                while (i < items.Count && IsWhitespaceSep(items[i])) i++;
                if (i >= items.Count) return null;

                switch (elem)
                {
                    case Literal lit:
                        if (items[i].SourceText != lit.Text)
                        {
                            if (lit.Optional) break;  // skip sans avancer
                            return null;
                        }
                        i++;
                        break;

                    case AnyLiteral any:
                    {
                        bool matched = false;
                        foreach (var alt in any.Alternatives)
                        {
                            if (items[i].SourceText == alt) { matched = true; break; }
                        }
                        if (!matched)
                        {
                            if (any.Optional) break;
                            return null;
                        }
                        i++;
                        break;
                    }

                    case Slot slot:
                        if (!CategoryMatches(items[i].Category, slot.Category)) return null;
                        slots[slot.Name] = items[i];
                        i++;
                        break;

                    case RepeatGroup rep when rep.IsComposite:
                    {
                        // Mode inner composite : chaque occurrence matche toute
                        // la sous-séquence d'inner-elements. Capture une list
                        // de dicts (1 dict de sub-slots par occurrence).
                        if (!TryMatchRepeatComposite(rep, items, i, out var occList, out int newI))
                            return null;
                        blocks[rep.Name] = occList;
                        i = newI;
                        break;
                    }

                    case RepeatGroup rep:
                    {
                        // Mode 1-slot : liste plate d'Items.
                        if (!TryMatchRepeatSimple(rep, items, i, out var captured, out int newI))
                            return null;
                        lists[rep.Name] = captured;
                        i = newI;
                        break;
                    }
                }
            }
            return new RewriteMatch(rule, startIndex, i, slots, lists, blocks);
        }

        private static bool TryMatchRepeatSimple(RepeatGroup rep, IReadOnlyList<Item> items,
            int startI, out List<Item> captured, out int newI)
        {
            captured = new List<Item>();
            int i = startI;
            while (i < items.Count && IsWhitespaceSep(items[i])) i++;
            if (i >= items.Count || !CategoryMatches(items[i].Category, rep.InnerCategory))
            {
                newI = startI;
                return false;
            }
            captured.Add(items[i]);
            i++;
            while (true)
            {
                int probe = i;
                while (probe < items.Count && IsWhitespaceSep(items[probe])) probe++;
                if (probe >= items.Count) break;
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
            newI = i;
            return captured.Count >= rep.MinCount;
        }

        private static bool TryMatchRepeatComposite(RepeatGroup rep, IReadOnlyList<Item> items,
            int startI, out List<IReadOnlyDictionary<string, Item>> occurrences, out int newI)
        {
            occurrences = new List<IReadOnlyDictionary<string, Item>>();
            int i = startI;
            // 1ère occurrence (= obligatoire si MinCount ≥ 1).
            if (!TryMatchInnerSequence(rep.InnerElements!, items, i, out var firstSlots, out int afterFirst))
            {
                newI = startI;
                return false;
            }
            occurrences.Add(firstSlots);
            i = afterFirst;

            // Occurrences suivantes : sep + inner.
            while (true)
            {
                int probe = i;
                while (probe < items.Count && IsWhitespaceSep(items[probe])) probe++;
                if (probe >= items.Count) break;
                if (rep.Separator != null)
                {
                    if (items[probe].SourceText != rep.Separator) break;
                    probe++;
                }
                if (!TryMatchInnerSequence(rep.InnerElements!, items, probe, out var occSlots, out int afterOcc))
                    break;
                occurrences.Add(occSlots);
                i = afterOcc;
                if (rep.MaxCount >= 0 && occurrences.Count >= rep.MaxCount) break;
            }

            newI = i;
            return occurrences.Count >= rep.MinCount;
        }

        private static bool TryMatchInnerSequence(
            IReadOnlyList<PatternElement> innerElements,
            IReadOnlyList<Item> items, int startI,
            out IReadOnlyDictionary<string, Item> slots, out int newI)
        {
            var dict = new Dictionary<string, Item>();
            int i = startI;
            foreach (var elem in innerElements)
            {
                while (i < items.Count && IsWhitespaceSep(items[i])) i++;
                if (i >= items.Count) { slots = dict; newI = startI; return false; }

                switch (elem)
                {
                    case Literal lit:
                        if (items[i].SourceText != lit.Text) { slots = dict; newI = startI; return false; }
                        i++;
                        break;
                    case Slot slot:
                        if (!CategoryMatches(items[i].Category, slot.Category)) { slots = dict; newI = startI; return false; }
                        dict[slot.Name] = items[i];
                        i++;
                        break;
                    // Pas de RepeatGroup imbriqué pour V1 — composer 2 règles.
                    default:
                        slots = dict;
                        newI = startI;
                        return false;
                }
            }
            slots = dict;
            newI = i;
            return true;
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
            // Set ⊃ Interval — un intervalle est un ensemble. Permet à une
            // règle `{a:set} union {b:set}` de matcher aussi des intervalles
            // (= la même règle gère `\mathbb{N} \cup \mathbb{R}` et
            // `[0;1] \cup [2;3]`).
            if (requested == Category.Set && actual == Category.Interval) return true;
            return false;
        }

        private static bool IsWhitespaceSep(Item item)
        {
            return item is TokenItem t
                && t.Category == Category.Sep
                && t.Token.Text == " ";
        }

        /// <summary>Substitue les <c>$name</c> du template par
        /// <see cref="Item.Latex"/> du slot capturé. Supporte aussi :
        /// <list type="bullet">
        ///   <item><c>$name | join: "STRING"</c> — itère une <see cref="RepeatGroup"/>
        ///     simple (= 1 slot), concatène les <see cref="Item.Latex"/>.</item>
        ///   <item><c>$listName.slotName | join: "STRING"</c> — itère une
        ///     <see cref="RepeatGroup"/> composite, extrait <c>slotName</c>
        ///     de chaque occurrence, joint avec <c>STRING</c>. Utilisé p.ex.
        ///     pour slurp de fraction sur N termes.</item>
        /// </list>
        /// Slot manquant → <c>\square</c>.</summary>
        public static string ApplyTemplate(string template,
            IReadOnlyDictionary<string, Item> slots,
            IReadOnlyDictionary<string, IReadOnlyList<Item>>? lists = null,
            IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, Item>>>? blocks = null)
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

                    // Forme `$listName.slotName` (= accès à un sub-slot d'un
                    // RepeatGroup composite).
                    string? subName = null;
                    if (j < template.Length && template[j] == '.' && j + 1 < template.Length
                        && IsNameStart(template[j + 1]))
                    {
                        int s = j + 1;
                        while (s < template.Length && IsNameCont(template[s])) s++;
                        subName = template.Substring(j + 1, s - (j + 1));
                        j = s;
                    }

                    // Tente filtre `| join: "STRING"` si présent juste après.
                    int k = j;
                    while (k < template.Length && template[k] == ' ') k++;
                    if (k < template.Length && template[k] == '|')
                    {
                        var filter = TryParseJoinFilter(template, k, out int filterEnd);
                        if (filter != null)
                        {
                            // Cas composite : $list.slot | join
                            if (subName != null && blocks != null && blocks.TryGetValue(name, out var occList))
                            {
                                for (int ii = 0; ii < occList.Count; ii++)
                                {
                                    if (ii > 0) sb.Append(filter);
                                    if (occList[ii].TryGetValue(subName, out var subItem))
                                        sb.Append(subItem.Latex);
                                    else
                                        sb.Append(@"\square");
                                }
                                i = filterEnd;
                                continue;
                            }
                            // Cas simple : $list | join
                            if (subName == null && lists != null && lists.TryGetValue(name, out var items))
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
                    }

                    if (slots.TryGetValue(name, out var item))
                        sb.Append(item.Latex);
                    else if (lists != null && lists.TryGetValue(name, out var rep))
                        sb.Append(string.Concat(rep));
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

        // Bug Phase D-4+ : `_` retiré de IsNameCont. Sinon le template
        // `$a_$b` (= prim-subscript) parse `$a_` comme une variable nommée
        // `a_` au lieu de `$a` + literal `_`. Conséquence : slot `a_` non
        // trouvé → `\square` émis → output `\squarei` au lieu de `a_i`.
        // Les noms de variables sont désormais alphanumériques only ; pour
        // un nom avec `_`, utiliser `${name_with_underscore}` (non
        // implémenté V1).
        private static bool IsNameStart(char c) => char.IsLetter(c);
        private static bool IsNameCont(char c) => char.IsLetterOrDigit(c);
    }

    /// <summary>Résultat d'un match : règle + range + slots capturés.
    /// <list type="bullet">
    ///   <item><see cref="Slots"/> : Item simple par nom (<see cref="Slot"/>).</item>
    ///   <item><see cref="Lists"/> : liste d'Items (<see cref="RepeatGroup"/> 1-slot).</item>
    ///   <item><see cref="Blocks"/> : liste de dicts d'Items (<see cref="RepeatGroup"/> composite).</item>
    /// </list></summary>
    public sealed class RewriteMatch
    {
        public RewriteRule Rule { get; }
        public int Start { get; }
        public int End { get; }
        public IReadOnlyDictionary<string, Item> Slots { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<Item>> Lists { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, Item>>> Blocks { get; }

        public RewriteMatch(RewriteRule rule, int start, int end,
            IReadOnlyDictionary<string, Item> slots,
            IReadOnlyDictionary<string, IReadOnlyList<Item>>? lists = null,
            IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, Item>>>? blocks = null)
        {
            Rule = rule;
            Start = start;
            End = end;
            Slots = slots;
            Lists = lists ?? new Dictionary<string, IReadOnlyList<Item>>();
            Blocks = blocks ?? new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, Item>>>();
        }

        public int Span => End - Start;
        public override string ToString() => $"Match[{Rule.Id} @ {Start}..{End}, {Slots.Count} slots, {Lists.Count} lists, {Blocks.Count} blocks]";
    }
}
