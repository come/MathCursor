using System;

namespace MathCursor.Host.Pipeline.Stages
{
    /// <summary>
    /// Stage placeholder pour la conversion LaTeX → OMML. En Phase 1 VSTO
    /// le rendu est délégué à Word (BuildUp via InsertOMathAt côté
    /// InserterStage). Ce stage reste un slot du pipeline (cohérent avec
    /// l'ADR) en cas où on voudrait insérer une étape de rendu côté core
    /// plus tard (ex. validation pre-Word, normalisation LaTeX, etc.).
    /// <para>
    /// Implémentation par défaut : identity (passe-plat). Cf. ADR
    /// <c>2026-05-06-Meta-l4-pipeline-and-session</c>.
    /// </para>
    /// </summary>
    internal sealed class RendererStage : ICommitStage
    {
        private readonly Func<CommitContext, CommitContext> _impl;

        /// <summary>Constructeur identity (no-op).</summary>
        public RendererStage() : this(ctx => ctx) { }

        /// <param name="impl">Délégué optionnel pour customiser le rendu.</param>
        public RendererStage(Func<CommitContext, CommitContext> impl)
        {
            _impl = impl ?? (ctx => ctx);
        }

        public string Name => "Renderer";

        public CommitContext Apply(CommitContext ctx) => _impl(ctx);
    }
}
