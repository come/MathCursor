using System.Collections.Generic;
using MathCursor.Core.Resolution;
using MathCursor.Core.Resolution.Signals;
using Xunit;

namespace MathCursor.Core.Tests.Resolution
{
    public class SidecarSignalTests
    {
        private static ContextSnapshot SnapshotWithSidecar(ResolutionSidecar sidecar)
            => new ContextSnapshot(rawSource: "x", sidecar: sidecar);

        [Fact]
        public void Empty_sidecar_produces_no_deltas()
        {
            var sig = new SidecarSignal();
            var deltas = sig.Score(SnapshotWithSidecar(ResolutionSidecar.Empty));
            Assert.Empty(deltas);
        }

        [Fact]
        public void Null_snapshot_produces_no_deltas()
        {
            var sig = new SidecarSignal();
            var deltas = sig.Score(null);
            Assert.Empty(deltas);
        }

        [Fact]
        public void Single_vote_produces_proportional_delta()
        {
            // 1 vote pour two-uppercase:0 (vec).
            var votes = new Dictionary<string, IReadOnlyDictionary<int, int>>
            {
                { "two-uppercase", new Dictionary<int, int> { { 0, 1 } } },
            };
            var sidecar = new ResolutionSidecar(new List<SpanPin>(), votes);

            var sig = new SidecarSignal();
            var deltas = sig.Score(SnapshotWithSidecar(sidecar));

            Assert.Single(deltas);
            // VoteWeight = 0.3 (constante interne)
            Assert.Equal(0.3, deltas["two-uppercase:0"], 6);
        }

        [Fact]
        public void Multiple_votes_same_alt_scale_linearly()
        {
            // 3 votes pour two-uppercase:0 → 3 × 0.3 = 0.9.
            var votes = new Dictionary<string, IReadOnlyDictionary<int, int>>
            {
                { "two-uppercase", new Dictionary<int, int> { { 0, 3 } } },
            };
            var sidecar = new ResolutionSidecar(new List<SpanPin>(), votes);

            var deltas = new SidecarSignal().Score(SnapshotWithSidecar(sidecar));

            Assert.Equal(0.9, deltas["two-uppercase:0"], 6);
        }

        [Fact]
        public void Different_alts_in_same_rule_get_separate_deltas()
        {
            // vec: 2 votes, produit: 1 vote.
            var votes = new Dictionary<string, IReadOnlyDictionary<int, int>>
            {
                { "two-uppercase", new Dictionary<int, int> { { 0, 2 }, { 1, 1 } } },
            };
            var sidecar = new ResolutionSidecar(new List<SpanPin>(), votes);

            var deltas = new SidecarSignal().Score(SnapshotWithSidecar(sidecar));

            Assert.Equal(2, deltas.Count);
            Assert.Equal(0.6, deltas["two-uppercase:0"], 6);
            Assert.Equal(0.3, deltas["two-uppercase:1"], 6);
        }

        [Fact]
        public void Pins_alone_do_not_produce_deltas()
        {
            // Le SidecarSignal ne contribue PAS pour les pins (qui sont
            // gérés span-level dans ZoneResolver). Un sidecar avec pins
            // mais sans votes → pas de score.
            var pins = new List<SpanPin> { new SpanPin("two-uppercase", 0, 2, 0) };
            var sidecar = new ResolutionSidecar(pins,
                new Dictionary<string, IReadOnlyDictionary<int, int>>());

            var deltas = new SidecarSignal().Score(SnapshotWithSidecar(sidecar));

            Assert.Empty(deltas);
        }

        [Fact]
        public void Multiple_rules_produce_independent_deltas()
        {
            var votes = new Dictionary<string, IReadOnlyDictionary<int, int>>
            {
                { "two-uppercase", new Dictionary<int, int> { { 0, 1 } } },
                { "canonical-set", new Dictionary<int, int> { { 1, 2 } } },
            };
            var sidecar = new ResolutionSidecar(new List<SpanPin>(), votes);

            var deltas = new SidecarSignal().Score(SnapshotWithSidecar(sidecar));

            Assert.Equal(2, deltas.Count);
            Assert.Equal(0.3, deltas["two-uppercase:0"], 6);
            Assert.Equal(0.6, deltas["canonical-set:1"], 6);
        }

        [Fact]
        public void Signal_metadata_matches_brief()
        {
            var sig = new SidecarSignal();
            Assert.Equal("Sidecar", sig.Name);
            Assert.Equal(ZoomLevel.L1_Block, sig.Level);
        }
    }
}
