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

        private readonly Tokenizer _tokenizer;
        private readonly IReadOnlyList<RewriteRule> _structuralRules;
        private readonly IReadOnlyList<RewriteRule> _phase0Rules;
        private readonly IReadOnlyList<RewriteRule> _primitiveRules;

        public RewriteEngine(LocaleVocabulary vocab, IReadOnlyList<RewriteRule> rules)
        {
            _tokenizer = new Tokenizer(vocab);
            _structuralRules = rules.Where(IsStructural).ToList();
            var others = rules.Where(r => !IsStructural(r)).ToList();
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

            var items = new List<Item>(tokens.Count);
            foreach (var t in tokens) items.Add(new TokenItem(t));

            var alternatives = new List<RewriteMatch>();
            var trace = new List<string>();
            string ruleId = ResolveItems(items, alternatives, trace);

            var sb = new StringBuilder();
            foreach (var item in items) sb.Append(item.Latex);

            return new RewriteResult(sb.ToString(), items, alternatives, ruleId, trace);
        }

        /// <summary>Résout une liste d'Items in-place. Anchors d'abord
        /// (chunk-scoped récursif), puis primitives. Retourne le ruleId top.</summary>
        private string ResolveItems(List<Item> items, List<RewriteMatch> alternatives, List<string> trace)
        {
            string ruleId = "";

            // Phase 0 : fusions token-level (signed-infinity, 2x, x2).
            RunPrimitivePhase(items, _phase0Rules, alternatives, trace, ref ruleId);

            // Structurels (chunk-scoped, récursif), tant qu'il y en a.
            int safety = SafetyMax;
            while (safety-- > 0)
            {
                var m = FindStructuralMatch(items);
                if (m == null) break;
                ApplyMatch(items, m, alternatives, trace, ref ruleId);
            }

            // Primitives (single-item, fixed-point) sur le résiduel.
            RunPrimitivePhase(items, _primitiveRules, alternatives, trace, ref ruleId);
            return ruleId;
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

        /// <summary>Résout récursivement un chunk capturé → un seul Item.
        /// Si la résolution laisse plusieurs Items, les concatène en un Expr.</summary>
        private Item ResolveChunk(List<Item> chunk)
        {
            var alts = new List<RewriteMatch>();
            var tr = new List<string>();
            ResolveItems(chunk, alts, tr);
            if (chunk.Count == 1) return chunk[0];
            var sb = new StringBuilder();
            var src = new StringBuilder();
            foreach (var it in chunk) { sb.Append(it.Latex); src.Append(it.SourceText); }
            return new RewriteItem("chunk", Category.Expr, src.ToString(), sb.ToString(), false);
        }

        private void ApplyMatch(List<Item> items, RewriteMatch m,
            List<RewriteMatch> alternatives, List<string> trace, ref string ruleId)
        {
            var latex = RewriteMatcher.ApplyTemplate(m.Rule.EmitTemplate, m.Slots);
            var src = ConcatSource(items, m.Start, m.End);
            var produced = new RewriteItem(m.Rule.Id, m.Rule.Produces, src, latex, m.IsPartial);
            items.RemoveRange(m.Start, m.End - m.Start);
            items.Insert(m.Start, produced);
            trace.Add($"{m.Rule.Id}@{m.Start} → {latex}");
            ruleId = m.Rule.Id;
        }

        /// <summary>Fixed-point leftmost-longest des règles single-item.</summary>
        private static void RunPrimitivePhase(List<Item> items, IReadOnlyList<RewriteRule> rules,
            List<RewriteMatch> alternatives, List<string> trace, ref string ruleId)
        {
            if (rules.Count == 0) return;
            int safety = SafetyMax;
            while (safety-- > 0)
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
                for (int k = 1; k < matches.Count; k++)
                    if (matches[k].Start == best.Start && matches[k].Span == best.Span)
                        alternatives.Add(matches[k]);

                var latex = RewriteMatcher.ApplyTemplate(best.Rule.EmitTemplate, best.Slots);
                var src = ConcatSource(items, best.Start, best.End);
                var produced = new RewriteItem(best.Rule.Id, best.Rule.Produces, src, latex, best.IsPartial);
                items.RemoveRange(best.Start, best.End - best.Start);
                items.Insert(best.Start, produced);
                trace.Add($"{best.Rule.Id}@{best.Start} → {latex}");
                ruleId = best.Rule.Id;
            }
        }

        private static string ConcatSource(IReadOnlyList<Item> items, int start, int endExcl)
        {
            var sb = new StringBuilder();
            for (int i = start; i < endExcl; i++) sb.Append(items[i].SourceText);
            return sb.ToString();
        }
    }
}
