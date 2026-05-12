using System.Collections.Generic;
using MathCursor.Host;
using Xunit;

namespace MathCursor.Tests.Host
{
    /// <summary>
    /// Tests pour <see cref="ParaXmlPrefetcher"/> — cache pré-fetch idle du
    /// paraXml courant. P2.5 du refactor archi (ADR
    /// <c>2026-05-12-Refactor-pure-merger-atomic-insert</c>).
    /// </summary>
    public sealed class ParaXmlPrefetcherTests
    {
        /// <summary>Fake source que les tests pilotent à la main.</summary>
        private sealed class FakeSource : IParaXmlSource
        {
            public int ParaStart = 0;
            public string ParaText = "";
            public string Xml = "<pkg>FAKE_XML</pkg>";
            public bool CanReadParagraph = true;
            public int ReadCount; // combien de fois ReadCurrentParaXml a été appelé

            public bool TryReadCurrentParagraph(out int paraStart, out string paraText)
            {
                paraStart = ParaStart;
                paraText = ParaText;
                return CanReadParagraph;
            }

            public string ReadCurrentParaXml()
            {
                ReadCount++;
                return Xml;
            }
        }

        [Fact]
        public void First_tick_does_nothing_yet_only_records_text()
        {
            var src = new FakeSource { ParaStart = 10, ParaText = "hello" };
            var p = new ParaXmlPrefetcher(src);

            p.Tick();

            // 1er tick : on note _lastSeenText mais pas de fetch (pas idle yet).
            Assert.Equal(0, src.ReadCount);
            Assert.Null(p.TryGet(10, "hello"));
        }

        [Fact]
        public void Second_tick_same_text_fetches()
        {
            var src = new FakeSource { ParaStart = 10, ParaText = "hello" };
            var p = new ParaXmlPrefetcher(src);

            p.Tick(); // 1er : note
            p.Tick(); // 2e : stable → fetch

            Assert.Equal(1, src.ReadCount);
            Assert.Equal("<pkg>FAKE_XML</pkg>", p.TryGet(10, "hello"));
        }

        [Fact]
        public void Text_change_between_ticks_does_not_fetch()
        {
            var src = new FakeSource { ParaStart = 10, ParaText = "hello" };
            var p = new ParaXmlPrefetcher(src);

            p.Tick();
            src.ParaText = "hello world"; // user a tapé entre les ticks
            p.Tick();

            // Pas idle → pas de fetch.
            Assert.Equal(0, src.ReadCount);
        }

        [Fact]
        public void Already_cached_text_skips_refresh()
        {
            var src = new FakeSource { ParaStart = 10, ParaText = "hello" };
            var p = new ParaXmlPrefetcher(src);

            p.Tick(); p.Tick(); // fetch initial
            Assert.Equal(1, src.ReadCount);

            // 3e et 4e ticks : cache déjà à jour, pas de refetch.
            p.Tick();
            p.Tick();
            Assert.Equal(1, src.ReadCount);
        }

        [Fact]
        public void Para_change_triggers_refresh_after_stabilization()
        {
            var src = new FakeSource { ParaStart = 10, ParaText = "para1" };
            var p = new ParaXmlPrefetcher(src);

            p.Tick(); p.Tick(); // fetch para1
            Assert.Equal(1, src.ReadCount);

            // User passe à un autre ¶.
            src.ParaStart = 50;
            src.ParaText = "para2";
            src.Xml = "<pkg>XML2</pkg>";

            p.Tick(); // 1er tick sur para2 → note
            Assert.Equal(1, src.ReadCount);
            p.Tick(); // 2e tick stable → refetch
            Assert.Equal(2, src.ReadCount);
            Assert.Equal("<pkg>XML2</pkg>", p.TryGet(50, "para2"));
        }

        [Fact]
        public void TryGet_returns_null_when_paraStart_mismatches()
        {
            var src = new FakeSource { ParaStart = 10, ParaText = "x" };
            var p = new ParaXmlPrefetcher(src);
            p.Tick(); p.Tick();

            Assert.NotNull(p.TryGet(10, "x"));
            Assert.Null(p.TryGet(99, "x"));   // mauvais paraStart
        }

        [Fact]
        public void TryGet_returns_null_when_paraText_mismatches()
        {
            var src = new FakeSource { ParaStart = 10, ParaText = "x" };
            var p = new ParaXmlPrefetcher(src);
            p.Tick(); p.Tick();

            Assert.NotNull(p.TryGet(10, "x"));
            Assert.Null(p.TryGet(10, "different"));
        }

        [Fact]
        public void Tick_handles_source_failure_silently()
        {
            var src = new FakeSource { CanReadParagraph = false };
            var p = new ParaXmlPrefetcher(src);

            p.Tick(); // source rate → no-op silencieux
            p.Tick();

            Assert.Equal(0, src.ReadCount);
            Assert.Null(p.TryGet(0, ""));
        }

        [Fact]
        public void Tick_skips_when_ReadCurrentParaXml_returns_null()
        {
            var src = new FakeSource { ParaStart = 10, ParaText = "x", Xml = null };
            var p = new ParaXmlPrefetcher(src);

            p.Tick(); p.Tick();

            Assert.Equal(1, src.ReadCount); // tentative comptée
            Assert.Null(p.TryGet(10, "x")); // mais pas caché
        }

        [Fact]
        public void Invalidate_clears_cache()
        {
            var src = new FakeSource { ParaStart = 10, ParaText = "x" };
            var p = new ParaXmlPrefetcher(src);
            p.Tick(); p.Tick();
            Assert.NotNull(p.TryGet(10, "x"));

            p.Invalidate();
            Assert.Null(p.TryGet(10, "x"));
        }

        [Fact]
        public void Diag_log_called_on_refresh()
        {
            var src = new FakeSource { ParaStart = 10, ParaText = "x" };
            var logs = new List<string>();
            var p = new ParaXmlPrefetcher(src, logs.Add);

            p.Tick(); p.Tick();

            Assert.Single(logs);
            Assert.Contains("prefetch.read_para_xml", logs[0]);
        }
    }
}
