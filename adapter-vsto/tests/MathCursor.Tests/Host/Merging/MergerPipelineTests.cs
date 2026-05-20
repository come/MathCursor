using System.Collections.Generic;
using MathCursor.Core.Resolution;
using MathCursor.Host.Merging;
using Xunit;

namespace MathCursor.Tests.Host.Merging
{
    /// <summary>
    /// Tests d'orchestration du <see cref="MergerPipeline"/> — ne dépend pas
    /// de Word, on injecte des <see cref="IZoneMerger"/> mocks via
    /// <see cref="StubMerger"/>. Cf. ADR <c>2026-05-06-Meta-zone-merger-pipeline</c>.
    /// </summary>
    public sealed class MergerPipelineTests
    {
        // ─── Orchestration de base ──────────────────────────────────────

        [Fact(DisplayName = "Aucun merger applicable → retourne null")]
        public void All_mergers_null_returns_null()
        {
            var pipeline = new MergerPipeline(new IZoneMerger[]
            {
                new StubMerger("A", null),
                new StubMerger("B", null),
            });

            var r = pipeline.Run(0, 5, "src");
            Assert.Null(r);
        }

        [Fact(DisplayName = "Premier merger match → retourné, suivants pas appelés")]
        public void First_match_wins_short_circuits_pipeline()
        {
            var matched = new MergeResult { AbsStart = 0, AbsEnd = 5, MergedSource = "matched" };
            var laterCalled = false;
            var pipeline = new MergerPipeline(new IZoneMerger[]
            {
                new StubMerger("First", matched),
                new StubMerger("Later", null, onCalled: () => laterCalled = true),
            });

            var r = pipeline.Run(0, 5, "src");

            Assert.Same(matched, r);
            Assert.False(laterCalled, "le merger postérieur ne doit pas être appelé après un match");
        }

        [Fact(DisplayName = "2e merger match si 1er null → 2e retourné")]
        public void Second_match_when_first_returns_null()
        {
            var matched = new MergeResult { MergedSource = "from-second" };
            var pipeline = new MergerPipeline(new IZoneMerger[]
            {
                new StubMerger("A", null),
                new StubMerger("B", matched),
                new StubMerger("C", null),
            });

            var r = pipeline.Run(0, 5, "src");
            Assert.Same(matched, r);
        }

        [Fact(DisplayName = "Merger qui throw → ignoré, pipeline continue")]
        public void Throwing_merger_is_skipped_pipeline_continues()
        {
            var matched = new MergeResult { MergedSource = "fallback" };
            var logs = new List<string>();
            var pipeline = new MergerPipeline(new IZoneMerger[]
            {
                new StubMerger("Crash", null, throwException: true),
                new StubMerger("Fallback", matched),
            }, log: logs.Add);

            var r = pipeline.Run(0, 5, "src");

            Assert.Same(matched, r);
            Assert.Contains(logs, l => l.Contains("merger_error: Crash"));
        }

        // ─── Contrat sidecar (cœur du bug 06-05) ────────────────────────

        [Fact(DisplayName = "Contrat sidecar : merger absorbe handles sans MergedSidecar → log WARN")]
        public void Merger_absorbs_handles_without_sidecar_emits_warn()
        {
            // Reproduction du pattern bug 06-05 : un merger dit "j'ai absorbé
            // 2 OMaths" mais oublie de calculer MergedSidecar. Le pipeline doit
            // détecter cette violation de contrat et alerter dans les logs.
            var brokenResult = new MergeResult
            {
                MergedSource = "AB+BC=CD",
                RemovedHandles = new List<string> { "h1", "h2" },
                MergedSidecar = ResolutionSidecar.Empty, // ← le contrat est violé ici
            };
            var logs = new List<string>();
            var pipeline = new MergerPipeline(
                new IZoneMerger[] { new StubMerger("Forgetful", brokenResult) },
                log: logs.Add);

            var r = pipeline.Run(0, 5, "src");

            Assert.NotNull(r); // le résultat est tout de même renvoyé (pas un crash)
            Assert.Contains(logs, l => l.Contains("merger_warn: Forgetful")
                                       && l.Contains("absorbé 2"));
        }

        [Fact(DisplayName = "Contrat sidecar respecté : pas de WARN si MergedSidecar non-vide")]
        public void Merger_with_handles_and_sidecar_does_not_warn()
        {
            var goodResult = new MergeResult
            {
                MergedSource = "AB+BC",
                RemovedHandles = new List<string> { "h1" },
                MergedSidecar = new ResolutionSidecar(
                    new[] { new SpanPin("two-uppercase", 0, 2, 0) },
                    new Dictionary<string, IReadOnlyDictionary<int, int>>()),
            };
            var logs = new List<string>();
            var pipeline = new MergerPipeline(
                new IZoneMerger[] { new StubMerger("Compliant", goodResult) },
                log: logs.Add);

            pipeline.Run(0, 5, "src");

            Assert.DoesNotContain(logs, l => l.Contains("merger_warn"));
        }

        [Fact(DisplayName = "Pas de handles absorbés → MergedSidecar vide acceptable, pas de WARN")]
        public void Merger_without_absorbed_handles_does_not_require_sidecar()
        {
            // Cas reverted-zone Mode 2 : pas d'OMath absorbé (RemovedHandles
            // empty), source mergée seulement. Sidecar empty est légitime ici.
            var legitResult = new MergeResult
            {
                MergedSource = "line1\nline2",
                RemovedHandles = new List<string>(), // empty
                MergedSidecar = ResolutionSidecar.Empty,
            };
            var logs = new List<string>();
            var pipeline = new MergerPipeline(
                new IZoneMerger[] { new StubMerger("NoAbsorption", legitResult) },
                log: logs.Add);

            pipeline.Run(0, 5, "src");

            Assert.DoesNotContain(logs, l => l.Contains("merger_warn"));
        }

        // ─── helper : stub IZoneMerger ──────────────────────────────────

        private sealed class StubMerger : IZoneMerger
        {
            private readonly MergeResult _toReturn;
            private readonly System.Action _onCalled;
            private readonly bool _throw;
            public StubMerger(string name, MergeResult toReturn,
                System.Action onCalled = null, bool throwException = false)
            {
                Name = name;
                _toReturn = toReturn;
                _onCalled = onCalled;
                _throw = throwException;
            }
            public string Name { get; }
            public MergeResult TryMerge(int absStart, int absEnd, string currentSource)
            {
                _onCalled?.Invoke();
                if (_throw) throw new System.InvalidOperationException("boom");
                return _toReturn;
            }
        }
    }
}
