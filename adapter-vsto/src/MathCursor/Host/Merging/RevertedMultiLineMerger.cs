using System;
using System.Collections.Generic;
using MathCursor.Core.Resolution;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.Merging
{
    /// <summary>
    /// Mode 2 du cross-merge (cf. ADR 2026-05-04-multiline-edit-cascade) :
    /// si l'user a fait « Revenir à la saisie » sur un OMath multi-ligne
    /// et que le commit courant est dans la zone reverted, on absorbe
    /// TOUS les paragraphes de la zone (y compris la 1re ligne sans marker).
    ///
    /// <para>Self-contained depuis P2.11 du refactor archi : reçoit la zone
    /// reverted via fonction (state vit dans le caller, ex.
    /// <see cref="EditMode.EditModeController"/>) et le sidecar d'origine
    /// via fonction du handle reverted.</para>
    /// </summary>
    internal sealed class RevertedMultiLineMerger : IZoneMerger
    {
        public readonly struct RevertedZone
        {
            public readonly int Start;
            public readonly int End;
            public readonly string HandleId;
            public bool IsActive => Start >= 0;
            public RevertedZone(int s, int e, string h) { Start = s; End = e; HandleId = h; }
            public static RevertedZone Inactive => new RevertedZone(-1, -1, null);
        }

        private readonly Func<Word.Document> _getActiveDoc;
        private readonly Func<RevertedZone> _getZone;
        private readonly Func<string, ResolutionSidecar> _getSidecarForHandle;
        private readonly Action<string> _log;

        public RevertedMultiLineMerger(
            Func<Word.Document> getActiveDoc,
            Func<RevertedZone> getZone,
            Func<string, ResolutionSidecar> getSidecarForHandle,
            Action<string> log)
        {
            _getActiveDoc = getActiveDoc ?? throw new ArgumentNullException(nameof(getActiveDoc));
            _getZone = getZone ?? throw new ArgumentNullException(nameof(getZone));
            _getSidecarForHandle = getSidecarForHandle ?? (_ => ResolutionSidecar.Empty);
            _log = log ?? (s => { });
        }

        public string Name => "RevertedMultiLineMerger";

        public MergeResult TryMerge(int absStart, int absEnd, string currentSource)
        {
            var zone = _getZone();
            if (!zone.IsActive) return null;
            if (absStart < zone.Start || absStart > zone.End + 1) return null;

            var doc = _getActiveDoc();
            if (doc == null) return null;

            try
            {
                var zoneRange = doc.Range(zone.Start, Math.Min(zone.End, doc.Content.End));
                var paras = zoneRange.Paragraphs;
                if (paras == null || paras.Count == 0) return null;

                var paragraphTexts = new List<string>();
                var paragraphStarts = new List<int>();
                int chainStart = int.MaxValue;
                int chainEnd = int.MinValue;
                foreach (Word.Paragraph p in paras)
                {
                    var r = p.Range;
                    if (r.Start < chainStart) chainStart = r.Start;
                    if (r.End - 1 > chainEnd) chainEnd = r.End - 1; // exclut ¶ mark
                    int contentEnd = Math.Max(r.Start, r.End - 1);
                    string txt = doc.Range(r.Start, contentEnd).Text ?? "";
                    paragraphTexts.Add(txt);
                    paragraphStarts.Add(r.Start);
                }
                if (paragraphTexts.Count < 2) return null;

                string mergedSource = RevertedZoneMerger.BuildMergedSource(
                    paragraphTexts, paragraphStarts, absStart, currentSource);
                // newAbsEnd = chainEnd (pas chainEnd + 1) : on ne consomme pas
                // le ¶ qui termine la dernière ligne. Cf. bug user 04-05.
                int newAbsEnd = Math.Max(chainEnd, absEnd);

                var mergedSidecar = !string.IsNullOrEmpty(zone.HandleId)
                    ? _getSidecarForHandle(zone.HandleId)
                    : ResolutionSidecar.Empty;
                _log($"xparMerge_mode2: revert zone absorbed {paragraphTexts.Count} paragraphs, range=[{chainStart},{newAbsEnd}], sidecarPins={mergedSidecar.SpanPins.Count}");

                return new MergeResult
                {
                    AbsStart = chainStart,
                    AbsEnd = newAbsEnd,
                    MergedSource = mergedSource,
                    RemovedHandles = new List<string>(),
                    MergedSidecar = mergedSidecar,
                };
            }
            catch (Exception ex)
            {
                _log("xparMerge_mode2_error: " + ex.Message);
                return null;
            }
        }
    }
}
