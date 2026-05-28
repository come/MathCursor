using System;
using System.Collections.Generic;
using System.Linq;
using MathCursor.Engine.Rewriting;
using MathCursor.Engine.Rewriting.Yaml;
using MathCursor.Engine.Rules;
using MathCursor.Engine.Vocabulary;

namespace MathCursor.Engine
{
    /// <summary>
    /// Implémentation par défaut de <see cref="IEngineFrontend"/>.
    ///
    /// <para>Utilise exclusivement le <see cref="RewriteEngine"/> depuis
    /// Phase D-6 (2026-05-26) — bascule franche.</para>
    /// </summary>
    public sealed class MathEngine : IEngineFrontend
    {
        private readonly LocaleVocabulary _vocab;
        private readonly RewriteEngine _rewriteEngine;

        /// <summary>Vocab locale chargé (= stopwords, span_delimiters, etc.).
        /// Exposé pour que l'adapter VSTO accède aux mêmes listes que le moteur.</summary>
        public LocaleVocabulary Vocab => _vocab;

        public MathEngine(LocaleVocabulary vocab, RewriteEngine rewriteEngine)
        {
            _vocab = vocab ?? throw new ArgumentNullException(nameof(vocab));
            _rewriteEngine = rewriteEngine ?? throw new ArgumentNullException(nameof(rewriteEngine));
        }

        public EngineResult Resolve(string source)
        {
            if (string.IsNullOrEmpty(source)) return EngineResult.Empty;
            var result = _rewriteEngine.Resolve(source);
            return AdaptRewriteResult(result);
        }

        /// <summary>Adapte un <see cref="RewriteResult"/> en <see cref="EngineResult"/>
        /// (= Phase D-6 bascule). Mapping : TopLatex direct, IsComplete = !Contains(\square),
        /// Alternatives → Collisions via emit du template.</summary>
        private EngineResult AdaptRewriteResult(RewriteResult r)
        {
            var collisions = new List<EngineCandidate>();
            foreach (var alt in r.Alternatives)
            {
                var altLatex = RewriteMatcher.ApplyTemplate(
                    alt.Rule.EmitTemplate, alt.Slots, alt.Lists, alt.Blocks);
                collisions.Add(new EngineCandidate(
                    latex: altLatex,
                    description: alt.Rule.Id,
                    ruleId: alt.Rule.Id,
                    score: alt.Span * 10));
            }
            return new EngineResult(
                topLatex: r.TopLatex,
                isComplete: !r.TopLatex.Contains(@"\square"),
                collisions: collisions,
                ruleId: r.RuleId);
        }

        // ─── Factory ──────────────────────────────────────────────────

        public static MathEngine BuildDefault(string localeCode = "fr")
        {
            return BuildDefaultWithRewriteEngine(localeCode);
        }

        /// <summary>Construit un MathEngine qui délègue au RewriteEngine
        /// (= moteur principal depuis Phase D-6).</summary>
        public static MathEngine BuildDefaultWithRewriteEngine(string localeCode = "fr")
        {
            var vocab = LocaleVocabulary.LoadEmbedded(localeCode);
            var concepts = RuleLoader.LoadAllEmbedded();
            var ruleSpecs = new List<RuleSpec>();
            foreach (var c in concepts)
                ruleSpecs.AddRange(c.Rules);

            var rewriteRules = new List<RewriteRule>();
            rewriteRules.AddRange(PrimitiveRules.All);
            rewriteRules.AddRange(RewriteRuleLoader.LoadAllEmbedded(vocab));
            var rewriteEngine = new RewriteEngine(vocab, rewriteRules);

            return new MathEngine(vocab, rewriteEngine);
        }
    }
}