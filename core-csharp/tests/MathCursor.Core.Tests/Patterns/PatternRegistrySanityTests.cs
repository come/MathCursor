using System;
using System.Collections.Generic;
using Xunit;
using MathCursor.Core.Patterns;

namespace MathCursor.Core.Tests.Patterns
{
    /// <summary>
    /// Sanity tests sur <see cref="PatternRegistry"/>. Vérifie lookup,
    /// rejection des duplicates, null safety. Étape P2 (ADR
    /// <c>2026-05-21-Meta-pattern-templates-vs-ambig-closed</c>).
    /// </summary>
    public class PatternRegistrySanityTests
    {
        [Fact]
        public void Ctor_with_null_templates_throws()
        {
            Assert.Throws<ArgumentNullException>(() => new PatternRegistry(null!));
        }

        [Fact]
        public void Empty_registry_count_is_zero()
        {
            var reg = new PatternRegistry(Array.Empty<IPatternTemplate>());
            Assert.Equal(0, reg.Count);
        }

        [Fact]
        public void Get_unknown_id_returns_null()
        {
            var reg = new PatternRegistry(Array.Empty<IPatternTemplate>());
            Assert.Null(reg.Get("missing"));
        }

        [Fact]
        public void Get_null_or_empty_id_returns_null()
        {
            var reg = new PatternRegistry(new[] { new StubTemplate("a") });
            Assert.Null(reg.Get(null!));
            Assert.Null(reg.Get(""));
        }

        [Fact]
        public void Get_existing_id_returns_template()
        {
            var t = new StubTemplate("forall-belongs");
            var reg = new PatternRegistry(new[] { t });
            Assert.Same(t, reg.Get("forall-belongs"));
        }

        [Fact]
        public void TryGet_existing_returns_true_and_template()
        {
            var t = new StubTemplate("ensemble");
            var reg = new PatternRegistry(new[] { t });
            Assert.True(reg.TryGet("ensemble", out var found));
            Assert.Same(t, found);
        }

        [Fact]
        public void TryGet_missing_returns_false_and_null()
        {
            var reg = new PatternRegistry(Array.Empty<IPatternTemplate>());
            Assert.False(reg.TryGet("missing", out var found));
            Assert.Null(found);
        }

        [Fact]
        public void Duplicate_templateId_throws_ArgumentException()
        {
            var a = new StubTemplate("dup");
            var b = new StubTemplate("dup");
            var ex = Assert.Throws<ArgumentException>(() => new PatternRegistry(new[] { a, b }));
            Assert.Contains("dup", ex.Message);
        }

        [Fact]
        public void Null_template_entries_are_skipped()
        {
            var t = new StubTemplate("real");
            var reg = new PatternRegistry(new IPatternTemplate?[] { null, t, null }!);
            Assert.Equal(1, reg.Count);
            Assert.Same(t, reg.Get("real"));
        }

        [Fact]
        public void Count_reflects_registered_templates()
        {
            var reg = new PatternRegistry(new[]
            {
                new StubTemplate("a"),
                new StubTemplate("b"),
                new StubTemplate("c"),
            });
            Assert.Equal(3, reg.Count);
        }

        // ─── Helper ───────────────────────────────────────────────────

        private sealed class StubTemplate : IPatternTemplate
        {
            public StubTemplate(string id) { TemplateId = id; }
            public string TemplateId { get; }
            public int Order => 0;
            public PatternMatch? TryMatchHead(PatternScanContext ctx) => null;
            public IReadOnlyList<PatternCompletion> Expand(PatternMatch state, PatternScanContext ctx)
                => Array.Empty<PatternCompletion>();
        }
    }
}
