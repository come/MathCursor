using System;
using System.Collections.Generic;
using MathCursor.Core.Resolution;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.Merging
{
    /// <summary>
    /// Cascade montante pour systèmes <c>{</c> (cases). Self-guarding :
    /// retourne null si current source ne commence pas par <c>{ </c>.
    /// Cf. ADR 05-05 cases-multiline-phase2 + brief 30-04 §3.4 (pas de mix
    /// avec align). Logique de merge pure dans <see cref="CasesCascadeMerger"/>
    /// (helper testé séparément).
    ///
    /// <para>Self-contained depuis P2.12 du refactor archi.</para>
    /// </summary>
    internal sealed class CasesChainCascadeMerger : IZoneMerger
    {
        private readonly Func<Word.Document> _getActiveDoc;
        private readonly ParagraphCascadeProbe _probe;
        private readonly Func<ResolutionSidecar> _getPopupSidecar;
        private readonly Func<string, ResolutionSidecar> _getSidecarForHandle;
        private readonly Action<string> _log;

        public CasesChainCascadeMerger(
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

        public string Name => "CasesChainCascadeMerger";

        public MergeResult TryMerge(int absStart, int absEnd, string currentSource)
        {
            if (!CasesCascadeMerger.StartsWithCasesMarker(currentSource)) return null;

            var doc = _getActiveDoc();
            if (doc == null) return null;

            _log($"xparMerge_cases: found cases marker `{{ ` in current source");

            var currentPara = doc.Range(absStart, absStart).Paragraphs[1];
            int currentParaStart = currentPara.Range.Start;

            if (absStart > currentParaStart)
            {
                string between = doc.Range(currentParaStart, absStart).Text ?? "";
                if (!string.IsNullOrEmpty(between) && between.Trim().Length > 0)
                {
                    _log("xparMerge_cases: text before zone in current ¶, abort");
                    return null;
                }
            }

            var paragraphsAbove = new List<string>();
            var removedHandles = new List<string>();
            int chainStart = currentParaStart;
            int cursor = currentParaStart;

            while (cursor > 0)
            {
                Word.Paragraph prev;
                try { prev = doc.Range(cursor - 1, cursor - 1).Paragraphs[1]; }
                catch { break; }
                int prevStart = prev.Range.Start;
                int prevContentEnd = prev.Range.End - 1;
                if (prevContentEnd <= prevStart) break; // ¶ vide = barrier

                var omathTop = _probe.FindOwnedAtEnd(doc, prevStart, prevContentEnd);
                if (omathTop.HasValue)
                {
                    // Source de vérité = le LaTeX rendu (cc.Tag.Latex). Un
                    // OMath EST cases ssi son latex contient `\begin{cases}`.
                    // Ça résout l'ambiguïté de la source steno :
                    //   - `{x+2=3` rendu cases (typé sans espace, 1ère cellule)
                    //   - `{1,2}` set en extension, PAS cases
                    // La source seule ne suffit pas. Le current source reste
                    // validé strictement via StartsWithCasesMarker (l'user a
                    // tapé `{ ` car listmode a pré-injecté).
                    bool aboveIsCases = !string.IsNullOrEmpty(omathTop.Value.latex)
                        && omathTop.Value.latex.IndexOf("\\begin{cases}", StringComparison.Ordinal) >= 0;
                    if (aboveIsCases)
                    {
                        // Normaliser la source absorbée à `{ ` (avec espace) si
                        // manquant — sinon BuildCascade rejettera (StartsWithCasesMarker strict).
                        string absorbedSource = omathTop.Value.source;
                        var trimmed = absorbedSource.TrimStart();
                        if (trimmed.Length >= 1 && trimmed[0] == '{'
                            && (trimmed.Length < 2 || trimmed[1] != ' '))
                        {
                            int idxBrace = absorbedSource.IndexOf('{');
                            absorbedSource = absorbedSource.Substring(0, idxBrace + 1) + " " + absorbedSource.Substring(idxBrace + 1);
                        }
                        paragraphsAbove.Insert(0, absorbedSource);
                        removedHandles.Add(omathTop.Value.handle);
                        chainStart = omathTop.Value.omStart;
                        _log($"xparMerge_cases: absorbed OMath top range=[{omathTop.Value.omStart},{prevContentEnd}] source=\"{Preview(absorbedSource)}\" (cases via latex)");
                    }
                    else
                    {
                        _log("xparMerge_cases: OMath above is not cases, stop");
                    }
                    break;
                }

                string prevText = doc.Range(prevStart, prevContentEnd).Text ?? "";
                if (string.IsNullOrWhiteSpace(prevText)) break;

                if (CasesCascadeMerger.StartsWithCasesMarker(prevText))
                {
                    paragraphsAbove.Insert(0, prevText);
                    chainStart = prevStart;
                    cursor = prevStart;
                    _log($"xparMerge_cases: cascaded text ¶ [{prevStart},{prevContentEnd}] = \"{Preview(prevText)}\"");
                    continue;
                }

                break; // texte non-cases, pas de mix
            }

            var cascade = CasesCascadeMerger.BuildCascade(paragraphsAbove, currentSource);
            if (cascade == null) return null;

            // Fusion des sidecars : chainLines = AbsorbedCount dernières lignes
            // de paragraphsAbove + currentSource (cf. CasesCascadeMerger.BuildCascade).
            var chainLines = new List<string>();
            int startIdx = paragraphsAbove.Count - cascade.AbsorbedCount;
            for (int i = startIdx; i < paragraphsAbove.Count; i++)
                chainLines.Add(paragraphsAbove[i]);
            chainLines.Add(currentSource);

            var mergedSidecar = BuildMergedSidecar(chainLines, removedHandles);

            return new MergeResult
            {
                AbsStart = chainStart,
                AbsEnd = absEnd,
                MergedSource = cascade.MergedSource,
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
                cumulativeShift += chainLines[li].Length + 1;
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
