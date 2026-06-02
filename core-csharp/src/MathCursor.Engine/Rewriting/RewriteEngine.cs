using System.Collections.Generic;
using System.Linq;
using System.Text;
using MathCursor.Engine.Tokenization;
using MathCursor.Engine.Vocabulary;

namespace MathCursor.Engine.Rewriting
{
    /// <summary>
    /// Moteur de rewriting V2 — résolution anchor-scoped (Phase 2, 2026-05-29).
    ///
    /// <para>Algorithme :</para>
    /// <list type="number">
    ///   <item><b>Anchors d'abord</b> : on cherche le 1er anchor (= règle dont
    ///     le pattern commence par un mot-clé : sum, lim, frac, …). Ses slots
    ///     composites capturent des <b>chunks délimités par les espaces</b>,
    ///     chacun résolu récursivement. L'anchor produit un Item typé. On
    ///     répète tant qu'il reste des anchors.</item>
    ///   <item><b>Primitives ensuite</b> : sur les Items restants, fixed-point
    ///     leftmost-longest des règles non-anchor (= +, /, ^, _, f(x), parens,
    ///     relations), slots single-item.</item>
    /// </list>
    ///
    /// <para>Permet la composition récursive (= <c>1/sum k 0 n f(k)</c>) et
    /// le binding structurel (= <c>sum k=1 n k</c>) car l'anchor claime ses
    /// chunks avant que les primitives n'agissent.</para>
    /// </summary>
    public sealed class RewriteEngine
    {
        private const int Phase0Max = 50;    // token-fusion (priority < 50)
        private const int SafetyMax = 256;

        private readonly LocaleVocabulary _vocab;
        private readonly Tokenizer _tokenizer;
        private readonly IReadOnlyList<RewriteRule> _structuralRules;
        private readonly IReadOnlyList<RewriteRule> _phase0Rules;
        private readonly IReadOnlyList<RewriteRule> _primitiveRules;
        private readonly IReadOnlyList<RewriteRule> _relationRules;

        public RewriteEngine(LocaleVocabulary vocab, IReadOnlyList<RewriteRule> rules)
        {
            _vocab = vocab;
            _tokenizer = new Tokenizer(vocab);
            // Les relations (= proposition) sont extraites EN PREMIER : elles
            // forment la phase la plus lâche, appliquée APRÈS l'arithmétique
            // (cf. ADR 2026-06-02-Fix-relation-precedence). Un `=` ne doit
            // jamais être absorbé par une fraction.
            _relationRules = rules.Where(r => r.Produces == Category.Relation).ToList();
            var rest = rules.Where(r => r.Produces != Category.Relation).ToList();
            _structuralRules = rest.Where(IsStructural).ToList();
            var others = rest.Where(r => !IsStructural(r)).ToList();
            _phase0Rules = others.Where(r => r.Priority < Phase0Max).ToList();
            _primitiveRules = others.Where(r => r.Priority >= Phase0Max).ToList();
        }

        /// <summary>
        /// Règle <b>structurelle</b> (= chunk-scoped, ses slots composites
        /// capturent des chunks délimités par espaces, résolus récursivement) :
        /// <list type="bullet">
        ///   <item>commence par un mot-clé alphabétique (sum, frac, lim, …), OU</item>
        ///   <item>a ≥ 2 literals/classes structurels (= frame ses args :
        ///     funcdef <c>: -&gt;</c>, paren-group <c>( )</c>, function-call
        ///     <c>( )</c>).</item>
        /// </list>
        /// Les autres (= primitives binaires <c>+ / ^ _ =</c>, à 1 seul
        /// opérateur) composent des Items déjà résolus, slots single-item.
        /// </summary>
        private static bool IsStructural(RewriteRule r)
        {
            var anchor = r.Pattern.AnchorLiteral;
            if (anchor != null && anchor.Length > 0 && char.IsLetter(anchor[0]))
                return true;
            int literals = r.Pattern.Elements.Count(e =>
                (e is Literal l && !l.Optional) || (e is AnyLiteral a && !a.Optional));
            return literals >= 2;
        }

        public RewriteResult Resolve(string source)
        {
            if (string.IsNullOrEmpty(source)) return RewriteResult.Empty;
            var tokens = _tokenizer.Tokenize(source);
            if (tokens.Count == 0) return RewriteResult.Empty;

            // Pré-pass : expansion des anchors (alias exact + préfixe ≥3 chars).
            tokens = AnchorExpander.Expand(tokens, _vocab);

            var items = new List<Item>(tokens.Count);
            foreach (var t in tokens) items.Add(new TokenItem(t));

            var trace = new List<string>();
            string ruleId = "";

            // Le top-level est juste le chunk racine : même cœur de résolution.
            var alternatives = ResolveReadings(items, trace, ref ruleId);

            return new RewriteResult(Serialize(items), items, alternatives, ruleId, trace);
        }

        /// <summary>Sérialise une lecture en LaTeX (= concat des Items).
        /// Utilisé pour le TopLatex (réponse finale) ; les alternatives, elles,
        /// restent structurelles jusqu'à l'adapter.</summary>
        private static string Serialize(IReadOnlyList<Item> items)
        {
            var sb = new StringBuilder();
            foreach (var item in items) sb.Append(item.Latex);
            return sb.ToString();
        }

        /// <summary>Résout une liste d'Items in-place. Ordre :
        /// <list type="number">
        ///   <item><b>Structurels</b> (chunk-scoped) d'abord : ils claiment
        ///     leurs régions (= un intervalle <c>[0,1]</c> split son `,`)
        ///     AVANT que les fusions ne s'appliquent. Chaque chunk est résolu
        ///     récursivement (= pipeline complet), donc les fusions et
        ///     primitives internes au chunk fonctionnent.</item>
        ///   <item><b>Fusions token-level</b> (phase 0 : décimale, ±∞, 2x,
        ///     x²) sur le résiduel.</item>
        ///   <item><b>Primitives binaires</b> (+, /, ^, =, …).</item>
        /// </list>
        /// Cet ordre rend le `,` contextuel sans hack tokenizer : séparateur
        /// dans une liste structurelle, décimal sinon.</summary>
        /// <summary>Cœur de résolution UNIFIÉ (top-level ET chunks). Résout
        /// <paramref name="items"/> en place vers la meilleure lecture
        /// (déterministe : structurel → phase0 → primitives → relations) et
        /// RETOURNE les lectures alternatives (fork des ordres + dépliage des
        /// Variants). Un chunk n'est qu'un sous-arbre : la MÊME fonction y est
        /// rappelée récursivement par le matcher → collisions génériques à
        /// toute profondeur, un seul chemin. Cf. ADR
        /// 2026-06-02-Feat-recursive-collisions-variants.</summary>
        private List<IReadOnlyList<Item>> ResolveReadings(
            List<Item> items, List<string> trace, ref string ruleId)
        {
            StructuralLoop(items, trace, ref ruleId);
            var snapshot = new List<Item>(items);
            RunPrimitivePhase(items, _phase0Rules, trace, ref ruleId);
            RunPrimitivePhase(items, _primitiveRules, trace, ref ruleId);
            RunPrimitivePhase(items, _relationRules, trace, ref ruleId);

            var alternatives = new List<IReadOnlyList<Item>>();
            foreach (var reading in ForkReadings(snapshot))
                foreach (var expanded in ExpandVariants(reading))
                    alternatives.Add(expanded);
            return alternatives;
        }

        /// <summary>Boucle structurelle (anchors) leftmost, point-fixe.</summary>
        private void StructuralLoop(List<Item> items, List<string> trace, ref string ruleId)
        {
            int safety = SafetyMax;
            while (safety-- > 0)
            {
                var m = FindStructuralMatch(items);
                if (m == null) break;
                ApplyMatch(items, m, trace, ref ruleId);
            }
        }

        /// <summary>Cherche le meilleur match structurel : leftmost-start,
        /// puis full &gt; partial, puis plus de slots remplis, puis span large.</summary>
        private RewriteMatch? FindStructuralMatch(List<Item> items)
        {
            RewriteMatch? best = null;
            for (int p = 0; p < items.Count; p++)
            {
                if (items[p].Category == Category.Sep) continue;
                foreach (var rule in _structuralRules)
                {
                    var m = RewriteMatcher.TryMatchAnchor(rule, items, p, ResolveChunk);
                    if (m == null || m.Span <= 0) continue;
                    if (best == null || Better(m, best)) best = m;
                }
                // leftmost : dès qu'une position produit un match, on s'arrête.
                if (best != null) break;
            }
            return best;
        }

        /// <summary>m meilleur que cur : moins de slots manquants (full &gt;
        /// partial), puis span plus large, puis priorité.</summary>
        private static bool Better(RewriteMatch m, RewriteMatch cur)
        {
            if (m.IsPartial != cur.IsPartial) return !m.IsPartial;
            if (m.FilledSlots != cur.FilledSlots) return m.FilledSlots > cur.FilledSlots;
            if (m.Span != cur.Span) return m.Span > cur.Span;
            return m.Rule.Priority > cur.Rule.Priority;
        }

        /// <summary>Résout récursivement un chunk capturé → un seul Item, en y
        /// ATTACHANT ses lectures alternatives (Variants). Le best est
        /// déterministe ; les autres lectures (fork d'ordre + variants déjà
        /// portés par les sous-Items) deviennent des Variants → les collisions
        /// remontent récursivement à toute profondeur.</summary>
        private Item ResolveChunk(List<Item> chunk)
        {
            var tr = new List<string>();
            string rid = "";
            // Même cœur que le top-level : best en place + lectures alternatives.
            var alternatives = ResolveReadings(chunk, tr, ref rid);
            var best = Collapse(chunk);

            // Les lectures alternatives du chunk deviennent les Variants du best
            // → propagées vers le haut par l'émission (collisions récursives).
            var seen = new HashSet<string> { Serialize(chunk) };
            List<Item>? variants = null;
            foreach (var reading in alternatives)
            {
                var latex = Serialize(reading);
                if (!seen.Add(latex)) continue;
                (variants ??= new List<Item>()).Add(
                    new RewriteItem("chunk-alt", best.Category, "", latex, false));
                if (variants.Count >= VariantCap) break;
            }
            if (variants != null) best.Variants = variants;
            return best;
        }

        /// <summary>Effondre une liste d'Items en un seul (Item unique tel quel,
        /// sinon concaténation en Expr).</summary>
        private static Item Collapse(List<Item> items)
        {
            if (items.Count == 1) return items[0];
            var sb = new StringBuilder();
            var src = new StringBuilder();
            foreach (var it in items) { sb.Append(it.Latex); src.Append(it.SourceText); }
            return new RewriteItem("chunk", Category.Expr, src.ToString(), sb.ToString(), false);
        }

        private static void ApplyMatch(List<Item> items, RewriteMatch m,
            List<string> trace, ref string ruleId)
        {
            var src = ConcatSource(items, m.Start, m.End);
            var produced = Produce(m, src);
            items.RemoveRange(m.Start, m.End - m.Start);
            items.Insert(m.Start, produced);
            trace.Add($"{m.Rule.Id}@{m.Start} → {produced.Latex}");
            ruleId = m.Rule.Id;
        }

        // ─── Production + propagation des variants (collisions récursives) ───
        // Un slot dont l'Item porte des Variants (lectures alternatives) fait
        // produire à la règle les sorties correspondantes, attachées comme
        // Variants de l'Item produit. Récursif par construction : les variants
        // remontent de n'importe quelle profondeur. Cf. ADR 2026-06-02-Feat-
        // recursive-collisions-variants.
        private const int VariantCap = 16;

        private static RewriteItem Produce(RewriteMatch m, string src)
        {
            var latex = RewriteMatcher.ApplyTemplate(m.Rule.EmitTemplate, m.Slots);
            var item = new RewriteItem(m.Rule.Id, m.Rule.Produces, src, latex, m.IsPartial);
            var variants = SlotVariants(m, latex);
            if (variants.Count > 0) item.Variants = variants;
            return item;
        }

        /// <summary>Pour chaque slot porteur de variants, ré-émet en substituant
        /// CE seul slot (vary-one-slot, borné). Suffit pour propager un point de
        /// collision par niveau, récursivement.</summary>
        private static IReadOnlyList<Item> SlotVariants(RewriteMatch m, string bestLatex)
        {
            List<Item>? result = null;
            foreach (var kv in m.Slots)
            {
                var slotVariants = kv.Value.Variants;
                if (slotVariants.Count == 0) continue;
                foreach (var variant in slotVariants)
                {
                    var slots2 = new Dictionary<string, Item>();
                    foreach (var p in m.Slots) slots2[p.Key] = p.Value;
                    slots2[kv.Key] = variant;
                    var vlatex = RewriteMatcher.ApplyTemplate(m.Rule.EmitTemplate, slots2);
                    if (vlatex == bestLatex) continue;
                    (result ??= new List<Item>()).Add(
                        new RewriteItem(m.Rule.Id, m.Rule.Produces, "", vlatex, m.IsPartial));
                    if (result.Count >= VariantCap) return result;
                }
            }
            return result ?? (IReadOnlyList<Item>)System.Array.Empty<Item>();
        }

        /// <summary>Fixed-point leftmost-longest des règles single-item
        /// (DÉTERMINISTE = lecture « best »). Les lectures concurrentes ne sont
        /// PAS enregistrées ici : c'est le fork (<see cref="ForkReadings"/>) qui
        /// les produit (Principe 5).</summary>
        private static void RunPrimitivePhase(List<Item> items, IReadOnlyList<RewriteRule> rules,
            List<string> trace, ref string ruleId)
        {
            if (rules.Count == 0) return;
            int safety = SafetyMax;
            while (safety-- > 0)
            {
                var matches = CollectMatches(items, rules);
                if (matches.Count == 0) break;

                matches.Sort((a, b) =>
                {
                    int d = a.Start.CompareTo(b.Start);
                    if (d != 0) return d;
                    d = b.Span.CompareTo(a.Span);
                    if (d != 0) return d;
                    d = (a.IsPartial ? 1 : 0).CompareTo(b.IsPartial ? 1 : 0);
                    if (d != 0) return d;
                    return b.Rule.Priority.CompareTo(a.Rule.Priority);
                });

                var best = matches[0];
                ApplyMatch(items, best, trace, ref ruleId);
            }
        }

        // ─── Fork multi-chaînes (Principe 5) ──────────────────────────────
        // Explore les ORDRES d'application des règles primitives. Chaque
        // ambiguïté (plusieurs matchs disponibles) fork un état par candidat.
        // Les lectures terminales (= plus aucun match) sont des structures
        // d'Items — jamais sérialisées ici. La dédup/sérialisation latex se
        // fait en dernière étape (adapter). Borné par ForkMaxStates.

        private const int ForkMaxStates = 2000;

        private List<IReadOnlyList<Item>> ForkReadings(List<Item> baseItems)
        {
            // Phase 0 (fusions) d'abord, puis primitives sur chaque lecture :
            // respecte l'ordre des phases comme la résolution déterministe.
            var afterP0 = ForkPhase(new List<List<Item>> { baseItems }, _phase0Rules);
            var afterPrim = ForkPhase(afterP0, _primitiveRules);
            // Relations en dernier (= plus lâche) : appliquées sur chaque
            // lecture arithmétique, jamais avant.
            var afterRel = ForkPhase(afterPrim, _relationRules);
            var readings = new List<IReadOnlyList<Item>>(afterRel.Count);
            foreach (var r in afterRel) readings.Add(r);
            return readings;
        }

        private static List<List<Item>> ForkPhase(List<List<Item>> states, IReadOnlyList<RewriteRule> rules)
        {
            if (rules.Count == 0) return states;
            var terminals = new List<List<Item>>();
            var stack = new Stack<List<Item>>();
            foreach (var s in states) stack.Push(s);
            int budget = ForkMaxStates;
            while (stack.Count > 0 && budget-- > 0)
            {
                var state = stack.Pop();
                var matches = CollectMatches(state, rules);
                if (matches.Count == 0) { terminals.Add(state); continue; }
                foreach (var m in matches)
                {
                    var next = new List<Item>(state);
                    ApplyMatchInPlace(next, m);
                    stack.Push(next);
                }
            }
            // Budget épuisé : on retient les états restants tels quels.
            while (stack.Count > 0) terminals.Add(stack.Pop());
            return terminals;
        }

        private static List<RewriteMatch> CollectMatches(List<Item> items, IReadOnlyList<RewriteRule> rules)
        {
            var matches = new List<RewriteMatch>();
            for (int p = 0; p < items.Count; p++)
            {
                if (items[p].Category == Category.Sep) continue;
                foreach (var rule in rules)
                {
                    var m = RewriteMatcher.TryMatch(rule, items, p);
                    if (m != null && m.Span > 0) matches.Add(m);
                }
            }
            return matches;
        }

        private static void ApplyMatchInPlace(List<Item> items, RewriteMatch m)
        {
            var produced = Produce(m, ConcatSource(items, m.Start, m.End));
            items.RemoveRange(m.Start, m.End - m.Start);
            items.Insert(m.Start, produced);
        }

        /// <summary>Déplie les Variants des Items d'une lecture (vary-un-item,
        /// borné) → lectures alternatives. Sérialisation latex hors moteur.</summary>
        private static List<List<Item>> ExpandVariants(IReadOnlyList<Item> reading)
        {
            var results = new List<List<Item>> { new List<Item>(reading) };
            for (int i = 0; i < reading.Count; i++)
            {
                var variants = reading[i].Variants;
                if (variants.Count == 0) continue;
                foreach (var v in variants)
                {
                    var clone = new List<Item>(reading);
                    clone[i] = v;
                    results.Add(clone);
                    if (results.Count > VariantCap) return results;
                }
            }
            return results;
        }

        private static string ConcatSource(IReadOnlyList<Item> items, int start, int endExcl)
        {
            var sb = new StringBuilder();
            for (int i = start; i < endExcl; i++) sb.Append(items[i].SourceText);
            return sb.ToString();
        }
    }
}
