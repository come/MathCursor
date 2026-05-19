using System;
using System.Collections.Generic;
using System.Text;
using MathCursor.Core.Resolution;
using MathCursor.Host.CCMeta;
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

        // ──────────────────────────────────────────────────────────────────
        // Phase B revival (2026-05-18) — LaTeX-preserving, voisin gauche
        // uniquement. Cf. ADR 2026-05-18-Feat-intra-omaths-merger-revival.
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Marqueurs de continuation : si <c>currentSource</c> commence par
        /// l'un d'eux, on tente le merge avec le voisin gauche. Sinon, on
        /// préserve l'OMath gauche intacte (= 2 OMaths côte à côte, cas
        /// légitime <c>f(x) g(x)</c>).
        /// </summary>
        public static bool IsMergeMarker(string source)
        {
            if (string.IsNullOrEmpty(source)) return false;
            // Test du plus long au plus court pour éviter "=" qui matche "=>" / "<=>".
            if (source.StartsWith("<=>", StringComparison.Ordinal)) return true;
            if (source.StartsWith("=>", StringComparison.Ordinal)) return true;
            if (source.StartsWith("=", StringComparison.Ordinal)) return true;
            if (source.StartsWith("{", StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// Tente le merge avec le voisin gauche, en PRÉSERVANT son LaTeX
        /// (lecture depuis <c>cc.Tag.Latex</c>, pas de re-rendu).
        ///
        /// <para>Retourne null si :
        /// <list type="bullet">
        ///   <item>la source ne commence pas par un marker de continuation,</item>
        ///   <item>pas de voisin gauche détecté,</item>
        ///   <item>le voisin n'a pas de CC MathCursor (= pas à nous),</item>
        ///   <item>le meta est corrompu (Latex/Steno null),</item>
        ///   <item>drift OMML détecté (édition manuelle Word post-commit).</item>
        /// </list>
        /// </para>
        /// </summary>
        public MergeResult TryMergeWithLeft(
            int absStart, int absEnd, string currentSource, string currentLatex)
        {
            try
            {
                if (!IsMergeMarker(currentSource))
                {
                    _log("merge_left: skip (source ne commence pas par marker)");
                    return null;
                }

                var adj = _finder.FindAdjacent(absStart, absEnd);
                if (adj.Left == null)
                {
                    _log("merge_left: skip (pas de voisin gauche)");
                    return null;
                }

                // Re-resolve la CC du voisin pour lire latex + hash + cc handle
                // (NeighborFinder n'expose que Steno et Handle ID).
                var (cc, meta) = CcMetaResolver.ResolveAt(adj.Left.OMath);
                if (cc == null || meta == null)
                {
                    _log("merge_left: skip (CC ou meta absent sur voisin)");
                    return null;
                }
                if (string.IsNullOrEmpty(meta.Latex))
                {
                    _log("merge_left: skip (meta.Latex vide)");
                    return null;
                }

                // Drift detection : si le OMML actuel du voisin diffère du
                // hash stocké → l'utilisateur a édité manuellement → skip.
                string currentOmmlHash = null;
                try { currentOmmlHash = Sha1Helper.Compute(adj.Left.OMath.Range.WordOpenXML); }
                catch (Exception exHash) { _log("merge_left: hash_error " + exHash.Message); return null; }

                if (!string.IsNullOrEmpty(meta.OmmlHash)
                    && !string.Equals(currentOmmlHash, meta.OmmlHash, StringComparison.Ordinal))
                {
                    // Phase 1 (2026-05-19) : drift logué en WARNING mais on
                    // continue. Faux positifs observés : Word mute le
                    // WordOpenXML de l'OMath entre le Tag-set time et le
                    // probe-time (post-commit layout, CcSticky, autoformat).
                    // Le hash actuel n'est pas fiable comme détecteur
                    // d'édition manuelle. Phase 2 : hash content-only
                    // canonicalisé. Cf. ADR 2026-05-18-Feat-intra-omaths-merger-revival.
                    _log(string.Format(
                        "merge_left: WARN drift detected (stored={0} current={1}) — proceed anyway (phase 1)",
                        Short(meta.OmmlHash), Short(currentOmmlHash)));
                }

                // Build merged LaTeX + steno. Pas de re-rendu : on concatène
                // directement les LaTeX (gauche déjà validé + nouveau).
                string mergedLatex = (meta.Latex ?? "") + (currentLatex ?? "");
                string mergedSteno = (meta.Steno ?? "") + (currentSource ?? "");

                // Zone d'absorption : on remonte jusqu'à cc.Range.Start (pas
                // om.Range.Start) pour englober les markers structurels du CC
                // (ex: idx 0 invisible avant l'OMath qui prend un slot interne).
                // Sinon SetRange(om.Start, ...) laisse un orphelin de CC.
                int leftStart;
                try { leftStart = cc.Range.Start; }
                catch { leftStart = adj.Left.RangeStart; }

                _log(string.Format(
                    "merge_left: ok absStart {0}→{1} (cc.Start={2}, om.Start={3}), source=\"{4}\" + \"{5}\"",
                    absStart, leftStart, leftStart, adj.Left.RangeStart,
                    Preview(meta.Steno), Preview(currentSource)));

                return new MergeResult
                {
                    AbsStart = leftStart,
                    AbsEnd = absEnd,
                    MergedSource = mergedSteno,
                    MergedLatex = mergedLatex,
                    LeftCcToCleanup = cc,
                    RemovedHandles = new List<string> { adj.Left.Handle },
                    MergedSidecar = ResolutionSidecar.Empty,
                };
            }
            catch (Exception ex)
            {
                _log("try_merge_with_left_error: " + ex.Message);
                return null;
            }
        }

        private static string Short(string hash)
        {
            if (string.IsNullOrEmpty(hash)) return "";
            return hash.Length > 8 ? hash.Substring(0, 8) : hash;
        }
    }
}
