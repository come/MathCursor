using System;

namespace MathCursor.Host.Pipeline.Stages
{
    /// <summary>
    /// Stage : persiste la source brute + sidecar JSON dans le
    /// <c>CustomXMLPart</c> du document via <c>IEquationStore</c>. Met à
    /// jour les bookmarks Word (<c>mcEq_*</c>) et le mapping mémoire
    /// <c>_sidecarsByHandle</c>. Pour les handles absorbés au merge,
    /// supprime également les entrées store + bookmarks correspondants.
    /// <para>
    /// Wrapper délégant en Phase 2.5 — logique reste dans
    /// <c>SuggestionService</c>. Cf. ADR
    /// <c>2026-05-06-Meta-l4-pipeline-and-session</c>.
    /// </para>
    /// </summary>
    internal sealed class StoreStage : ICommitStage
    {
        private readonly Func<CommitContext, CommitContext> _impl;

        public StoreStage(Func<CommitContext, CommitContext> impl)
        {
            _impl = impl ?? throw new ArgumentNullException(nameof(impl));
        }

        public string Name => "Store";

        public CommitContext Apply(CommitContext ctx) => _impl(ctx);
    }
}
