using System;
using System.Collections.Generic;
using System.Text;
using MathCursor.Core.Resolution;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.Merging
{
    /// <summary>
    /// Merge des OMaths adjacents dans le même paragraphe (intra-merge).
    /// Ex : commit <c>AB+BC</c> à gauche + <c>= AC</c> committé maintenant →
    /// fusion en un OMath <c>AB+BC = AC</c>. Priorité max dans le pipeline.
    ///
    /// <para>P2 + refacto 2026-05-14 : la détection des voisins est
    /// déléguée à <see cref="NeighborFinder"/> (méthode UNIQUE de probe
    /// partagée avec le pipeline cross-merge à terme).</para>
    /// </summary>
    internal sealed class IntraOMathsMerger : IZoneMerger
    {
        private readonly NeighborFinder _finder;
        private readonly Func<ResolutionSidecar> _getPopupSidecar;
        private readonly Func<string, ResolutionSidecar> _getSidecarForHandle;
        private readonly Action<string> _log;

        public IntraOMathsMerger(
            NeighborFinder finder,
            Func<ResolutionSidecar> getPopupSidecar,
            Func<string, ResolutionSidecar> getSidecarForHandle,
            Action<string> log)
        {
            _finder = finder ?? throw new ArgumentNullException(nameof(finder));
            _getPopupSidecar = getPopupSidecar ?? (() => ResolutionSidecar.Empty);
            _getSidecarForHandle = getSidecarForHandle ?? (_ => ResolutionSidecar.Empty);
            _log = log ?? (s => { });
        }

        public string Name => "IntraOMathsMerger";

        public MergeResult TryMerge(int absStart, int absEnd, string currentSource)
        {
            try
            {
                _log($"merge: try absStart={absStart} absEnd={absEnd} middle=\"{Preview(currentSource)}\"");

                var adj = _finder.FindAdjacent(absStart, absEnd);
                if (adj.Left == null && adj.Right == null)
                {
                    _log("merge: no adjacent OMath found, skip merge");
                    return null;
                }

                // ── Assemble la mergedSource + le sidecar fusionné ──────
                var sb = new StringBuilder();
                if (adj.Left != null) { sb.Append(adj.Left.Source); sb.Append(' '); }
                sb.Append(currentSource ?? string.Empty);
                if (adj.Right != null) { sb.Append(' '); sb.Append(adj.Right.Source); }

                int newStart = adj.Left?.RangeStart ?? absStart;
                int newEnd = adj.Right?.RangeEnd ?? absEnd;

                var removed = new List<string>();
                if (adj.Left != null) removed.Add(adj.Left.Handle);
                if (adj.Right != null) removed.Add(adj.Right.Handle);

                var mergedSc = IntraMergeSidecarBuilder.Build(
                    adj.Left?.Source, adj.Left != null ? _getSidecarForHandle(adj.Left.Handle) : null,
                    currentSource, _getPopupSidecar(),
                    adj.Right?.Source, adj.Right != null ? _getSidecarForHandle(adj.Right.Handle) : null);
                _log($"merge sidecar: pins={mergedSc.SpanPins.Count} ruleVotes={mergedSc.ZoneVotes.Count}");

                return new MergeResult
                {
                    AbsStart = newStart,
                    AbsEnd = newEnd,
                    MergedSource = sb.ToString(),
                    RemovedHandles = removed,
                    MergedSidecar = mergedSc,
                };
            }
            catch (Exception ex)
            {
                _log("try_merge_error: " + ex.Message);
                return null;
            }
        }

        private static string Preview(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length > 30 ? s.Substring(0, 30) + "..." : s;
        }
    }
}
