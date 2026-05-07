using System.Collections.Generic;

namespace MathCursor.Core.Resolution.Signals
{
    /// <summary>
    /// Signal contextuel <c>L2_Paragraph</c> qui consomme les pins
    /// accumulés dans <see cref="ContextSnapshot.RecentParagraphPins"/>.
    ///
    /// <para>Cas typique : l'utilisateur résout <c>AB</c> en <c>vec</c> sur la
    /// ligne 1 d'un système. Au commit popup, le pin est poussé dans le
    /// <see cref="GlobalContext"/> (couché ¶ courant). Ligne 2 du système :
    /// <c>AD</c> NER détecte la même ambiguïté <c>two-uppercase</c>, ce signal
    /// muscle l'alt <c>vec</c> via le contexte ¶ → résolution auto sans
    /// re-popup.</para>
    ///
    /// <para>Cf. brief <c>2026-05-07-global-context-multi-zoom-ranking</c>,
    /// cas d'étude #1 (AB/AD système 2 lignes).</para>
    /// </summary>
    public sealed class ParagraphResolutionsSignal : IContextSignal
    {
        // Score par pin du ¶. Plus discret qu'un vote sidecar (le ¶ peut
        // contenir des résolutions non liées à la zone courante, on
        // contribue mais on ne domine pas).
        private const double PinWeight = 1.0;

        public string Name => "ParagraphResolutions";

        public ZoomLevel Level => ZoomLevel.L2_Paragraph;

        public IReadOnlyDictionary<string, double> Score(ContextSnapshot? ctx)
        {
            var deltas = new Dictionary<string, double>();
            if (ctx?.RecentParagraphPins == null) return deltas;

            // Aggrège par (rule, alt) en cumulant les pins du ¶.
            // Les pins du même (rule, alt) s'additionnent (3 résolutions vec
            // dans le ¶ → boost plus fort qu'une seule).
            foreach (var pin in ctx.RecentParagraphPins)
            {
                if (pin == null || string.IsNullOrEmpty(pin.Rule)) continue;
                if (pin.AltIdx < 0) continue;
                string key = ScoringHints.Key(pin.Rule, pin.AltIdx);
                if (deltas.TryGetValue(key, out double current))
                    deltas[key] = current + PinWeight;
                else
                    deltas[key] = PinWeight;
            }
            return deltas;
        }
    }
}
