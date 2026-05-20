using System.Collections.Generic;
using MathCursor.Core.Resolution;
using MathCursor.Host.Merging;
using MathCursor.Host.Pipeline;
using MathCursor.Host.Pipeline.Stages;
using Xunit;

namespace MathCursor.Tests.Host.Pipeline.Stages
{
    /// <summary>
    /// Tests du <see cref="MergerStage"/> isolé. <see cref="MergerPipeline"/>
    /// alimenté par des <see cref="IZoneMerger"/> stubs pour tester
    /// l'extraction du <see cref="MergeResult"/> en <see cref="CommitContext"/>.
    /// </summary>
    public sealed class MergerStageTests
    {
        private static CommitContext NewCtx(int absStart = 0, int absEnd = 5,
            string source = "AB", string latex = "AB")
            => new CommitContext(absStart, absEnd, source, latex);

        [Fact(DisplayName = "Aucun merger applicable → ctx inchangé")]
        public void No_merger_match_returns_ctx_unchanged()
        {
            var pipeline = new MergerPipeline(new IZoneMerger[]
            {
                new StubMerger("A", null),
            });
            var stage = new MergerStage(pipeline, _ => null);
            var ctx = NewCtx();

            var result = stage.Apply(ctx);
            Assert.Same(ctx, result);
        }

        [Fact(DisplayName = "Merger intra-line absorbe → ctx mis à jour, pas de cross-paragraphe")]
        public void Intra_merger_match_updates_ctx_no_cross_paragraph()
        {
            var sidecar = new ResolutionSidecar(
                new[] { new SpanPin("two-uppercase", 0, 2, 0) },
                new Dictionary<string, IReadOnlyDictionary<int, int>>());
            var merged = new MergeResult
            {
                AbsStart = 0,
                AbsEnd = 10,
                MergedSource = "AB+BC = AC", // espace simple, pas de \n
                RemovedHandles = new List<string> { "h1" },
                MergedSidecar = sidecar,
            };
            var pipeline = new MergerPipeline(new IZoneMerger[]
            {
                new StubMerger("Intra", merged),
            });
            var stage = new MergerStage(pipeline, _ => null);

            var result = stage.Apply(NewCtx(absStart: 5, absEnd: 9));

            Assert.Equal(0, result.AbsStart);
            Assert.Equal(10, result.AbsEnd);
            Assert.Equal("AB+BC = AC", result.Source);
            Assert.Same(sidecar, result.Sidecar);
            Assert.Single(result.RemovedHandles);
            Assert.False(result.WasCrossParagraphMerge);
            Assert.Null(result.CrossMergeMarker);
        }

        [Fact(DisplayName = "Merger cross-line (\\n dans mergedSource) → flags cross-paragraphe + marker extrait")]
        public void Cross_line_merger_match_sets_cross_paragraph_flags()
        {
            var merged = new MergeResult
            {
                AbsStart = 0,
                AbsEnd = 30,
                MergedSource = "AB+BC=CD\n= CH+HD", // \n présent
                RemovedHandles = new List<string> { "h1", "h2" },
                MergedSidecar = ResolutionSidecar.Empty,
            };
            var pipeline = new MergerPipeline(new IZoneMerger[]
            {
                new StubMerger("MarkerChain", merged),
            });
            // extracteur de marker : retourne le 1er marker reconnu (ici "=")
            var stage = new MergerStage(pipeline, mergedSource => "=");

            var result = stage.Apply(NewCtx());

            Assert.True(result.WasCrossParagraphMerge);
            Assert.Equal("=", result.CrossMergeMarker);
            Assert.Equal(2, result.RemovedHandles.Count);
        }

        [Fact(DisplayName = "Source mergée transmise telle quelle au stage suivant (Resolver)")]
        public void Merged_source_propagates_to_next_stage()
        {
            // Simule la chaîne : MergerStage met `Source = "AB+BC = AC"` et
            // `Sidecar = ...`, le ResolverStage suivant doit voir ces valeurs.
            var sidecar = new ResolutionSidecar(
                new[]
                {
                    new SpanPin("two-uppercase", 0, 2, 0),
                    new SpanPin("two-uppercase", 3, 2, 0),
                    new SpanPin("two-uppercase", 8, 2, 0),
                },
                new Dictionary<string, IReadOnlyDictionary<int, int>>());
            var merged = new MergeResult
            {
                AbsStart = 0,
                AbsEnd = 10,
                MergedSource = "AB+BC = AC",
                RemovedHandles = new List<string>(),
                MergedSidecar = sidecar,
            };
            var pipeline = new MergerPipeline(new IZoneMerger[]
            {
                new StubMerger("Intra", merged),
            });
            var mergerStage = new MergerStage(pipeline, _ => null);

            var afterMerge = mergerStage.Apply(NewCtx());

            // Le ResolverStage suivant verra une Source mergée avec sidecar.
            Assert.Equal("AB+BC = AC", afterMerge.Source);
            Assert.Equal(3, afterMerge.Sidecar.SpanPins.Count);
        }

        // ─── helpers ────────────────────────────────────────────────

        private sealed class StubMerger : IZoneMerger
        {
            private readonly MergeResult _result;
            public StubMerger(string name, MergeResult result)
            {
                Name = name;
                _result = result;
            }
            public string Name { get; }
            public MergeResult TryMerge(int absStart, int absEnd, string currentSource)
                => _result;
        }
    }
}
