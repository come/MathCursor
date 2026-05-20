using System;
using MathCursor.HostContract;

namespace MathCursor.Host.Pipeline.Stages
{
    /// <summary>
    /// Stage : mémorise le sidecar (pins/votes) en mémoire pour le handle
    /// courant. La SOURCE et le LATEX vivent maintenant dans le
    /// <c>cc.Tag</c> JSON de l'OMath (cf. <see cref="MathCursor.Host.CCMeta.MCMeta"/>),
    /// posé par <c>InsertOMathAt</c>. Plus de <c>CustomXMLPart</c>, plus de
    /// bookmark — ce stage devient un simple stash in-memory.
    ///
    /// <para>Phase B brief 2026-05-18 (probe minimale + backlink natif).</para>
    /// </summary>
    internal sealed class StoreStage : ICommitStage
    {
        private readonly EquationHandleRegistry _registry;
        private readonly Action<string> _log;

        public StoreStage(EquationHandleRegistry registry, Action<string> log = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _log = log ?? (_ => { });
        }

        public string Name => "Store";

        public CommitContext Apply(CommitContext ctx)
        {
            if (ctx == null) return null;

            // Mode édition : sidecar déjà connu via _registry, juste un re-stash
            // pour s'assurer qu'il est à jour avec les pins post-popup.
            if (ctx.EditingHandle != null)
            {
                _log($"edit commit handle={ctx.EditingHandle.Id} latex=\"{ctx.Latex}\"");
                _registry.Stash(ctx.EditingHandle.Id);
                return ctx;
            }

            // Nouveau commit : handle généré par InsertOMathAt et posé sur
            // ctx.NewHandle. On stash le sidecar in-memory.
            if (ctx.NewHandle != null)
            {
                _registry.Stash(ctx.NewHandle.Id, ctx.Sidecar);
                _log($"insert commit handle={ctx.NewHandle.Id} range=[{ctx.AbsStart},{ctx.AbsEnd}] latex=\"{ctx.Latex}\" source=\"{ctx.Source}\"");
            }

            return ctx;
        }
    }
}
