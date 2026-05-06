using System;

namespace MathCursor.Host.Pipeline.Stages
{
    /// <summary>
    /// Stage : prend un snapshot du commit (source brute + LaTeX final) pour
    /// la fenêtre « Signaler une erreur ». Side-effect global sur
    /// <c>_lastAction</c> de SuggestionService — placé entre Resolver et
    /// Inserter pour capturer le LaTeX post-merge mais pre-insert (préserve
    /// le comportement actuel : si l'insertion fail, le report a quand même
    /// le bon LaTeX).
    /// <para>
    /// Stage techniquement « accessoire » : pourrait disparaître si on
    /// extrait le mécanisme de reporting comme un EventListener du pipeline
    /// plus tard. Pour Phase 3a on le garde explicite.
    /// </para>
    /// </summary>
    internal sealed class SnapshotStage : ICommitStage
    {
        private readonly Func<CommitContext, CommitContext> _impl;

        public SnapshotStage(Func<CommitContext, CommitContext> impl)
        {
            _impl = impl ?? throw new ArgumentNullException(nameof(impl));
        }

        public string Name => "Snapshot";

        public CommitContext Apply(CommitContext ctx) => _impl(ctx);
    }
}
