using System;
using System.Collections.Generic;
using System.Text;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.Inserters
{
    /// <summary>
    /// Helpers Word interop pour localiser une OMath fraîchement insérée
    /// et extraire le texte typé hors-OMath dans une plage. Statique parce
    /// que sans état ; appelé par les stratégies d'insertion + autres
    /// code paths. P2.7 du refactor archi (ADR
    /// <c>2026-05-12-Refactor-pure-merger-atomic-insert</c>).
    /// </summary>
    internal static class WordOMathFinder
    {
        /// <summary>
        /// Localise une OMath à la position <paramref name="absStart"/>
        /// (typiquement juste insérée). Scope au ¶ courant (rapide + tolérant
        /// à n'importe quelle largeur OMath). Fallback global scan si miss.
        /// </summary>
        public static bool TryLocate(
            Word.Document doc, int absStart, string logPrefix,
            out int newStart, out int newEnd, Action<string> logDiag = null)
        {
            newStart = absStart;
            newEnd = absStart;
            try
            {
                int docEnd = doc.Content.End;
                int probePos = Math.Min(Math.Max(0, absStart), Math.Max(0, docEnd - 1));
                Word.Range paraRange = null;
                try { paraRange = doc.Range(probePos, probePos).Paragraphs[1].Range; }
                catch (Exception exP) { logDiag?.Invoke($"{logPrefix}_locate_para_probe_error: " + exP.Message); }
                if (paraRange != null)
                {
                    foreach (Word.OMath om in paraRange.OMaths)
                    {
                        var rng = om.Range;
                        if (rng.Start <= absStart && rng.End > absStart)
                        {
                            logDiag?.Invoke($"{logPrefix}: matched [{rng.Start},{rng.End}] (para probe)");
                            newStart = rng.Start;
                            newEnd = rng.End;
                            return true;
                        }
                    }
                }
                // Fallback : scan global (gros doc lent mais correct).
                foreach (Word.OMath om in doc.OMaths)
                {
                    var rng = om.Range;
                    if (rng.Start <= absStart && rng.End > absStart)
                    {
                        logDiag?.Invoke($"{logPrefix}: matched [{rng.Start},{rng.End}] (fallback global scan)");
                        newStart = rng.Start;
                        newEnd = rng.End;
                        return true;
                    }
                }
            }
            catch (Exception ex) { logDiag?.Invoke($"{logPrefix}_locate_error: " + ex.Message); }
            logDiag?.Invoke($"{logPrefix}: ok but OMath not found at absStart={absStart}");
            return false;
        }

        /// <summary>
        /// Concatène les chars de [absStart, absEnd] qui sont HORS de toute
        /// OMath. Sans OMath dans la plage, équivalent à Range.Text. Avec
        /// OMath absorbées, exclut les chars placeholder pour que le splicer
        /// tail-match les <c>&lt;w:r&gt;</c> typés.
        /// </summary>
        public static string ComputeTypedText(Word.Document doc, int absStart, int absEnd)
        {
            var range = doc.Range(absStart, absEnd);
            var omRanges = new List<(int s, int e)>();
            try
            {
                foreach (Word.OMath om in range.OMaths)
                    omRanges.Add((om.Range.Start, om.Range.End));
            }
            catch { }
            if (omRanges.Count == 0) return range.Text ?? "";

            omRanges.Sort((a, b) => a.s.CompareTo(b.s));
            var sb = new StringBuilder();
            int cur = absStart;
            foreach (var (s, e) in omRanges)
            {
                if (s > cur) sb.Append(doc.Range(cur, s).Text ?? "");
                cur = Math.Max(cur, e);
            }
            if (cur < absEnd) sb.Append(doc.Range(cur, absEnd).Text ?? "");
            return sb.ToString();
        }
    }
}
