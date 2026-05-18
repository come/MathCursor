using System;
using System.Diagnostics;
using MathCursor.Host.CCMeta;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.Merging
{
    /// <summary>
    /// Méthode UNIQUE de recherche de voisins (OMaths adjacents) à une
    /// zone d'insertion. Utilisé par <see cref="IntraOMathsMerger"/>
    /// (gauche / droite intra-¶) et — phase ultérieure — par les mergers
    /// cross-merge (¶ au-dessus).
    ///
    /// <para>Le résultat (<see cref="Neighbor"/>) contient les bornes Range
    /// + le handle + la source originelle, ce qui permet à l'inserter
    /// d'avoir toute l'info sans avoir à re-scanner les bookmarks.</para>
    ///
    /// <para>Phase B (2026-05-18) : la source du voisin et le handle viennent
    /// du <c>cc.Tag</c> (parsé en MCMeta) au lieu du couple bookmark + store.
    /// Backlink O(1) via <c>om.Range.ParentContentControl</c> au lieu de
    /// scanner <c>doc.Bookmarks</c>.</para>
    ///
    /// <para>Scoped scan : <c>doc.Range(probeStart, probeEnd).OMaths</c>
    /// au lieu de <c>doc.OMaths</c> global (perf gros doc).</para>
    /// </summary>
    internal sealed class NeighborFinder
    {
        private readonly Func<Word.Document> _getActiveDoc;
        private readonly Action<string> _log;

        public NeighborFinder(
            Func<Word.Document> getActiveDoc,
            Action<string> log)
        {
            _getActiveDoc = getActiveDoc ?? throw new ArgumentNullException(nameof(getActiveDoc));
            _log = log ?? (s => { });
        }

        /// <summary>
        /// Recherche les OMaths voisines à gauche et à droite de la zone
        /// <c>[absStart, absEnd)</c>. Tolère 1 espace simple entre la zone
        /// et le voisin (= comportement naturel utilisateur qui tape un
        /// espace de séparation).
        /// </summary>
        public AdjacentNeighbors FindAdjacent(int absStart, int absEnd)
        {
            var doc = _getActiveDoc();
            if (doc == null) { _log("neighbor: skip (no active document)"); return new AdjacentNeighbors(); }

            int docEnd = doc.Content.End;

            // ── GAUCHE — 0 ou 1 espace simple ──────────────────────────
            int leftScan = absStart - 1;
            bool leftHadSpace = false;
            if (leftScan >= 0 && IsSingleSpaceAt(doc, leftScan))
            {
                leftScan--;
                leftHadSpace = true;
            }
            _log($"neighbor left: scan={leftScan} hadSpace={leftHadSpace} (OMath ending at {leftScan + 1})");
            var left = Probe(doc, leftScan, leftScan + 1, expectEndAt: leftScan + 1, expectStartAt: -1, side: "left");

            // ── DROITE — 0 ou 1 espace ─────────────────────────────────
            int rightScan = absEnd;
            bool rightHadSpace = false;
            if (rightScan < docEnd && IsSingleSpaceAt(doc, rightScan))
            {
                rightScan++;
                rightHadSpace = true;
            }
            _log($"neighbor right: scan={rightScan} hadSpace={rightHadSpace} docEnd={docEnd} (OMath starting at {rightScan})");
            Neighbor right = null;
            if (rightScan < docEnd)
                right = Probe(doc, rightScan, rightScan + 1, expectEndAt: -1, expectStartAt: rightScan, side: "right");

            return new AdjacentNeighbors { Left = left, Right = right };
        }

        private Neighbor Probe(Word.Document doc, int probeStart, int probeEnd,
            int expectEndAt, int expectStartAt, string side)
        {
            if (probeStart < 0) { _log($"neighbor {side}: scan < 0, skip"); return null; }
            int docEnd = doc.Content.End;
            int ps = Math.Max(0, probeStart);
            int pe = Math.Min(docEnd, probeEnd);
            if (pe <= ps) pe = ps + 1;

            var sw = Stopwatch.StartNew();
            int scanned = 0;
            Neighbor hit = null;
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

                    _log($"neighbor {side}: candidate OMath range=[{omStart},{omEnd}]");
                    var (_, meta) = CcMetaResolver.ResolveAt(om);
                    if (meta == null)
                    {
                        _log($"neighbor {side}: no MathCursor CC found, skip (OMath orpheline)");
                        break;
                    }
                    _log($"neighbor {side}: handle={meta.HandleId} source=\"{Preview(meta.Steno)}\"");
                    hit = new Neighbor
                    {
                        OMath = om,
                        RangeStart = omStart,
                        RangeEnd = omEnd,
                        Handle = meta.HandleId,
                        Source = meta.Steno ?? "",
                    };
                    break;
                }
            }
            catch (Exception ex) { _log($"neighbor_{side}_scoped_error: {ex.Message}"); }
            sw.Stop();
            _log($"PERF neighbor_{side}.scoped_scan={sw.ElapsedMilliseconds}ms scanned={scanned} hit={(hit != null ? "y" : "n")}");
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
    }
}
