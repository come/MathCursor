using System;
using MathCursor.Host.Merging;

namespace MathCursor.Host.Pipeline.Stages
{
    /// <summary>
    /// Stage : applique le <see cref="MergerPipeline"/> existant sur le ctx
    /// d'entrée. Si un merger absorbe des voisins, met à jour le ctx avec
    /// les nouvelles bornes, la mergedSource, le sidecar fusionné, les
    /// handles à supprimer, et les méta cross-paragraphe (marker, flag).
    /// Sinon, retourne le ctx tel quel.
    /// <para>
    /// 1er stage du <see cref="CommitPipeline"/>. Cf. ADR
    /// <c>2026-05-06-Meta-l4-pipeline-and-session</c>.
    /// </para>
    /// </summary>
    internal sealed class MergerStage : ICommitStage
    {
        private readonly MergerPipeline _mergers;
        private readonly Func<string, string> _extractMarker;

        /// <param name="mergers">Pipeline de mergers (intra/reverted/cases/marker).</param>
        /// <param name="extractMarker">Fonction qui extrait le marker dominant
        /// d'une mergedSource cross-paragraphe (= ExtractMarkerFromMergedSource
        /// statique de SuggestionService — sera extraite proprement en Phase 4).</param>
        public MergerStage(MergerPipeline mergers, Func<string, string> extractMarker)
        {
            _mergers = mergers ?? throw new ArgumentNullException(nameof(mergers));
            _extractMarker = extractMarker ?? (_ => null);
        }

        public string Name => "Merger";

        public CommitContext Apply(CommitContext ctx)
        {
            if (ctx == null) return null;
            // Mode édition : pas de fusion (le revert remplace l'OMath en cours,
            // pas de merge avec voisins). Préserve `if (editing == null)` du
            // SuggestionService.
            if (ctx.EditingHandle != null) return ctx;

            var merged = _mergers.Run(ctx.AbsStart, ctx.AbsEnd, ctx.Source);
            if (merged == null) return ctx; // pas de voisin à absorber

            bool isCross = merged.MergedSource != null
                && merged.MergedSource.IndexOf('\n') >= 0;
            string marker = isCross ? _extractMarker(merged.MergedSource) : null;

            return ctx.WithMergeResult(
                absStart: merged.AbsStart,
                absEnd: merged.AbsEnd,
                mergedSource: merged.MergedSource,
                mergedSidecar: merged.MergedSidecar,
                removedHandles: (System.Collections.Generic.IReadOnlyList<string>)merged.RemovedHandles
                                  ?? System.Array.Empty<string>(),
                wasCrossParagraphMerge: isCross,
                crossMergeMarker: marker);
        }
    }
}
