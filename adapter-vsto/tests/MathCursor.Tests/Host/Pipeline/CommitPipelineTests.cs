using System.Collections.Generic;
using MathCursor.Host.Pipeline;
using Xunit;

namespace MathCursor.Tests.Host.Pipeline
{
    /// <summary>
    /// Tests d'orchestration de <see cref="CommitPipeline"/>. Stages
    /// remplacés par des stubs pour isoler la logique d'itération.
    /// </summary>
    public sealed class CommitPipelineTests
    {
        private static CommitContext NewCtx(int absStart = 0, int absEnd = 5,
            string source = "AB", string latex = "AB")
            => new CommitContext(absStart, absEnd, source, latex);

        [Fact(DisplayName = "Pipeline vide → retourne le ctx initial inchangé")]
        public void Empty_pipeline_returns_initial_context()
        {
            var pipeline = new CommitPipeline(new ICommitStage[0]);
            var initial = NewCtx();

            var result = pipeline.Run(initial);
            Assert.Same(initial, result);
        }

        [Fact(DisplayName = "Stages appelés dans l'ordre, ctx propagé entre eux")]
        public void Stages_run_in_order_propagating_context()
        {
            var calls = new List<string>();
            var stages = new ICommitStage[]
            {
                new StubStage("A", ctx =>
                {
                    calls.Add("A");
                    return ctx.WithLatex("after-A");
                }),
                new StubStage("B", ctx =>
                {
                    calls.Add("B(saw=" + ctx.Latex + ")");
                    return ctx.WithLatex("after-B");
                }),
            };
            var pipeline = new CommitPipeline(stages);

            var result = pipeline.Run(NewCtx());

            Assert.Equal(new[] { "A", "B(saw=after-A)" }, calls);
            Assert.Equal("after-B", result.Latex);
        }

        [Fact(DisplayName = "Stage qui retourne null → ctx précédent préservé (fallback safe)")]
        public void Stage_returning_null_falls_back_to_previous_context()
        {
            var stages = new ICommitStage[]
            {
                new StubStage("A", ctx => ctx.WithLatex("set-by-A")),
                new StubStage("B", ctx => null),
                new StubStage("C", ctx => ctx.WithLatex(ctx.Latex + "+C")),
            };
            var pipeline = new CommitPipeline(stages);

            var result = pipeline.Run(NewCtx());

            // B retournant null ne casse pas la chaîne — C voit le ctx d'A
            Assert.Equal("set-by-A+C", result.Latex);
        }

        [Fact(DisplayName = "Pipeline.Run(null) → ArgumentNullException")]
        public void Run_with_null_initial_throws()
        {
            var pipeline = new CommitPipeline(new ICommitStage[0]);
            Assert.Throws<System.ArgumentNullException>(() => pipeline.Run(null));
        }

        [Fact(DisplayName = "Stage qui set IsAborted → stages suivants skippés (rollback safe)")]
        public void Aborted_stage_short_circuits_remaining_stages()
        {
            var calls = new List<string>();
            var stages = new ICommitStage[]
            {
                new StubStage("Insert", ctx =>
                {
                    calls.Add("Insert");
                    return ctx.WithAbort(); // simulate insertion failure
                }),
                new StubStage("Store", ctx =>
                {
                    calls.Add("Store"); // ne doit PAS être appelé
                    return ctx;
                }),
                new StubStage("Layout", ctx =>
                {
                    calls.Add("Layout"); // ne doit PAS être appelé
                    return ctx;
                }),
            };
            var pipeline = new CommitPipeline(stages);

            var result = pipeline.Run(NewCtx());

            Assert.True(result.IsAborted);
            Assert.Equal(new[] { "Insert" }, calls); // seulement Insert
        }

        // ─── stub helper ────────────────────────────────────────────

        private sealed class StubStage : ICommitStage
        {
            private readonly System.Func<CommitContext, CommitContext> _impl;
            public StubStage(string name, System.Func<CommitContext, CommitContext> impl)
            {
                Name = name;
                _impl = impl;
            }
            public string Name { get; }
            public CommitContext Apply(CommitContext ctx) => _impl(ctx);
        }
    }
}
