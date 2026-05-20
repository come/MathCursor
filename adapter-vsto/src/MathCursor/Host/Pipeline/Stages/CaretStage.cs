using System;

namespace MathCursor.Host.Pipeline.Stages
{
    /// <summary>
    /// Stage : positionne le caret après l'OMath inséré. Si list-mode
    /// activé (cross-merge), positionne le caret en début de nouvelle
    /// ligne avec marker pré-injecté. Sinon, juste après la fin de l'OMath.
    /// <para>
    /// Wrapper délégant en Phase 2.5 — logique reste dans
    /// <c>SuggestionService</c> (<c>SetCaretAtPosition</c>,
    /// <c>NudgeCursorOutOfMath</c>). Cf. ADR
    /// <c>2026-05-06-Meta-l4-pipeline-and-session</c>.
    /// </para>
    /// </summary>
    internal sealed class CaretStage : ICommitStage
    {
        private readonly Func<CommitContext, CommitContext> _impl;

        public CaretStage(Func<CommitContext, CommitContext> impl)
        {
            _impl = impl ?? throw new ArgumentNullException(nameof(impl));
        }

        public string Name => "Caret";

        public CommitContext Apply(CommitContext ctx) => _impl(ctx);
    }
}
