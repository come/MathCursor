using System;
using MathCursor.Core.Resolution;

namespace MathCursor.Host
{
    /// <summary>
    /// Event args pour <see cref="SuggestionService.ContextResolved"/>.
    /// Contient le rawSource, le snapshot du contexte de session et les
    /// hints scorés produits par <see cref="ContextScorer.Aggregate"/>.
    ///
    /// <para>Consommé par le pane debug <c>ContextInspectorPane</c> pour
    /// afficher le scoring contextuel en temps réel.</para>
    /// </summary>
    public sealed class ContextResolveEventArgs : EventArgs
    {
        public string RawSource { get; }
        public ContextSnapshot Snapshot { get; }
        public ScoringHints Hints { get; }

        public ContextResolveEventArgs(
            string rawSource,
            ContextSnapshot snapshot,
            ScoringHints hints)
        {
            RawSource = rawSource ?? string.Empty;
            Snapshot = snapshot ?? new ContextSnapshot(rawSource, ResolutionSidecar.Empty);
            Hints = hints ?? ScoringHints.Empty;
        }
    }
}
