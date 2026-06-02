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
        /// <see cref="EngineResult"/> (= contrat public stable pour l'adapter).
        ///
        /// <para><b>Dernière étape latex</b> : les lectures alternatives arrivent
        /// du moteur comme des structures (<c>IReadOnlyList&lt;Item&gt;</c>). On
        /// les sérialise ICI, on dédoublonne et on retire celle égale au top.
        /// Le moteur, lui, ne manipule jamais de latex pour les collisions.</para></summary>
        private static EngineResult Adapt(RewriteResult r)
        {
            var seen = new HashSet<string> { r.TopLatex };
            var collisions = new List<EngineCandidate>();
            foreach (var reading in r.Alternatives)
            {
                var latex = SerializeReading(reading);
                if (string.IsNullOrEmpty(latex) || !seen.Add(latex)) continue;
                collisions.Add(new EngineCandidate(
                    latex: latex,
                    description: latex,
                    ruleId: "",
                    score: 0));
            }
            return new EngineResult(
                topLatex: r.TopLatex,
                isComplete: !r.TopLatex.Contains(@"\square"),
                collisions: collisions,
                ruleId: r.RuleId);
        }

        private static string SerializeReading(IReadOnlyList<Item> items)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var it in items) sb.Append(it.Latex);
            return sb.ToString();
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