using System.Collections.Generic;
using MathCursor.Core.Resolution;
using MathCursor.Host.Pipeline;
using MathCursor.HostContract;
using Xunit;

namespace MathCursor.Tests.Host.Pipeline
{
    /// <summary>
    /// Tests immutabilité + With* de <see cref="CommitContext"/>. Cf. ADR
    /// <c>2026-05-06-Meta-l4-pipeline-and-session</c>.
    /// </summary>
    public sealed class CommitContextTests
    {
        [Fact(DisplayName = "Constructor minimal : valeurs par défaut sûres (pas de null)")]
        public void Constructor_minimal_provides_safe_defaults()
        {
            var ctx = new CommitContext(
                absStart: 0, absEnd: 5,
                source: "AB", latex: "AB");

            Assert.Equal(0, ctx.AbsStart);
            Assert.Equal(5, ctx.AbsEnd);
            Assert.Equal("AB", ctx.Source);
            Assert.Equal("AB", ctx.Latex);
            Assert.Same(ResolutionSidecar.Empty, ctx.Sidecar);
            Assert.NotNull(ctx.RemovedHandles);
            Assert.Empty(ctx.RemovedHandles);
            Assert.Null(ctx.NewHandle);
            Assert.Null(ctx.EditingHandle);
            Assert.False(ctx.WasCrossParagraphMerge);
            Assert.Null(ctx.CrossMergeMarker);
        }

        [Fact(DisplayName = "Source/Latex null → string.Empty (jamais null)")]
        public void Null_strings_normalize_to_empty()
        {
            var ctx = new CommitContext(0, 0, source: null, latex: null);
            Assert.Equal(string.Empty, ctx.Source);
            Assert.Equal(string.Empty, ctx.Latex);
        }

        [Fact(DisplayName = "WithMergeResult : nouveau context, original inchangé (immutable)")]
        public void WithMergeResult_returns_new_context_originals_unchanged()
        {
            var original = new CommitContext(5, 10, "AB", "AB");
            var sidecar = new ResolutionSidecar(
                new[] { new SpanPin("two-uppercase", 0, 2, 0) },
                new Dictionary<string, IReadOnlyDictionary<int, int>>());

            var merged = original.WithMergeResult(
                absStart: 0, absEnd: 15,
                mergedSource: "AB+BC = AC",
                mergedSidecar: sidecar,
                removedHandles: new[] { "h1", "h2" },
                wasCrossParagraphMerge: false,
                crossMergeMarker: null);

            // Original inchangé
            Assert.Equal(5, original.AbsStart);
            Assert.Equal("AB", original.Source);
            Assert.Same(ResolutionSidecar.Empty, original.Sidecar);
            Assert.Empty(original.RemovedHandles);

            // Nouveau context avec les valeurs de merge
            Assert.Equal(0, merged.AbsStart);
            Assert.Equal(15, merged.AbsEnd);
            Assert.Equal("AB+BC = AC", merged.Source);
            Assert.Same(sidecar, merged.Sidecar);
            Assert.Equal(2, merged.RemovedHandles.Count);
            // Latex inchangé (pas dans le scope d'un merge)
            Assert.Equal("AB", merged.Latex);
        }

        [Fact(DisplayName = "WithLatex : seul Latex change, le reste préservé")]
        public void WithLatex_preserves_other_fields()
        {
            var sidecar = new ResolutionSidecar(
                new[] { new SpanPin("two-uppercase", 0, 2, 0) },
                new Dictionary<string, IReadOnlyDictionary<int, int>>());
            var original = new CommitContext(
                absStart: 5, absEnd: 10, source: "AB", latex: "AB",
                sidecar: sidecar,
                removedHandles: new[] { "h1" });

            var rendered = original.WithLatex("\\vec{AB}");

            Assert.Equal("\\vec{AB}", rendered.Latex);
            Assert.Equal(5, rendered.AbsStart);
            Assert.Equal("AB", rendered.Source);
            Assert.Same(sidecar, rendered.Sidecar);
            Assert.Single(rendered.RemovedHandles);
        }

        [Fact(DisplayName = "WithNewHandle : seul NewHandle change")]
        public void WithNewHandle_preserves_other_fields()
        {
            var original = new CommitContext(0, 5, "AB", "\\vec{AB}");
            var inserted = original.WithNewHandle(new EquationHandle("eq-new"));

            Assert.NotNull(inserted.NewHandle);
            Assert.Equal("eq-new", inserted.NewHandle.Id);
            Assert.Null(original.NewHandle); // immutable
            Assert.Equal("\\vec{AB}", inserted.Latex);
        }

        [Fact(DisplayName = "WasCrossParagraphMerge + marker propagés au merge")]
        public void Cross_paragraph_merge_metadata_propagated()
        {
            var original = new CommitContext(0, 0, "", "");
            var merged = original.WithMergeResult(
                absStart: 0, absEnd: 30,
                mergedSource: "AB+BC=CD\n= CH+HD",
                mergedSidecar: ResolutionSidecar.Empty,
                removedHandles: new[] { "h1" },
                wasCrossParagraphMerge: true,
                crossMergeMarker: "=");

            Assert.True(merged.WasCrossParagraphMerge);
            Assert.Equal("=", merged.CrossMergeMarker);
        }
    }
}
