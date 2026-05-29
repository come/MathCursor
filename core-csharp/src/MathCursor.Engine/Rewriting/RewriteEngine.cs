using System.Collections.Generic;
using System.Linq;
using System.Text;
using MathCursor.Engine.Tokenization;
using MathCursor.Engine.Vocabulary;

namespace MathCursor.Engine.Rewriting
{
    /// <summary>
    /// Moteur de rewriting V2 (2026-05-29).
    ///
    /// <para><b>Phase 1 (= cette version)</b> : fixed-point multi-phase
    /// single-chain. Tokenize → Items → applique les règles par phases de
    /// priorité (0 token-fusion, 1 primitives, 2 anchors), leftmost-longest
    /// à chaque tour.</para>
    ///
    /// <para><b>À venir</b> (ADR 2026-05-28, phases 2-4) : scan-keywords +
    /// scoping top-down + multi-chains beam search. Cette version pose la
    /// base fonctionnelle ; les cas de composition récursive avancés
    /// (= <c>1/sum k 0 n f(k)</c>) seront couverts par le scan-keywords.</para>
    /// </summary>
    public sealed class RewriteEngine
    {
        private const int Phase0Max = 50;    // token-fusion (priority < 50)
        private const int Phase1Max = 100;   // primitives  (priority < 100)
        private const int SafetyMax = 64;

        private readonly Tokenizer _tokenizer;
        private readonly IReadOnlyList<RewriteRule> _rules;

        public RewriteEngine(LocaleVocabulary vocab, IReadOnlyList<RewriteRule> rules)
        {
            _tokenizer = new Tokenizer(vocab);
            _rules = rules;
        }

        public RewriteResult Resolve(string source)
        {
            if (string.IsNullOrEmpty(source)) return RewriteResult.Empty;
            var tokens = _tokenizer.Tokenize(source);
            if (tokens.Count == 0) return RewriteResult.Empty;

            var items = new List<Item>(tokens.Count);
            foreach (var t in tokens) items.Add(new TokenItem(t));

            var alternatives = new List<RewriteMatch>();
            string ruleId = "";

            var phase0 = _rules.Where(r => r.Priority < Phase0Max).ToList();
            var phase1 = _rules.Where(r => r.Priority < Phase1Max).ToList();

            RunPhase(items, phase0, alternatives, ref ruleId);
            RunPhase(items, phase1, alternatives, ref ruleId);
            RunPhase(items, _rules, alternatives, ref ruleId);

            var sb = new StringBuilder();
            foreach (var item in items) sb.Append(item.Latex);

            return new RewriteResult(sb.ToString(), items, alternatives, ruleId);
        }

        private static void RunPhase(List<Item> items, IReadOnlyList<RewriteRule> rules,
            List<RewriteMatch> alternatives, ref string ruleId)
        {
            if (rules.Count == 0) return;
            int safety = SafetyMax;
            while (safety-- > 0)
            {
                var matches = new List<RewriteMatch>();
                for (int p = 0; p < items.Count; p++)
                    foreach (var rule in rules)
                    {
                        var m = RewriteMatcher.TryMatch(rule, items, p);
                        if (m != null && m.Span > 0) matches.Add(m);
                    }
                if (matches.Count == 0) break;

                // leftmost-longest : Start asc, puis Span desc, puis full>partial,
                // puis Priority desc.
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
                var produced = new RewriteItem(
                    best.Rule.Id, best.Rule.Produces, src, latex, best.IsPartial);
                items.RemoveRange(best.Start, best.End - best.Start);
                items.Insert(best.Start, produced);
                ruleId = best.Rule.Id; // last wins (= règle englobante)
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
