using System;
using MathCursor.Core;
using MathCursor.Core.Resolution;

namespace MathCursor.Host
{
    /// <summary>
    /// Event args pour <see cref="SuggestionService.ContextResolved"/>.
    /// Contient le rawSource, le snapshot du contexte de session, les hints
    /// scorés et le <see cref="ResolvedZone"/> final (post-splice/filtrage).
    ///
    /// <para>Consommé par le pane debug <c>ContextInspectorPane</c> pour
    /// afficher en temps réel : le scoring contextuel ET ce qui en résulte
    /// vraiment (ambig restantes, top LaTeX). Permet de distinguer un
    /// score inerte (rule jamais matchée sur la zone) d'un score effectif
    /// (rule matchée → splice appliqué).</para>
    /// </summary>
    public sealed class ContextResolveEventArgs : EventArgs
    {
        public string RawSource { get; }
        public ContextSnapshot Snapshot { get; }
        public ScoringHints Hints { get; }

        /// <summary>Résultat de la résolution (top LaTeX + ambig restantes
        /// après splice contextuel). Peut être null si la zone n'a pas pu
        /// être résolue.</summary>
        public ResolvedZone Resolved { get; }

        public ContextResolveEventArgs(
            string rawSource,
            ContextSnapshot snapshot,
            ScoringHints hints,
            ResolvedZone resolved)
        {
            RawSource = rawSource ?? string.Empty;
            Snapshot = snapshot ?? new ContextSnapshot(rawSource, ResolutionSidecar.Empty);
            Hints = hints ?? ScoringHints.Empty;
            Resolved = resolved;
        }
    }
}
