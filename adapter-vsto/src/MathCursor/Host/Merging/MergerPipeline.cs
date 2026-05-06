using System;
using System.Collections.Generic;

namespace MathCursor.Host.Merging
{
    /// <summary>
    /// Itère une liste ordonnée de <see cref="IZoneMerger"/> et renvoie le
    /// premier <see cref="MergeResult"/> non-null (premier match wins).
    /// L'ordre = priorité (intra avant cross, cases avant marker, etc.).
    /// Cf. ADR <c>2026-05-06-Meta-zone-merger-pipeline</c>.
    /// <para>
    /// Remplace le if-pile <c>if (merged == null) merged = TryX(...)</c> qui
    /// vivait dans <c>SuggestionService.OnPopupCommitRequested</c>.
    /// </para>
    /// </summary>
    internal sealed class MergerPipeline
    {
        private readonly IReadOnlyList<IZoneMerger> _mergers;
        private readonly Action<string> _log;

        public MergerPipeline(IReadOnlyList<IZoneMerger> mergers, Action<string> log = null)
        {
            _mergers = mergers ?? throw new ArgumentNullException(nameof(mergers));
            _log = log ?? (_ => { });
        }

        /// <summary>
        /// Essaie chaque merger dans l'ordre. Retourne le premier
        /// <see cref="MergeResult"/> non-null, ou <c>null</c> si aucun n'a
        /// matché. Vérifie aussi le contrat sidecar : si un merger absorbe
        /// des handles mais ne calcule pas <c>MergedSidecar</c>, log un WARN
        /// (les désambig seront perdues — bug pattern 06-05).
        /// </summary>
        public MergeResult Run(int absStart, int absEnd, string currentSource)
        {
            foreach (var m in _mergers)
            {
                MergeResult r;
                try { r = m.TryMerge(absStart, absEnd, currentSource); }
                catch (Exception ex)
                {
                    _log($"merger_error: {m.Name} threw: {ex.Message}");
                    continue;
                }
                if (r == null) continue;

                // Contrat ADR 06-05 Phase 1.6 : handles absorbés ⇒ sidecar
                // fusionné non-vide. Sinon les vec/∀/∃ sautent au reranking.
                if (r.RemovedHandles != null && r.RemovedHandles.Count > 0
                    && (r.MergedSidecar == null || r.MergedSidecar.IsEmpty))
                {
                    _log($"merger_warn: {m.Name} a absorbé {r.RemovedHandles.Count} handle(s) " +
                         "sans calculer MergedSidecar — désambig vec/forall vont sauter");
                }

                _log($"merger_match: {m.Name} → range=[{r.AbsStart},{r.AbsEnd}] removed={r.RemovedHandles?.Count ?? 0}");
                return r;
            }
            return null;
        }
    }
}
