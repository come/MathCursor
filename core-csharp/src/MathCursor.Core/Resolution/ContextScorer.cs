using System.Collections.Generic;

namespace MathCursor.Core.Resolution
{
    /// <summary>
    /// Aggrège les <see cref="IContextSignal"/>s en <see cref="ScoringHints"/>.
    /// Pondère chaque contribution par le poids de son <see cref="ZoomLevel"/>.
    ///
    /// <para>Cf. brief <c>2026-05-07-global-context-multi-zoom-ranking</c> pour
    /// le modèle de poids et la justification des valeurs initiales.</para>
    ///
    /// <para>V1 (commit framework) : somme pondérée pure. Pas de cap, pas de
    /// décay. À ajouter quand on aura assez de données empiriques pour
    /// calibrer.</para>
    /// </summary>
    public sealed class ContextScorer
    {
        /// <summary>Poids initiaux par niveau (à calibrer).
        /// Valeurs issues du brief 2026-05-07.</summary>
        public static IReadOnlyDictionary<ZoomLevel, double> DefaultLevelWeights { get; }
            = new Dictionary<ZoomLevel, double>
            {
                { ZoomLevel.L0_Token,        1.0 },
                { ZoomLevel.L1_Block,        0.9 },
                { ZoomLevel.L2_Paragraph,    0.7 },
                { ZoomLevel.L3_NeighborParas, 0.4 },
                { ZoomLevel.L4_Section,      0.3 },
                { ZoomLevel.L5_Document,     0.15 },
            };

        private readonly IReadOnlyList<IContextSignal> _signals;
        private readonly IReadOnlyDictionary<ZoomLevel, double> _levelWeights;

        public ContextScorer(
            IReadOnlyList<IContextSignal>? signals,
            IReadOnlyDictionary<ZoomLevel, double>? levelWeights = null)
        {
            _signals = signals ?? new List<IContextSignal>();
            _levelWeights = levelWeights ?? DefaultLevelWeights;
        }

        /// <summary>Calcule les scores agrégés à partir du snapshot.</summary>
        public ScoringHints Aggregate(ContextSnapshot? ctx)
        {
            if (ctx == null || _signals.Count == 0) return ScoringHints.Empty;

            var altScores = new Dictionary<string, double>();
            var trace = new List<string>();

            foreach (var sig in _signals)
            {
                if (sig == null) continue;
                var deltas = sig.Score(ctx);
                if (deltas == null || deltas.Count == 0) continue;

                if (!_levelWeights.TryGetValue(sig.Level, out double w) || w == 0.0)
                    w = 1.0; // niveau non configuré : poids 1.0 par défaut

                foreach (var kv in deltas)
                {
                    if (string.IsNullOrEmpty(kv.Key)) continue;
                    double weighted = kv.Value * w;
                    if (altScores.TryGetValue(kv.Key, out double current))
                        altScores[kv.Key] = current + weighted;
                    else
                        altScores[kv.Key] = weighted;
                    trace.Add($"{sig.Name}@{sig.Level}: {kv.Key} += {weighted:F3}");
                }
            }

            return new ScoringHints(altScores, trace);
        }
    }
}
