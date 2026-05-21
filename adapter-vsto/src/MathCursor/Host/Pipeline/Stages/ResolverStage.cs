using System;
using MathCursor.Core;
using MathCursor.Core.Resolution;

namespace MathCursor.Host.Pipeline.Stages
{
    /// <summary>
    /// Stage : applique <see cref="ZoneResolver.Resolve(string, GlobalContext, ResolutionSidecar)"/>
    /// sur la <c>Source</c> du ctx (mergedSource si MergerStage a absorbé des
    /// voisins) avec le <c>Sidecar</c> fusionné + GlobalContext de session.
    /// Renseigne <c>Latex</c> avec le top-1 résolu (vec/paren/etc. appliqués).
    /// <para>
    /// 2e stage du <see cref="CommitPipeline"/>. Cf. ADR
    /// <c>2026-05-06-Meta-l4-pipeline-and-session</c>.
    /// </para>
    /// </summary>
    internal sealed class ResolverStage : ICommitStage
    {
        private readonly ZoneResolver _resolver;
        private readonly Func<GlobalContext> _getGlobalCtx;

        public ResolverStage(ZoneResolver resolver, Func<GlobalContext> getGlobalCtx = null)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _getGlobalCtx = getGlobalCtx ?? (() => null);
        }

        public string Name => "Resolver";

        public CommitContext Apply(CommitContext ctx)
        {
            if (ctx == null) return null;
            if (string.IsNullOrEmpty(ctx.Source)) return ctx;
            if (!ctx.WasMerged && (ctx.Sidecar == null || ctx.Sidecar.IsEmpty))
                return ctx;

            var resolved = _resolver.Resolve(ctx.Source, _getGlobalCtx(), ctx.Sidecar);
            if (resolved == null || string.IsNullOrEmpty(resolved.TopLatex))
                return ctx;

            return ctx.WithLatex(resolved.TopLatex);
        }
    }
}
