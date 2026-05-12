using System;
using System.Diagnostics;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.Inserters
{
    /// <summary>
    /// Stratégie d'insertion par splice du <c>&lt;w:p&gt;</c> existant.
    /// Lit le paraXml courant (avec cache pré-fetch idle), splice la
    /// nouvelle OMath dans les runs typés (avec absorption des OMaths
    /// voisines via <see cref="InlineOMathSplicer"/>), réinsère via
    /// <c>firstPara.Range.InsertXML</c>. Atomique au niveau du ¶.
    ///
    /// <para>Couvre le cas inline single-¶ : <c>targetCount == 1</c>,
    /// pas display math. Le fast path l'a déjà éliminé pour les ¶ purs.</para>
    /// </summary>
    internal sealed class InlineSpliceInserter
    {
        private readonly OMathXmlCache _omathCache;
        private readonly ParaXmlPrefetcher _prefetcher;
        private readonly OMathStagingService _staging;
        private readonly Action<string> _log;

        public InlineSpliceInserter(
            OMathXmlCache omathCache,
            ParaXmlPrefetcher prefetcher,
            OMathStagingService staging,
            Action<string> log)
        {
            _omathCache = omathCache ?? throw new ArgumentNullException(nameof(omathCache));
            _prefetcher = prefetcher ?? throw new ArgumentNullException(nameof(prefetcher));
            _staging = staging ?? throw new ArgumentNullException(nameof(staging));
            _log = log ?? (s => { });
        }

        public string Name => "para_splice";

        public InsertResult TryInsert(InsertContext ctx)
        {
            if (ctx.IsDisplayMath || ctx.TargetCount != 1) return InsertResult.Skipped;

            try
            {
                string mathSource = WordOMathFinder.ComputeTypedText(ctx.Doc, ctx.AbsStart, ctx.AbsEnd);

                // Lecture paraXml — cache prefetch hit ou live read.
                int firstParaStart = ctx.FirstPara.Range.Start;
                string firstParaText = ctx.FirstPara.Range.Text ?? "";
                string paraXml = _prefetcher.TryGet(firstParaStart, firstParaText);
                if (paraXml != null)
                {
                    _log($"PERF para_splice.read_para_xml=0ms (prefetch hit, len={paraXml.Length})");
                }
                else
                {
                    var swRead = Stopwatch.StartNew();
                    paraXml = ctx.FirstPara.Range.WordOpenXML;
                    swRead.Stop();
                    _log($"PERF para_splice.read_para_xml={swRead.ElapsedMilliseconds}ms paraXmlLen={paraXml?.Length ?? 0}");
                }
                if (string.IsNullOrEmpty(paraXml)) return InsertResult.Skipped;

                // Élément <m:oMath> — cache LRU hit ou build ghost doc.
                string newOMathOnly = _omathCache.TryGet(ctx.Latex);
                if (newOMathOnly != null)
                {
                    _log($"PERF para_splice.build_isolated=0ms (cache hit)");
                }
                else
                {
                    var swBuild = Stopwatch.StartNew();
                    string capturedPkg = _staging.BuildOMathXml(ctx.Latex);
                    swBuild.Stop();
                    _log($"PERF para_splice.build_isolated={swBuild.ElapsedMilliseconds}ms");
                    newOMathOnly = InlineOMathSplicer.ExtractOMathElement(capturedPkg);
                    if (string.IsNullOrEmpty(newOMathOnly)) return InsertResult.Skipped;
                    _omathCache.Set(ctx.Latex, newOMathOnly);
                }

                // Splice (avec absorption des voisins via absorbedHandles).
                var swSplice = Stopwatch.StartNew();
                string newParaXml = InlineOMathSplicer.SpliceOMathInDocXml(
                    paraXml, mathSource, newOMathOnly, ctx.AbsorbedHandles);
                swSplice.Stop();
                _log($"PERF para_splice.splice_xml={swSplice.ElapsedMilliseconds}ms");

                if (string.IsNullOrEmpty(newParaXml))
                {
                    _log($"para_splice: skip (no match for \"{Preview(mathSource)}\")");
                    return InsertResult.Skipped;
                }
                _log($"para_splice: ok mathSource=\"{Preview(mathSource)}\" newParaLen={newParaXml.Length}");

                // InsertXML atomique sur le ¶ entier.
                var swInsert = Stopwatch.StartNew();
                ctx.FirstPara.Range.InsertXML(newParaXml);
                swInsert.Stop();
                _log($"PERF para_splice.insert_xml={swInsert.ElapsedMilliseconds}ms (len={newParaXml.Length})");

                if (!WordOMathFinder.TryLocate(ctx.Doc, ctx.AbsStart, "para_splice",
                    out int newStart, out int newEnd, _log))
                {
                    return InsertResult.Skipped;
                }
                return InsertResult.Ok(newStart, newEnd);
            }
            catch (Exception ex)
            {
                _log("para_splice_error: " + ex.Message);
                return InsertResult.Skipped;
            }
        }

        private static string Preview(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length > 30 ? s.Substring(0, 30) + "..." : s;
        }
    }
}
