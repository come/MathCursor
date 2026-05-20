using System.Collections.Generic;

namespace MathCursor.Core.Resolution
{
    /// <summary>
    /// Snapshot immutable du contexte au moment d'une requête de résolution.
    /// Construit par <see cref="GlobalContext.Snapshot"/> avant chaque appel
    /// au scorer. Toutes les sources de signal lisent leurs données ici, pas
    /// d'état mutable partagé pendant le scoring.
    ///
    /// Cf. brief <c>2026-05-07-global-context-multi-zoom-ranking</c>.
    /// </summary>
    public sealed class ContextSnapshot
    {
        /// <summary>Source brute de la zone math en cours de résolution.</summary>
        public string RawSource { get; }

        /// <summary>Sidecar de résolutions de la zone (L0/L1).
        /// Jamais null — <see cref="ResolutionSidecar.Empty"/> si rien.</summary>
        public ResolutionSidecar Sidecar { get; }

        /// <summary>Pins explicites accumulés dans le ¶ courant lors de la
        /// session, hors de la zone math actuelle. Alimente <c>L2</c>.
        /// Ordre = ordre chronologique (plus récent en fin).</summary>
        public IReadOnlyList<SpanPin> RecentParagraphPins { get; }

        public ContextSnapshot(
            string? rawSource,
            ResolutionSidecar? sidecar,
            IReadOnlyList<SpanPin>? recentParagraphPins = null)
        {
            RawSource = rawSource ?? string.Empty;
            Sidecar = sidecar ?? ResolutionSidecar.Empty;
            RecentParagraphPins = recentParagraphPins ?? new List<SpanPin>();
        }
    }
}
