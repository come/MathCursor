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
        // Poids du pin le plus récent (= le dernier ajouté). Les pins plus
        // anciens ont un poids moindre via décay exponentiel — voir Lambda.
        private const double PinWeight = 1.0;

        // Décay exponentiel : weight = PinWeight * exp(-distance * Lambda),
        // où distance = (n - 1 - i) (= distance au plus récent).
        // Lambda = 0.5 → demi-vie ~1.4 pins. Le plus récent = 1.0,
        // l'avant-dernier ≈ 0.61, l'antépénultième ≈ 0.37.
        //
        // Effet « muscler le plus proche » (demande user 2026-05-07) :
        // si AB→vec (ancien) et CD→paren (récent), paren gagne sur la
        // prochaine ambig (= 1.0 vs 0.61). Si vec×3 anciens vs paren×1
        // récent, vec garde l'avantage de peu (cumul historique).
        private const double Lambda = 0.5;

        public string Name => "ParagraphResolutions";

        public ZoomLevel Level => ZoomLevel.L2_Paragraph;

        public IReadOnlyDictionary<string, double> Score(ContextSnapshot? ctx)
        {
            var deltas = new Dictionary<string, double>();
            if (ctx?.RecentParagraphPins == null) return deltas;

            int n = ctx.RecentParagraphPins.Count;
            for (int i = 0; i < n; i++)
            {
                var pin = ctx.RecentParagraphPins[i];
                if (pin == null || string.IsNullOrEmpty(pin.Rule)) continue;
                if (pin.AltIdx < 0) continue;

                // Distance au plus récent : i = n-1 → distance 0 (max).
                // Décay exponentiel donne du poids au récent.
                int distance = n - 1 - i;
                double weight = PinWeight * System.Math.Exp(-distance * Lambda);

                string key = ScoringHints.Key(pin.Rule, pin.AltIdx);
                if (deltas.TryGetValue(key, out double current))
                    deltas[key] = current + weight;
                else
                    deltas[key] = weight;
            }
            return deltas;
        }
    }
}
