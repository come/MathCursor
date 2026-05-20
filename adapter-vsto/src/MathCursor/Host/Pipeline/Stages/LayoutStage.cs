using System;

namespace MathCursor.Host.Pipeline.Stages
{
    /// <summary>
    /// Stage : finalise le layout post-insertion — alignement OMath
    /// (gauche/centre selon paragraphe parent), strip ¶ vide résiduel
    /// avant l'OMath, append ¶ vide après si nécessaire pour le caret,
    /// activation list-mode si <c>WasCrossParagraphMerge</c>.
    /// <para>
    /// Wrapper délégant en Phase 2.5 — logique reste dans
    /// <c>SuggestionService.FinalizeCrossMergeLayout</c>. Cf. ADR
    /// <c>2026-05-06-Meta-l4-pipeline-and-session</c>.
    /// </para>
    /// </summary>
    internal sealed class LayoutStage : ICommitStage
    {
        private readonly Func<CommitContext, CommitContext> _impl;

        public LayoutStage(Func<CommitContext, CommitContext> impl)
        {
            _impl = impl ?? throw new ArgumentNullException(nameof(impl));
        }

        public string Name => "Layout";

        public CommitContext Apply(CommitContext ctx) => _impl(ctx);
    }
}
