using System;
using System.Diagnostics;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.Inserters
{
    /// <summary>
    /// Stratégie d'insertion atomique via <c>Range.InsertXML</c> sur
    /// [absStart, absEnd]. Word remplace toute la range (avec OMaths
    /// absorbées au passage) par le bloc capturé en une seule transaction.
    /// Sur abort → doc intact. Cf. ADR
    /// <c>2026-05-12-Refactor-pure-merger-atomic-insert</c>.
    ///
    /// <para>Catch-all : couvre display math (align*/cases), cross-paragraph,
    /// et tout cas où le fast path et le splice ont skip ou fail.</para>
    /// </summary>
    internal sealed class AtomicRangeInserter
    {
        private readonly OMathStagingService _staging;
        private readonly Action<string> _log;

        public AtomicRangeInserter(OMathStagingService staging, Action<string> log)
        {
            _staging = staging ?? throw new ArgumentNullException(nameof(staging));
            _log = log ?? (s => { });
        }

        public string Name => "atomic_insert";

        public InsertResult TryInsert(InsertContext ctx)
        {
            try
            {
                var swBuild = Stopwatch.StartNew();
                string capturedXml = _staging.BuildOMathXml(ctx.Latex);
                swBuild.Stop();
                _log($"PERF atomic_insert.build_isolated={swBuild.ElapsedMilliseconds}ms");
                if (string.IsNullOrEmpty(capturedXml))
                {
                    _log("atomic_insert: build returned null");
                    return InsertResult.Skipped;
                }

                try { capturedXml = OMathParaJcPatcher.EnsureDisplayWithLeftJc(capturedXml, out _); }
                catch (Exception ex) { _log("atomic_insert_ensure_error: " + ex.Message); }

                var swInsert = Stopwatch.StartNew();
                ctx.Doc.Range(ctx.AbsStart, ctx.AbsEnd).InsertXML(capturedXml);
                swInsert.Stop();
                _log($"PERF atomic_insert.range_insertxml={swInsert.ElapsedMilliseconds}ms (range=[{ctx.AbsStart},{ctx.AbsEnd}] len={capturedXml.Length})");

                if (!WordOMathFinder.TryLocate(ctx.Doc, ctx.AbsStart, "atomic_insert",
                    out int newStart, out int newEnd, _log))
                {
                    return InsertResult.Skipped;
                }
                return InsertResult.Ok(newStart, newEnd);
            }
            catch (Exception ex)
            {
                _log("atomic_insert_error: " + ex.Message);
                return InsertResult.Skipped;
            }
        }
    }
}
