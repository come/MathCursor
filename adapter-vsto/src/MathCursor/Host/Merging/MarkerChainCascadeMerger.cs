using System;
using System.Collections.Generic;
using MathCursor.Core.Resolution;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.Merging
{
    /// <summary>
    /// Cascade montante pour blocs align* (markers <c>=</c>, <c>&lt;=&gt;</c>,
    /// <c>=&gt;</c>, <c>&lt;=</c>). Self-guarding : retourne null si current
    /// source ne commence pas par un marker align.
    ///
    /// <para>Algorithme (cf. ADR 04-05 + brief 30-04 §3.2) :</para>
    /// <list type="bullet">
    /// <item>Paragraphe vide → barrier, stop sans absorber.</item>
    /// <item>Paragraphe contient un OMath à nous en fin → ABSORBÉ comme
    /// sommet de la cascade, on stoppe.</item>
    /// <item>Paragraphe texte commence par marker align → ABSORBÉ, on
    /// continue plus haut.</item>
    /// <item>Paragraphe texte sans marker → stop sans absorber.</item>
    /// </list>
    ///
    /// <para>Self-contained depuis P2.12 du refactor archi.</para>
    /// </summary>
    internal sealed class MarkerChainCascadeMerger : IZoneMerger
    {
        private static readonly string[] AlignMarkers = { "<==>", "<=>", "==>", "=>", "<==", "<=", "=" };

        private readonly Func<Word.Document> _getActiveDoc;
        private readonly ParagraphCascadeProbe _probe;
        private readonly Func<ResolutionSidecar> _getPopupSidecar;
        private readonly Func<string, ResolutionSidecar> _getSidecarForHandle;
        private readonly Action<string> _log;

        public MarkerChainCascadeMerger(
            Func<Word.Document> getActiveDoc,
            ParagraphCascadeProbe probe,
            Func<ResolutionSidecar> getPopupSidecar,
            Func<string, ResolutionSidecar> getSidecarForHandle,
            Action<string> log)
        {
            _getActiveDoc = getActiveDoc ?? throw new ArgumentNullException(nameof(getActiveDoc));
            _probe = probe ?? throw new ArgumentNullException(nameof(probe));
            _getPopupSidecar = getPopupSidecar ?? (() => ResolutionSidecar.Empty);
            _getSidecarForHandle = getSidecarForHandle ?? (_ => ResolutionSidecar.Empty);
            _log = log ?? (s => { });
        }

        public string Name => "MarkerChainCascadeMerger";

        public static bool StartsWithAlignMarker(string s, out string matchedMarker)
        {
            matchedMarker = null;
            if (string.IsNullOrEmpty(s)) return false;
            string trimmed = s.TrimStart();
            foreach (var m in AlignMarkers)
            {
                if (trimmed.StartsWith(m, StringComparison.Ordinal))
                {
                    matchedMarker = m;
                    return true;
                }
            }
            return false;
        }

        public MergeResult TryMerge(int absStart, int absEnd, string currentSource)
        {
            if (!StartsWithAlignMarker(currentSource, out string matchedMarker)) return null;

            var doc = _getActiveDoc();
            if (doc == null) return null;

            _log($"xparMerge_mode1: found marker `{matchedMarker}` in current source");

            var currentPara = doc.Range(absStart, absStart).Paragraphs[1];
            int currentParaStart = currentPara.Range.Start;

            // Entre currentParaStart et absStart : que du whitespace ? Sinon abort.
            if (absStart > currentParaStart)
            {
                string between = doc.Range(currentParaStart, absStart).Text ?? "";
                if (!string.IsNullOrEmpty(between) && between.Trim().Length > 0)
                {
                    _log("xparMerge_mode1: text before zone in current ¶, abort");
                    return null;
                }
            }

            var chainLines = new List<string> { currentSource };
            var removedHandles = new List<string>();
            int chainStart = currentParaStart;
            int cursor = currentParaStart;

            while (cursor > 0)
            {
                Word.Paragraph prev;
                try { prev = doc.Range(cursor - 1, cursor - 1).Paragraphs[1]; }
                catch { break; }
                int prevStart = prev.Range.Start;
                int prevContentEnd = prev.Range.End - 1; // exclut ¶ mark
                if (prevContentEnd <= prevStart) break; // ¶ vide = barrier

                string prevText = doc.Range(prevStart, prevContentEnd).Text ?? "";
                if (string.IsNullOrWhiteSpace(prevText)) break;

                var omathTop = _probe.FindOwnedAtEnd(doc, prevStart, prevContentEnd);
                if (omathTop.HasValue)
                {
                    chainLines.Insert(0, omathTop.Value.source);
                    removedHandles.Add(omathTop.Value.handle);
                    chainStart = omathTop.Value.omStart;
                    _log($"xparMerge_mode1: absorbed OMath top range=[{omathTop.Value.omStart},{prevContentEnd}] source=\"{Preview(omathTop.Value.source)}\"");
                    break;
                }

                if (StartsWithAlignMarker(prevText, out _))
                {
                    chainLines.Insert(0, prevText);
                    chainStart = prevStart;
                    cursor = prevStart;
                    _log($"xparMerge_mode1: cascaded text ¶ [{prevStart},{prevContentEnd}] = \"{Preview(prevText)}\"");
                    continue;
                }

                break; // texte sans marker
            }

            if (chainLines.Count < 2) return null;

            string mergedSource = string.Join("\n", chainLines);
            var mergedSidecar = BuildMergedSidecar(chainLines, removedHandles);

            return new MergeResult
            {
                AbsStart = chainStart,
                AbsEnd = absEnd,
                MergedSource = mergedSource,
                RemovedHandles = removedHandles,
                MergedSidecar = mergedSidecar,
            };
        }

        private ResolutionSidecar BuildMergedSidecar(List<string> chainLines, List<string> removedHandles)
        {
            var sidecarParts = new List<ResolutionSidecar>();
            var offsetShifts = new List<int>();
            int cumulativeShift = 0;
            int absorbedHandleIdx = 0;
            for (int li = 0; li < chainLines.Count; li++)
            {
                ResolutionSidecar partSc;
                bool isLastLine = (li == chainLines.Count - 1);
                if (isLastLine) partSc = _getPopupSidecar();
                else if (absorbedHandleIdx < removedHandles.Count)
                    partSc = _getSidecarForHandle(removedHandles[absorbedHandleIdx++]);
                else partSc = ResolutionSidecar.Empty;
                sidecarParts.Add(partSc);
                offsetShifts.Add(cumulativeShift);
                cumulativeShift += chainLines[li].Length + 1; // +1 pour le \n
            }
            return SidecarMerger.Merge(sidecarParts, offsetShifts);
        }

        private static string Preview(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length > 30 ? s.Substring(0, 30) + "..." : s;
        }
    }
}
