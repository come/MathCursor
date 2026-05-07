using System.Collections.Generic;

namespace MathCursor.Core.Resolution
{
    /// <summary>
    /// Une source de signal contextuel qui contribue au scoring d'alternatives.
    /// Chaque signal opère à un <see cref="ZoomLevel"/> donné et retourne des
    /// deltas additifs par alternative. Le <see cref="ContextScorer"/> agrège
    /// tous les signaux pondérés par leur niveau.
    ///
    /// Cf. brief <c>2026-05-07-global-context-multi-zoom-ranking</c>.
    /// </summary>
    public interface IContextSignal
    {
        /// <summary>Identifiant lisible (logging, debug, traces).</summary>
        string Name { get; }

        /// <summary>Niveau de zoom auquel ce signal opère. Détermine son
        /// poids relatif dans l'agrégation.</summary>
        ZoomLevel Level { get; }

        /// <summary>
        /// Retourne des deltas additifs par alternative.
        /// Format des clés : <c>"{ruleId}:{altIdx}"</c> (ex:
        /// <c>"two-uppercase:0"</c> pour <c>vec</c>).
        /// Valeurs : delta de score brut (positif = muscle, négatif = démuscle).
        /// Le scorer multiplie par le poids du niveau.
        /// </summary>
        IReadOnlyDictionary<string, double> Score(ContextSnapshot ctx);
    }
}
