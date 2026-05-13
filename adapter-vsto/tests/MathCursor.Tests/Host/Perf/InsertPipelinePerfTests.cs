using System;
using System.Diagnostics;
using System.Text;
using MathCursor.Host;
using Xunit;

namespace MathCursor.Tests.Host.Perf
{
    /// <summary>
    /// Budget perf sur les hot paths purs (sans Word interop) du pipeline
    /// d'insertion : <see cref="OMathParaJcPatcher"/> et
    /// <see cref="InlineOMathSplicer"/>.
    ///
    /// <para>Validation user-facing 2026-05-13 : « ca doit pouvoir gérer un
    /// gros doc et un petit doc ». Budget perçu — MathCursor = rapidité de
    /// saisie (cf. memory <c>project_positioning_speed</c>). Si une fonction
    /// XML pure dépasse ~15ms sur un doc de ~100 ¶, c'est un signal de
    /// complexité (Regex catastrophic backtracking, LINQ to XML inefficient,
    /// allocation excessive…).</para>
    ///
    /// <para>Méthodologie : 1 warm-up + 5 itérations mesurées + médiane.
    /// Assert sur la médiane (résistant aux pics JIT/GC du 1er run).</para>
    ///
    /// <para>Limite : ne mesure pas <c>Word.Range.InsertXML</c> ni
    /// <c>BuildUp</c> (interop Word). L'instrumentation
    /// <c>LogDiag("PERF ...")</c> existante reste la source de vérité pour
    /// le runtime Word.</para>
    /// </summary>
    public sealed class InsertPipelinePerfTests
    {
        private const int Iterations = 5;

        // ─── Mesure ──────────────────────────────────────────────────

        /// <summary>1 warm-up + N mesures, retourne la médiane (= time[N/2]
        /// après tri). Robuste au pic JIT/GC du 1er run.</summary>
        private static long MeasureMedianMs(Action op)
        {
            op(); // warm-up
            var times = new long[Iterations];
            for (int i = 0; i < Iterations; i++)
            {
                var sw = Stopwatch.StartNew();
                op();
                sw.Stop();
                times[i] = sw.ElapsedMilliseconds;
            }
            Array.Sort(times);
            return times[Iterations / 2];
        }

        // ─── Fixtures XML ────────────────────────────────────────────

        /// <summary>1 ¶ avec une OMath inline cible. Mime "Soit f"
        /// Ctrl+Espace, état du doc juste avant que le patcher tourne.</summary>
        private static string SmallDocXml()
            => FullDocPkg(ParaWithRunsAndOMath("Soit ", "f"));

        /// <summary>~100 ¶ alternant texte plain et ¶ avec OMath, +1 tableau
        /// 5×5, +1 ¶ cible (queue = "z") pour le splice. Mime un doc
        /// typique d'élève en milieu de prise de notes.</summary>
        private static string BigDocXml()
        {
            var sb = new StringBuilder(16384);
            for (int i = 0; i < 60; i++)
            {
                if (i % 3 == 0) sb.Append(ParaWithRunsAndOMath("Texte ligne " + i + " avec ", "x"));
                else sb.Append(ParaPlain("Paragraphe simple numero " + i + " avec un peu de texte."));
            }
            sb.Append("<w:tbl>");
            for (int r = 0; r < 5; r++)
            {
                sb.Append("<w:tr>");
                for (int c = 0; c < 5; c++)
                    sb.Append("<w:tc><w:p><w:r><w:t>cell ").Append(r).Append(',').Append(c).Append("</w:t></w:r></w:p></w:tc>");
                sb.Append("</w:tr>");
            }
            sb.Append("</w:tbl>");
            // ¶ cible du splice (queue = "z", unique dans le doc)
            sb.Append(ParaPlain("Cible avant le mathsource : z"));
            for (int i = 0; i < 40; i++)
            {
                if (i % 4 == 0) sb.Append(ParaWithRunsAndOMath("Suite " + i + " : ", "y"));
                else sb.Append(ParaPlain("Suite paragraphe " + i + "."));
            }
            return FullDocPkg(sb.ToString());
        }

        // ─── Helpers XML brut ────────────────────────────────────────

        private static string FullDocPkg(string bodyInner)
            => "<?xml version=\"1.0\"?>"
            + "<pkg:package xmlns:pkg=\"http://schemas.microsoft.com/office/2006/xmlPackage\">"
            + "<pkg:part pkg:name=\"/word/document.xml\" pkg:contentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\">"
            + "<pkg:xmlData>"
            + "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\""
            + " xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\">"
            + "<w:body>" + bodyInner + "</w:body>"
            + "</w:document>"
            + "</pkg:xmlData></pkg:part></pkg:package>";

        private static string ParaPlain(string text)
            => "<w:p><w:r><w:t>" + text + "</w:t></w:r></w:p>";

        private static string ParaWithRunsAndOMath(string before, string mathContent)
            => "<w:p><w:r><w:t>" + before + "</w:t></w:r>"
             + "<m:oMath><m:r><m:t>" + mathContent + "</m:t></m:r></m:oMath>"
             + "</w:p>";

        private static string FakeNewOMath(string content)
            => "<m:oMath xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\"><m:r><m:t>"
             + content + "</m:t></m:r></m:oMath>";

        // ─── Tests Patcher ───────────────────────────────────────────

        [Fact(DisplayName = "Patcher EnsureDisplayWithLeftJc small doc — médiane < 3ms")]
        public void Patcher_EnsureDisplay_SmallDoc_UnderBudget()
        {
            string xml = SmallDocXml();
            long medianMs = MeasureMedianMs(() => OMathParaJcPatcher.EnsureDisplayWithLeftJc(xml, out _));
            Assert.True(medianMs < 3,
                $"Patcher SmallDoc médiane {medianMs}ms dépasse budget 3ms — régression perf");
        }

        [Fact(DisplayName = "Patcher EnsureDisplayWithLeftJc big doc — médiane < 15ms")]
        public void Patcher_EnsureDisplay_BigDoc_UnderBudget()
        {
            string xml = BigDocXml();
            long medianMs = MeasureMedianMs(() => OMathParaJcPatcher.EnsureDisplayWithLeftJc(xml, out _));
            Assert.True(medianMs < 15,
                $"Patcher BigDoc médiane {medianMs}ms dépasse budget 15ms — régression perf");
        }

        [Fact(DisplayName = "Patcher Patch (idempotent, left) big doc — médiane < 15ms")]
        public void Patcher_PatchIdempotent_BigDoc_UnderBudget()
        {
            // Pré-wrap pour passer dans le chemin Patch (m:oMathPara présent).
            string xml = OMathParaJcPatcher.EnsureDisplayWithLeftJc(BigDocXml(), out _);
            long medianMs = MeasureMedianMs(() => OMathParaJcPatcher.Patch(xml, "left", out _));
            Assert.True(medianMs < 15,
                $"Patcher.Patch BigDoc médiane {medianMs}ms dépasse budget 15ms");
        }

        // ─── Tests Splice ────────────────────────────────────────────

        [Fact(DisplayName = "InlineOMathSplicer splice small doc — médiane < 5ms")]
        public void Splice_SmallDoc_UnderBudget()
        {
            string xml = SmallDocXml();
            string newOM = FakeNewOMath("f");
            long medianMs = MeasureMedianMs(() =>
                InlineOMathSplicer.SpliceOMathInDocXml(xml, "f", newOM));
            Assert.True(medianMs < 5,
                $"Splice SmallDoc médiane {medianMs}ms dépasse budget 5ms");
        }

        [Fact(DisplayName = "InlineOMathSplicer splice big doc — médiane < 25ms")]
        public void Splice_BigDoc_UnderBudget()
        {
            string xml = BigDocXml();
            string newOM = FakeNewOMath("z");
            long medianMs = MeasureMedianMs(() =>
                InlineOMathSplicer.SpliceOMathInDocXml(xml, "z", newOM));
            Assert.True(medianMs < 25,
                $"Splice BigDoc médiane {medianMs}ms dépasse budget 25ms");
        }

        // ─── Pipeline total ──────────────────────────────────────────

        [Fact(DisplayName = "Pipeline pur (splice + patch) big doc — médiane < 40ms")]
        public void Pipeline_Total_BigDoc_UnderBudget()
        {
            string xml = BigDocXml();
            string newOM = FakeNewOMath("z");
            long medianMs = MeasureMedianMs(() =>
            {
                string spliced = InlineOMathSplicer.SpliceOMathInDocXml(xml, "z", newOM);
                if (spliced != null) OMathParaJcPatcher.EnsureDisplayWithLeftJc(spliced, out _);
            });
            Assert.True(medianMs < 40,
                $"Pipeline BigDoc médiane {medianMs}ms dépasse budget 40ms — régression perf");
        }
    }
}
