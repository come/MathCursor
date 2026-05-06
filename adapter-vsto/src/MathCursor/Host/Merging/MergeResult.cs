using System.Collections.Generic;
using MathCursor.Core.Resolution;

namespace MathCursor.Host.Merging
{
    /// <summary>
    /// Résultat d'une tentative de merge par un <see cref="IZoneMerger"/> :
    /// nouvelles positions absolues englobant les OMaths/paragraphes
    /// fusionnés, source mergée, handles à supprimer du store, sidecar
    /// fusionné. Cf. ADR <c>2026-05-06-Meta-zone-merger-pipeline</c>.
    /// </summary>
    internal sealed class MergeResult
    {
        public int AbsStart { get; set; }
        public int AbsEnd { get; set; }
        public string MergedSource { get; set; }
        public List<string> RemovedHandles { get; set; }

        /// <summary>
        /// Sidecar de résolutions fusionné — offsets recalibrés sur
        /// <see cref="MergedSource"/> + votes sommés. <see cref="ResolutionSidecar.Empty"/>
        /// si aucun OMath absorbé n'avait de sidecar (cas dégradé).
        /// <para>
        /// <b>Contrat IZoneMerger (ADR 06-05 Phase 1.6)</b> : si
        /// <see cref="RemovedHandles"/>.Count &gt; 0, ce champ <i>doit</i>
        /// être calculé via <see cref="SidecarMerger.Merge"/> — sinon les
        /// vec/∀/∃ des OMaths absorbés sautent au reranking. Le pipeline
        /// log un WARN si le contrat est violé.
        /// </para>
        /// </summary>
        public ResolutionSidecar MergedSidecar { get; set; } = ResolutionSidecar.Empty;
    }
}
