using System.Collections.Generic;
using System.Linq;
using System.Text;
using MathCursor.Engine.Tokenization;
using MathCursor.Engine.Vocabulary;

namespace MathCursor.Engine.Rewriting
{
    /// <summary>
    /// Moteur de rewriting à point fixe (Phase A POC).
    ///
    /// <para>Algo :</para>
    /// <list type="number">
    ///   <item>Tokenize → liste d'<see cref="Item"/> (= <see cref="TokenItem"/>
    ///     pour chaque token).</item>
    ///   <item>Loop : scan chaque position × chaque règle. Si ≥ 1 match →
    ///     applique le meilleur (= leftmost-longest), stash les autres comme
    ///     <see cref="Collisions"/>. Sinon → break.</item>
    ///   <item>Emit : concat des <see cref="Item.Latex"/> restants.</item>
    /// </list>
    ///
    /// <para>Migration Chantier 4 Phase A (2026-05-25) — POC isolé, ne touche
    /// pas au <c>MathEngine</c> en prod.</para>
    /// </summary>
    public sealed class RewriteEngine
    {
        private readonly LocaleVocabulary _vocab;
        private readonly Tokenizer _tokenizer;
        private readonly IReadOnlyList<RewriteRule> _rules;

        public RewriteEngine(LocaleVocabulary vocab, IReadOnlyList<RewriteRule> rules)
        {
            _vocab = vocab;
            _tokenizer = new Tokenizer(vocab);
            _rules = rules;
        }

        /// <summary>Seuils Priority qui séparent les phases.
        /// <list type="bullet">
        ///   <item>Priority &lt; <see cref="Phase0Separator"/> (50) : phase 0
        ///     = fusions token-level (= <c>+\infty</c>, etc.).</item>
        ///   <item>Priority &lt; <see cref="PhaseSeparator"/> (100) : phase 1
        ///     = primitives (= paren-group, add, sub, function-call).</item>
        ///   <item>Priority &gt;= 100 : phase 2 = anchors.</item>
        /// </list>
        /// Phase D-3 + D-4+++ (2026-05-26).</summary>
        public const int Phase0Separator = 50;
        public const int PhaseSeparator = 100;

        public RewriteResult Resolve(string source)
        {
            if (string.IsNullOrEmpty(source)) return RewriteResult.Empty;
            var tokens = _tokenizer.Tokenize(source);
            if (tokens.Count == 0) return RewriteResult.Empty;

            var items = new List<Item>(tokens.Count);
            foreach (var t in tokens) items.Add(new TokenItem(t));

            var alternatives = new List<RewriteMatch>();
            string? primaryRuleId = null;

            // Scheduling multi-phase :
            // - Phase 0 (P<50) : fusions token-level. Tourne en premier
            //   pour produire des Items composites (= `+\infty` → 1 Item)
            //   avant que les opérateurs binaires aient leur chance.
            // - Phase 1 (P<100) : primitives. paren-group, add, sub,
            //   function-call, etc.
            // - Phase 2 (toutes) : anchors. Matchent sur Items déjà
            //   préparés en phase 1.
            RunPhase(items, _rules.Where(r => r.Priority < Phase0Separator),
                alternatives, ref primaryRuleId);
            RunPhase(items, _rules.Where(r => r.Priority < PhaseSeparator),
                alternatives, ref primaryRuleId);
            RunPhase(items, _rules,
                alternatives, ref primaryRuleId);

            // Emit final : concat des Latex restants.
            var sb = new StringBuilder();
            foreach (var item in items)
                sb.Append(item.Latex);

            return new RewriteResult(
                topLatex: sb.ToString(),
                items: items,
                alternatives: alternatives,
                ruleId: primaryRuleId ?? "");
        }

        /// <summary>Loop fixed-point d'une phase avec les règles données.
        /// Modifie <paramref name="items"/> in-place.</summary>
        private static void RunPhase(List<Item> items, IEnumerable<RewriteRule> phaseRules,
            List<RewriteMatch> alternatives, ref string? primaryRuleId)
        {
            var phaseRulesList = phaseRules as IList<RewriteRule> ?? phaseRules.ToArray();
            if (phaseRulesList.Count == 0) return;

            int safety = 64;
            while (safety-- > 0)
            {
                var matches = new List<RewriteMatch>();
                for (int p = 0; p < items.Count; p++)
                {
                    foreach (var rule in phaseRulesList)
                    {
                        var m = RewriteMatcher.TryMatch(rule, items, p);
                        if (m != null) matches.Add(m);
                    }
                }
                if (matches.Count == 0) break;

                // Leftmost-longest avec Priority desc en tie.
                matches.Sort((a, b) =>
                {
                    int dStart = a.Start.CompareTo(b.Start);
                    if (dStart != 0) return dStart;
                    int dSpan = b.Span.CompareTo(a.Span);
                    if (dSpan != 0) return dSpan;
                    return b.Rule.Priority.CompareTo(a.Rule.Priority);
                });
                var best = matches[0];

                for (int k = 1; k < matches.Count; k++)
                {
                    if (matches[k].Start == best.Start)
                        alternatives.Add(matches[k]);
                }

                var latex = RewriteMatcher.ApplyTemplate(
                    best.Rule.EmitTemplate, best.Slots, best.Lists, best.Blocks);
                var sourceText = ConcatSource(items, best.Start, best.End);
                var produced = new RewriteItem(best.Rule.Id, best.Rule.Produces, sourceText, latex);
                items.RemoveRange(best.Start, best.End - best.Start);
                items.Insert(best.Start, produced);
                primaryRuleId ??= best.Rule.Id;
            }
        }

        private static string ConcatSource(IReadOnlyList<Item> items, int start, int endExcl)
        {
            var sb = new StringBuilder();
            for (int i = start; i < endExcl; i++) sb.Append(items[i].SourceText);
            return sb.ToString();
        }
    }

    /// <summary>Résultat d'une résolution rewriting (Phase A).</summary>
    public sealed class RewriteResult
    {
        public string TopLatex { get; }
        public IReadOnlyList<Item> Items { get; }
        public IReadOnlyList<RewriteMatch> Alternatives { get; }
        public string RuleId { get; }

        public RewriteResult(string topLatex, IReadOnlyList<Item> items,
            IReadOnlyList<RewriteMatch> alternatives, string ruleId)
        {
            TopLatex = topLatex ?? string.Empty;
            Items = items;
            Alternatives = alternatives;
            RuleId = ruleId ?? "";
        }

        public static RewriteResult Empty { get; } = new RewriteResult(
            "", new List<Item>(), new List<RewriteMatch>(), "");
    }
}
