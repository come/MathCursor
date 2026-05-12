using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using MathCursor.Core.Resolution;
using MathCursor.Host.Bookmarks;
using MathCursor.HostContract;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.Merging
{
    /// <summary>
    /// Merge des OMaths adjacents dans le même paragraphe (intra-merge).
    /// Ex : commit <c>AB+BC</c> à gauche + <c>= AC</c> committé maintenant →
    /// fusion en un OMath <c>AB+BC = AC</c>. Priorité max dans le pipeline
    /// (gagne toujours sur cross-merge si applicable).
    ///
    /// <para>Implémentation complète (P2.9 du refactor archi) : self-contained,
    /// scan scoped pour perf gros doc (cf. ADR
    /// <c>2026-05-12-Perf-commit-pipeline-three-stage-stack</c>), construit
    /// le <see cref="ResolutionSidecar"/> fusionné via
    /// <see cref="IntraMergeSidecarBuilder"/>.</para>
    /// </summary>
    internal sealed class IntraOMathsMerger : IZoneMerger
    {
        private readonly Func<Word.Document> _getActiveDoc;
        private readonly IEquationStore _store;
        private readonly EquationBookmarkRegistry _bookmarks;
        private readonly Func<ResolutionSidecar> _getPopupSidecar;
        private readonly Func<string, ResolutionSidecar> _getSidecarForHandle;
        private readonly Action<string> _log;

        public IntraOMathsMerger(
            Func<Word.Document> getActiveDoc,
            IEquationStore store,
            EquationBookmarkRegistry bookmarks,
            Func<ResolutionSidecar> getPopupSidecar,
            Func<string, ResolutionSidecar> getSidecarForHandle,
            Action<string> log)
        {
            _getActiveDoc = getActiveDoc ?? throw new ArgumentNullException(nameof(getActiveDoc));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _bookmarks = bookmarks ?? throw new ArgumentNullException(nameof(bookmarks));
            _getPopupSidecar = getPopupSidecar ?? (() => ResolutionSidecar.Empty);
            _getSidecarForHandle = getSidecarForHandle ?? (_ => ResolutionSidecar.Empty);
            _log = log ?? (s => { });
        }

        public string Name => "IntraOMathsMerger";

        public MergeResult TryMerge(int absStart, int absEnd, string currentSource)
        {
            try
            {
                var doc = _getActiveDoc();
                if (doc == null) { _log("merge: skip (no active document)"); return null; }

                _log($"merge: try absStart={absStart} absEnd={absEnd} middle=\"{Preview(currentSource)}\"");

                // ── GAUCHE — 0 ou 1 espace simple, scoped scan ──────────
                int docEnd = doc.Content.End;
                int leftScan = absStart - 1;
                bool leftHadSpace = false;
                if (leftScan >= 0 && IsSingleSpaceAt(doc, leftScan))
                {
                    leftScan--;
                    leftHadSpace = true;
                }
                _log($"merge left: scan={leftScan} hadSpace={leftHadSpace} (looking for OMath ending at {leftScan + 1})");

                var leftHit = ProbeNeighbor(doc, leftScan, leftScan + 1, expectEndAt: leftScan + 1, expectStartAt: -1, side: "left");

                // ── DROITE — 0 ou 1 espace, scoped scan ─────────────────
                int rightScan = absEnd;
                bool rightHadSpace = false;
                if (rightScan < docEnd && IsSingleSpaceAt(doc, rightScan))
                {
                    rightScan++;
                    rightHadSpace = true;
                }
                _log($"merge right: scan={rightScan} hadSpace={rightHadSpace} docEnd={docEnd} (looking for OMath starting at {rightScan})");

                NeighborHit rightHit = null;
                if (rightScan < docEnd)
                    rightHit = ProbeNeighbor(doc, rightScan, rightScan + 1, expectEndAt: -1, expectStartAt: rightScan, side: "right");

                if (leftHit == null && rightHit == null)
                {
                    _log("merge: no adjacent OMath found, skip merge");
                    return null;
                }

                // ── Assemble la mergedSource + le sidecar fusionné ──────
                var sb = new StringBuilder();
                if (leftHit != null) { sb.Append(leftHit.Source); sb.Append(' '); }
                sb.Append(currentSource ?? string.Empty);
                if (rightHit != null) { sb.Append(' '); sb.Append(rightHit.Source); }

                int newStart = leftHit != null ? leftHit.OMath.Range.Start : absStart;
                int newEnd = rightHit != null ? rightHit.OMath.Range.End : absEnd;

                var removed = new List<string>();
                if (leftHit != null) removed.Add(leftHit.Handle);
                if (rightHit != null) removed.Add(rightHit.Handle);

                var mergedSc = IntraMergeSidecarBuilder.Build(
                    leftHit?.Source, leftHit != null ? _getSidecarForHandle(leftHit.Handle) : null,
                    currentSource, _getPopupSidecar(),
                    rightHit?.Source, rightHit != null ? _getSidecarForHandle(rightHit.Handle) : null);
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

        /// <summary>
        /// Scoped scan : doc.Range(probeStart, probeEnd).OMaths au lieu de
        /// scan global doc.OMaths (~150ms × N sur gros doc). Filtre par
        /// position attendue (<paramref name="expectEndAt"/> pour le merge
        /// gauche, <paramref name="expectStartAt"/> pour droite), récupère
        /// le handle + source si l'OMath est à nous.
        /// </summary>
        private NeighborHit ProbeNeighbor(Word.Document doc, int probeStart, int probeEnd,
            int expectEndAt, int expectStartAt, string side)
        {
            if (probeStart < 0) { _log($"merge {side}: scan < 0, skip"); return null; }
            int docEnd = doc.Content.End;
            int ps = Math.Max(0, probeStart);
            int pe = Math.Min(docEnd, probeEnd);
            if (pe <= ps) pe = ps + 1;

            var sw = Stopwatch.StartNew();
            int scanned = 0;
            NeighborHit hit = null;
            try
            {
                foreach (Word.OMath om in doc.Range(ps, pe).OMaths)
                {
                    scanned++;
                    int omStart = om.Range.Start;
                    int omEnd = om.Range.End;
                    bool match = (expectEndAt >= 0 && omEnd == expectEndAt)
                              || (expectStartAt >= 0 && omStart == expectStartAt);
                    if (!match) continue;

                    _log($"merge {side}: candidate OMath range=[{omStart},{omEnd}]");
                    var h = _bookmarks.FindHandleForOMath(om);
                    _log($"merge {side}: handle={(h ?? "null")}");
                    if (h == null) break;

                    try
                    {
                        var stored = _store.RetrieveAsync(new EquationHandle(h)).GetAwaiter().GetResult();
                        if (stored != null && !string.IsNullOrEmpty(stored.Source))
                        {
                            hit = new NeighborHit { OMath = om, Handle = h, Source = stored.Source };
                            _log($"merge {side}: source=\"{Preview(stored.Source)}\"");
                        }
                        else { _log($"merge {side}: stored null or empty source"); }
                    }
                    catch (Exception ex) { _log($"merge_retrieve_{side}_error: {ex.Message}"); }
                    break;
                }
            }
            catch (Exception ex) { _log($"merge_{side}_scoped_error: {ex.Message}"); }
            sw.Stop();
            _log($"PERF merge_{side}.scoped_scan={sw.ElapsedMilliseconds}ms scanned={scanned} {side}OMath={(hit != null ? "found" : "null")}");
            return hit;
        }

        private static bool IsSingleSpaceAt(Word.Document doc, int pos)
        {
            try
            {
                var t = doc.Range(pos, pos + 1).Text ?? "";
                return t.Length > 0 && t[0] == ' ';
            }
            catch { return false; }
        }

        private static string Preview(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length > 30 ? s.Substring(0, 30) + "..." : s;
        }

        private sealed class NeighborHit
        {
            public Word.OMath OMath;
            public string Handle;
            public string Source;
        }
    }
}
