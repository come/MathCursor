using System;
using System.Diagnostics;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.Inserters
{
    /// <summary>
    /// Stratégie d'insertion "pure paragraph fast path" (couche 1/3 du
    /// stack perf, ADR <c>2026-05-12-Perf-commit-pipeline-three-stage-stack</c>).
    ///
    /// <para>Détection stricte : <c>paraText.Trim() == mathSource.Trim()</c>
    /// ET <c>paraRange.OMaths.Count == 0</c> ET <c>paraRange.Tables.Count == 0</c>.
    /// Quand satisfaite, on appelle <c>range.OMaths.Add</c> + <c>BuildUp</c>
    /// DIRECTEMENT sur le range typé. Aucune absorption possible (rien à
    /// absorber autour). Skip splice + isolated build + InsertXML = ~190ms
    /// gagné par commit. Cible : élève qui tape une formule sur sa ligne
    /// vide (cas dominant chez le PAP).</para>
    /// </summary>
    internal sealed class PureFastPathInserter
    {
        private readonly Action<string> _log;

        public PureFastPathInserter(Action<string> log)
        {
            _log = log ?? (s => { });
        }

        public string Name => "fast_path";

        public InsertResult TryInsert(InsertContext ctx)
        {
            if (ctx.IsDisplayMath || ctx.TargetCount != 1) return InsertResult.Skipped;

            try
            {
                var sw = Stopwatch.StartNew();
                string mathSource = (ctx.Doc.Range(ctx.AbsStart, ctx.AbsEnd).Text ?? "").Trim();
                string paraText = TrimParagraphMarks(ctx.FirstPara.Range.Text ?? "").Trim();

                bool textMatchesExactly = paraText == mathSource;
                int omathInPara = ctx.FirstPara.Range.OMaths.Count;
                int tablesInPara = ctx.FirstPara.Range.Tables.Count;
                bool isPure = textMatchesExactly && omathInPara == 0 && tablesInPara == 0;
                _log($"fast_path probe: textEq={textMatchesExactly} omaths={omathInPara} tables={tablesInPara} → pure={isPure}");

                if (!isPure) return InsertResult.Skipped;

                string unicodeMath;
                try { unicodeMath = MathCursor.Core.LatexToUnicodeMath.Convert(ctx.Latex); }
                catch (Exception exU) { _log("fast_path_l2um_error: " + exU.Message); return InsertResult.Skipped; }
                if (string.IsNullOrEmpty(unicodeMath)) return InsertResult.Skipped;

                var typedRange = ctx.Doc.Range(ctx.AbsStart, ctx.AbsEnd);
                typedRange.Text = unicodeMath;
                int afterReplaceEnd = ctx.AbsStart + unicodeMath.Length;
                var rebuiltRange = ctx.Doc.Range(ctx.AbsStart, afterReplaceEnd);
                rebuiltRange.OMaths.Add(rebuiltRange);
                rebuiltRange.OMaths.BuildUp();

                if (!WordOMathFinder.TryLocate(ctx.Doc, ctx.AbsStart, "fast_path",
                    out int newStart, out int newEnd, _log))
                {
                    _log("fast_path: BuildUp ok but OMath not found, fallback to splice");
                    return InsertResult.Skipped;
                }
                sw.Stop();
                _log($"PERF fast_path.total={sw.ElapsedMilliseconds}ms (skipped splice + isolated build)");
                return InsertResult.Ok(newStart, newEnd);
            }
            catch (Exception ex)
            {
                _log("fast_path_error: " + ex.Message);
                return InsertResult.Skipped;
            }
        }

        /// <summary>Retire les marques de ¶ Word (\r, \a, \v, \f) en fin.</summary>
        private static string TrimParagraphMarks(string s)
        {
            while (s.Length > 0
                && (s[s.Length - 1] == '\r' || s[s.Length - 1] == '\a'
                    || s[s.Length - 1] == '\v' || s[s.Length - 1] == '\f'))
            {
                s = s.Substring(0, s.Length - 1);
            }
            return s;
        }
    }
}
