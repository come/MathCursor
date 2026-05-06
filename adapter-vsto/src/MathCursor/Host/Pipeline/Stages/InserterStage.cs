using System;

namespace MathCursor.Host.Pipeline.Stages
{
    /// <summary>
    /// Stage : insère le LaTeX dans Word via OMath (build XML isolé +
    /// transplant ou fallback API <c>OMaths.Add+BuildUp</c>). Met à jour
    /// les bornes du ctx avec celles de l'OMath inséré + le NewHandle si
    /// nouveau commit (vs. édition d'un OMath existant).
    /// <para>
    /// Wrapper délégant en Phase 2.5 — la logique métier (~750 LoC) reste
    /// dans <c>SuggestionService</c> pour ce sprint. Phase 3 fera la
    /// vraie extraction. Cf. ADR <c>2026-05-06-Meta-l4-pipeline-and-session</c>.
    /// </para>
    /// <para>
    /// Si l'insertion échoue (rollback Word), retourner
    /// <c>ctx.WithAbort()</c> — le pipeline saute les stages suivants.
    /// </para>
    /// </summary>
    internal sealed class InserterStage : ICommitStage
    {
        private readonly Func<CommitContext, CommitContext> _impl;

        public InserterStage(Func<CommitContext, CommitContext> impl)
        {
            _impl = impl ?? throw new ArgumentNullException(nameof(impl));
        }

        public string Name => "Inserter";

        public CommitContext Apply(CommitContext ctx) => _impl(ctx);
    }
}
