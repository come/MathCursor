using System;
using System.Collections.Generic;
using Xunit;
using MathCursor.Core.Patterns;
using MathCursor.Core.Lattice.Ast;

namespace MathCursor.Core.Tests.Patterns
{
    /// <summary>
    /// Sanity tests sur <see cref="PatternPipeline"/>. Au stade P2, aucun
    /// template n'est encore inscrit — on vérifie juste que la pipeline
    /// tourne à vide sans NPE et que les invariants de robustesse sont
    /// en place (null check, ctx vide). Étape P2 (ADR
    /// <c>2026-05-21-Meta-pattern-templates-vs-ambig-closed</c>).
    /// </summary>
    public class PatternPipelineSanityTests
    {
        private static PatternScanContext EmptyCtx() =>
            new PatternScanContext(
                topAst: null,
                topLatex: "x",
                source: "x",
                caretOffset: null);

        [Fact]
        public void Ctor_with_null_templates_throws()
        {
            Assert.Throws<ArgumentNullException>(() => new PatternPipeline(null!));
        }

        [Fact]
        public void Run_with_zero_templates_returns_empty_no_npe()
        {
            var pipeline = new PatternPipeline(Array.Empty<IPatternTemplate>());
            var r = pipeline.Run(EmptyCtx());
            Assert.NotNull(r);
            Assert.Empty(r);
        }

        [Fact]
        public void Run_with_null_ctx_throws()
        {
            var pipeline = new PatternPipeline(Array.Empty<IPatternTemplate>());
            Assert.Throws<ArgumentNullException>(() => pipeline.Run(null!));
        }

        [Fact]
        public void Run_skips_template_when_TryMatchHead_returns_null()
        {
            // Template no-op : aucun head ne matche jamais.
            var pipeline = new PatternPipeline(new[] { new NoopTemplate() });
            var r = pipeline.Run(EmptyCtx());
            Assert.Empty(r);
        }

        [Fact]
        public void Run_collects_completions_from_matching_template()
        {
            var pipeline = new PatternPipeline(new[] { new AlwaysMatchTemplate() });
            var r = pipeline.Run(EmptyCtx());
            Assert.Single(r);
            Assert.Equal("stub", r[0].Description);
        }

        [Fact]
        public void Templates_are_ordered_by_Order_property()
        {
            // 2 templates, Order=10 et Order=5. Order=5 doit tourner en premier
            // (= sa complétion apparaît avant celle d'Order=10 dans le résultat).
            var pipeline = new PatternPipeline(new IPatternTemplate[]
            {
                new AlwaysMatchTemplate(templateId: "late", order: 10, description: "late"),
                new AlwaysMatchTemplate(templateId: "early", order: 5, description: "early"),
            });
            var r = pipeline.Run(EmptyCtx());
            Assert.Equal(2, r.Count);
            Assert.Equal("early", r[0].Description);
            Assert.Equal("late", r[1].Description);
        }

        // ─── Helpers ──────────────────────────────────────────────────

        private sealed class NoopTemplate : IPatternTemplate
        {
            public string TemplateId => "noop";
            public int Order => 0;
            public PatternMatch? TryMatchHead(PatternScanContext ctx) => null;
            public IReadOnlyList<PatternCompletion> Expand(PatternMatch state, PatternScanContext ctx)
                => Array.Empty<PatternCompletion>();
        }

        private sealed class AlwaysMatchTemplate : IPatternTemplate
        {
            private readonly string _description;
            public AlwaysMatchTemplate(string templateId = "always", int order = 0,
                string description = "stub")
            {
                TemplateId = templateId;
                Order = order;
                _description = description;
            }
            public string TemplateId { get; }
            public int Order { get; }
            public PatternMatch? TryMatchHead(PatternScanContext ctx)
                => new PatternMatch(TemplateId, 0, 0,
                    new Dictionary<string, SlotValue>(), isComplete: true);
            public IReadOnlyList<PatternCompletion> Expand(PatternMatch state, PatternScanContext ctx)
                => new[] { new PatternCompletion(_description, "", "", null, 100) };
        }
    }
}
