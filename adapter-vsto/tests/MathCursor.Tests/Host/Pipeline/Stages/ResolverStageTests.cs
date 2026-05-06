using System.Collections.Generic;
using MathCursor.Core;
using MathCursor.Core.Lattice;
using MathCursor.Core.Resolution;
using MathCursor.Host.Pipeline;
using MathCursor.Host.Pipeline.Stages;
using Xunit;

namespace MathCursor.Tests.Host.Pipeline.Stages
{
    /// <summary>
    /// Tests du <see cref="ResolverStage"/> isolé. ZoneResolver réel
    /// (logique pure du core, pas besoin de mock).
    /// </summary>
    public sealed class ResolverStageTests
    {
        private static ResolverStage MakeStage()
            => new ResolverStage(new ZoneResolver(new LatticeEngine()));

        [Fact(DisplayName = "Source vide → ctx inchangé (no-op)")]
        public void Empty_source_returns_ctx_unchanged()
        {
            var stage = MakeStage();
            var ctx = new CommitContext(0, 0, source: "", latex: "");

            var result = stage.Apply(ctx);
            Assert.Same(ctx, result);
        }

        [Fact(DisplayName = "Source `AB+BC` sans sidecar → Latex peuplé sans \\vec (default)")]
        public void Resolves_without_sidecar_produces_default_latex()
        {
            var stage = MakeStage();
            var ctx = new CommitContext(0, 5, source: "AB+BC", latex: "");

            var result = stage.Apply(ctx);

            Assert.NotEqual(ctx, result);
            Assert.NotEmpty(result.Latex);
            Assert.DoesNotContain("\\vec", result.Latex);
        }

        [Fact(DisplayName = "Source `AB` + sidecar pin AB→vec → Latex contient \\vec{AB}")]
        public void Resolves_with_sidecar_pin_applies_vec()
        {
            var stage = MakeStage();
            var sidecar = new ResolutionSidecar(
                new[] { new SpanPin(AlternativeGenerator.RuleTwoUppercase, 0, 2, 0) },
                new Dictionary<string, IReadOnlyDictionary<int, int>>());
            var ctx = new CommitContext(0, 2, source: "AB", latex: "", sidecar: sidecar);

            var result = stage.Apply(ctx);

            Assert.Contains("\\vec{AB}", result.Latex);
        }

        [Fact(DisplayName = "Stage immutable : ctx d'entrée inchangé")]
        public void Stage_does_not_mutate_input_ctx()
        {
            var stage = MakeStage();
            var ctx = new CommitContext(0, 5, source: "AB+BC", latex: "original");

            stage.Apply(ctx);

            Assert.Equal("original", ctx.Latex); // pas modifié
        }
    }
}
