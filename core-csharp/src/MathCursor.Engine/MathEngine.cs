using System;
using System.Collections.Generic;
using MathCursor.Engine.Rewriting;
using MathCursor.Engine.Vocabulary;

namespace MathCursor.Engine
{
    /// <summary>
    /// Implémentation par défaut de <see cref="IEngineFrontend"/>.
    ///
    /// <para>Délègue au <see cref="RewriteEngine"/> V2 (2026-05-29). Cf. ADR
    /// 2026-05-28-rewriting-engine-v2.</para>
    /// </summary>
    public sealed class MathEngine : IEngineFrontend
    {
        private readonly LocaleVocabulary _vocab;
        private readonly RewriteEngine _engine;

        /// <summary>Vocab locale chargé. Exposé pour que l'adapter VSTO
        /// accède aux mêmes listes que le moteur.</summary>
        public LocaleVocabulary Vocab => _vocab;

        public MathEngine(LocaleVocabulary vocab, RewriteEngine engine)
        {
            _vocab = vocab ?? throw new ArgumentNullException(nameof(vocab));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        public EngineResult Resolve(string source)
        {
            if (string.IsNullOrEmpty(source)) return EngineResult.Empty;
            return Adapt(_engine.Resolve(source));
        }

        /// <summary>Adapte un <see cref="RewriteResult"/> en
        /// <see cref="EngineResult"/> (= contrat public stable pour l'adapter).</summary>
        private static EngineResult Adapt(RewriteResult r)
        {
            var collisions = new List<EngineCandidate>();
            foreach (var alt in r.Alternatives)
            {
                var latex = RewriteMatcher.ApplyTemplate(alt.Rule.EmitTemplate, alt.Slots);
                collisions.Add(new EngineCandidate(
                    latex: latex,
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
            var vocab = LocaleVocabulary.LoadEmbedded(localeCode);
            var rules = RuleSetLoader.LoadAllEmbedded(vocab);
            return new MathEngine(vocab, new RewriteEngine(vocab, rules));
        }
    }
}