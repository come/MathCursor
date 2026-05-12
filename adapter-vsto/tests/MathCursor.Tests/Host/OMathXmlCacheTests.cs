using System;
using MathCursor.Host;
using Xunit;

namespace MathCursor.Tests.Host
{
    /// <summary>
    /// Tests pour <see cref="OMathXmlCache"/> — cache LRU borné LaTeX → XML.
    /// Couche 2/3 perf (ADR <c>2026-05-12-Perf-commit-pipeline-three-stage-stack</c>),
    /// extrait en classe propre par P2.4 du refactor archi.
    /// </summary>
    public sealed class OMathXmlCacheTests
    {
        [Fact]
        public void TryGet_returns_null_on_miss()
        {
            var c = new OMathXmlCache();
            Assert.Null(c.TryGet("x+y"));
        }

        [Fact]
        public void Set_then_TryGet_returns_value()
        {
            var c = new OMathXmlCache();
            c.Set("\\frac{1}{x}", "<m:oMath>FRAC</m:oMath>");
            Assert.Equal("<m:oMath>FRAC</m:oMath>", c.TryGet("\\frac{1}{x}"));
        }

        [Fact]
        public void Set_on_existing_key_updates_value()
        {
            var c = new OMathXmlCache();
            c.Set("x", "OLD");
            c.Set("x", "NEW");
            Assert.Equal("NEW", c.TryGet("x"));
            Assert.Equal(1, c.Count);
        }

        [Fact]
        public void LRU_eviction_when_capacity_exceeded()
        {
            var c = new OMathXmlCache(capacity: 3);
            c.Set("a", "A");
            c.Set("b", "B");
            c.Set("c", "C");
            // Sans access, "a" est la moins récente.
            c.Set("d", "D"); // doit évincer "a"

            Assert.Null(c.TryGet("a"));
            Assert.Equal("B", c.TryGet("b"));
            Assert.Equal("C", c.TryGet("c"));
            Assert.Equal("D", c.TryGet("d"));
        }

        [Fact]
        public void TryGet_touches_LRU_position()
        {
            var c = new OMathXmlCache(capacity: 3);
            c.Set("a", "A");
            c.Set("b", "B");
            c.Set("c", "C");
            // Touch "a" → devient le plus récent. "b" devient le moins récent.
            c.TryGet("a");
            c.Set("d", "D"); // doit évincer "b" (et pas "a")

            Assert.Equal("A", c.TryGet("a"));
            Assert.Null(c.TryGet("b"));
            Assert.Equal("C", c.TryGet("c"));
            Assert.Equal("D", c.TryGet("d"));
        }

        [Fact]
        public void Set_existing_key_also_touches_LRU()
        {
            var c = new OMathXmlCache(capacity: 3);
            c.Set("a", "A");
            c.Set("b", "B");
            c.Set("c", "C");
            // Set("a", ...) doit aussi remettre "a" en tête.
            c.Set("a", "A2");
            c.Set("d", "D"); // évince "b"

            Assert.Equal("A2", c.TryGet("a"));
            Assert.Null(c.TryGet("b"));
        }

        [Fact]
        public void Null_and_empty_inputs_are_ignored()
        {
            var c = new OMathXmlCache();
            c.Set(null, "X");
            c.Set("", "X");
            c.Set("k", null);
            c.Set("k", "");
            Assert.Equal(0, c.Count);
            Assert.Null(c.TryGet(null));
            Assert.Null(c.TryGet(""));
        }

        [Fact]
        public void Clear_empties_cache()
        {
            var c = new OMathXmlCache();
            c.Set("a", "A");
            c.Set("b", "B");
            c.Clear();
            Assert.Equal(0, c.Count);
            Assert.Null(c.TryGet("a"));
        }

        [Fact]
        public void Capacity_must_be_positive()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new OMathXmlCache(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new OMathXmlCache(-1));
        }

        [Fact]
        public void Capacity_1_evicts_on_every_new_insert()
        {
            var c = new OMathXmlCache(capacity: 1);
            c.Set("a", "A");
            c.Set("b", "B");
            Assert.Null(c.TryGet("a"));
            Assert.Equal("B", c.TryGet("b"));
            Assert.Equal(1, c.Count);
        }
    }
}
