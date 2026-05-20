using System;
using MathCursor.Core;

namespace MathCursor.Host.Pipeline.Stages
{
    /// <summary>
    /// Stage : applique <see cref="ZoneResolver.Resolve(string,
    /// MathCursor.Core.Resolution.ResolutionSidecar)"/> sur la <c>Source</c>
    /// du ctx (mergedSource si MergerStage a absorbé des voisins) avec le
    /// <c>Sidecar</c> fusionné. Renseigne <c>Latex</c> avec le top-1 résolu
    /// (vec/paren/etc. appliqués).
    /// <para>
    /// 2e stage du <see cref="CommitPipeline"/>. Cf. ADR
    /// <c>2026-05-06-Meta-l4-pipeline-and-session</c>.
    /// </para>
    /// </summary>
    internal sealed class ResolverStage : ICommitStage
    {
        private readonly ZoneResolver _resolver;

        public ResolverStage(ZoneResolver resolver)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        public string Name => "Resolver";

        public CommitContext Apply(CommitContext ctx)
        {
            if (ctx == null) return null;
            if (string.IsNullOrEmpty(ctx.Source)) return ctx;
            // Re-Resolve si MergerStage a transformé le ctx (cross-merge
            // texte-only avec Sidecar Empty mais Source mergée — bug canary 3)
            // OU si le ctx a un sidecar non-empty (pins/votes à appliquer).
            // Skip uniquement quand pas de merge ET sidecar empty (= rien à
            // faire, préserve le LaTeX popup avec ses substitutions in-line).
            if (!ctx.WasMerged && (ctx.Sidecar == null || ctx.Sidecar.IsEmpty))
                return ctx;

            var resolved = _resolver.Resolve(ctx.Source, ctx.Sidecar);
            if (resolved == null || string.IsNullOrEmpty(resolved.TopLatex))
                return ctx;

            return ctx.WithLatex(resolved.TopLatex);
        }
    }
}
