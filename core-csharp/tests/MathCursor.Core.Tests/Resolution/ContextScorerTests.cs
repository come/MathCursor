using System.Collections.Generic;
using System.Linq;
using MathCursor.Core.Resolution;
using Xunit;

namespace MathCursor.Core.Tests.Resolution
{
    public class ContextScorerTests
    {
        // Helper : signal stub qui retourne des deltas fixes pour un niveau donné.
        private sealed class StubSignal : IContextSignal
        {
            public string Name { get; }
            public ZoomLevel Level { get; }
            private readonly Dictionary<string, double> _deltas;
            public StubSignal(string name, ZoomLevel level, Dictionary<string, double> deltas)
            {
                Name = name;
                Level = level;
                _deltas = deltas;
            }
            public IReadOnlyDictionary<string, double> Score(ContextSnapshot ctx) => _deltas;
        }

        private static ContextSnapshot AnySnapshot()
            => new ContextSnapshot(rawSource: "x", sidecar: ResolutionSidecar.Empty);

        // ─── Aggregation de base ──────────────────────────────────────

        [Fact]
        public void Empty_signals_returns_empty_hints()
        {
            var scorer = new ContextScorer(new List<IContextSignal>());
            var hints = scorer.Aggregate(AnySnapshot());
            Assert.Empty(hints.AltScores);
            Assert.Empty(hints.Trace);
        }

        [Fact]
        public void Null_snapshot_returns_empty_hints()
        {
            var scorer = new ContextScorer(new List<IContextSignal> {
                new StubSignal("s1", ZoomLevel.L0_Token, new Dictionary<string, double> { { "two-uppercase:0", 1.0 } })
            });
            var hints = scorer.Aggregate(null);
            Assert.Empty(hints.AltScores);
        }

        [Fact]
        public void Single_signal_at_L0_passes_value_through_with_weight_1()
        {
            // L0 a poids 1.0 → la valeur du signal sort telle quelle.
            var sig = new StubSignal("s", ZoomLevel.L0_Token,
                new Dictionary<string, double> { { "two-uppercase:0", 0.5 } });
            var scorer = new ContextScorer(new List<IContextSignal> { sig });

            var hints = scorer.Aggregate(AnySnapshot());

            Assert.Single(hints.AltScores);
            Assert.Equal(0.5, hints.AltScores["two-uppercase:0"], 6);
        }

        [Fact]
        public void Signal_at_L2_is_weighted_by_paragraph_weight()
        {
            // L2 a poids 0.7 par défaut → 1.0 × 0.7 = 0.7.
            var sig = new StubSignal("s", ZoomLevel.L2_Paragraph,
                new Dictionary<string, double> { { "two-uppercase:0", 1.0 } });
            var scorer = new ContextScorer(new List<IContextSignal> { sig });

            var hints = scorer.Aggregate(AnySnapshot());

            Assert.Equal(0.7, hints.AltScores["two-uppercase:0"], 6);
        }

        // ─── Accumulation multi-signaux ───────────────────────────────

        [Fact]
        public void Multiple_signals_on_same_alt_accumulate_weighted()
        {
            // Cas typique du brief : L1 (bloc) et L2 (¶) musclent vec ensemble.
            //   L1 (poids 0.9) × 1.0 = 0.9
            //   L2 (poids 0.7) × 1.0 = 0.7
            //   total = 1.6
            var s1 = new StubSignal("L1", ZoomLevel.L1_Block,
                new Dictionary<string, double> { { "two-uppercase:0", 1.0 } });
            var s2 = new StubSignal("L2", ZoomLevel.L2_Paragraph,
                new Dictionary<string, double> { { "two-uppercase:0", 1.0 } });
            var scorer = new ContextScorer(new List<IContextSignal> { s1, s2 });

            var hints = scorer.Aggregate(AnySnapshot());

            Assert.Equal(1.6, hints.AltScores["two-uppercase:0"], 6);
        }

        [Fact]
        public void Different_alts_are_tracked_independently()
        {
            var s = new StubSignal("s", ZoomLevel.L0_Token, new Dictionary<string, double>
            {
                { "two-uppercase:0", 0.5 },  // vec
                { "two-uppercase:1", 0.2 },  // produit
                { "canonical-set:0", 0.8 },  // R variable
            });
            var scorer = new ContextScorer(new List<IContextSignal> { s });

            var hints = scorer.Aggregate(AnySnapshot());

            Assert.Equal(3, hints.AltScores.Count);
            Assert.Equal(0.5, hints.AltScores["two-uppercase:0"], 6);
            Assert.Equal(0.2, hints.AltScores["two-uppercase:1"], 6);
            Assert.Equal(0.8, hints.AltScores["canonical-set:0"], 6);
        }

        [Fact]
        public void Negative_delta_demuscles_alt()
        {
            // Un signal peut démuscler une alt (delta négatif).
            var s = new StubSignal("s", ZoomLevel.L0_Token,
                new Dictionary<string, double> { { "two-uppercase:1", -0.4 } });
            var scorer = new ContextScorer(new List<IContextSignal> { s });

            var hints = scorer.Aggregate(AnySnapshot());

            Assert.Equal(-0.4, hints.AltScores["two-uppercase:1"], 6);
        }

        // ─── BestAltForRule ───────────────────────────────────────────

        [Fact]
        public void BestAltForRule_returns_winner()
        {
            var s = new StubSignal("s", ZoomLevel.L0_Token, new Dictionary<string, double>
            {
                { "two-uppercase:0", 1.5 },
                { "two-uppercase:1", 0.3 },
            });
            var scorer = new ContextScorer(new List<IContextSignal> { s });

            var hints = scorer.Aggregate(AnySnapshot());

            var (alt, score) = hints.BestAltForRule("two-uppercase");
            Assert.Equal(0, alt);
            Assert.Equal(1.5, score, 6);
        }

        [Fact]
        public void BestAltForRule_returns_minus_one_if_no_score()
        {
            var scorer = new ContextScorer(new List<IContextSignal>());
            var hints = scorer.Aggregate(AnySnapshot());

            var (alt, _) = hints.BestAltForRule("two-uppercase");
            Assert.Equal(-1, alt);
        }

        [Fact]
        public void BestAltForRule_isolates_by_rule_prefix()
        {
            // Un score sur "canonical-set:0" ne doit pas leak sur "two-uppercase:0".
            var s = new StubSignal("s", ZoomLevel.L0_Token, new Dictionary<string, double>
            {
                { "canonical-set:0", 2.0 },
            });
            var scorer = new ContextScorer(new List<IContextSignal> { s });

            var hints = scorer.Aggregate(AnySnapshot());

            var (alt, _) = hints.BestAltForRule("two-uppercase");
            Assert.Equal(-1, alt);
        }

        // ─── Trace ────────────────────────────────────────────────────

        [Fact]
        public void Trace_records_each_contribution()
        {
            var s1 = new StubSignal("Sidecar", ZoomLevel.L1_Block,
                new Dictionary<string, double> { { "two-uppercase:0", 1.0 } });
            var s2 = new StubSignal("Para", ZoomLevel.L2_Paragraph,
                new Dictionary<string, double> { { "two-uppercase:0", 0.5 } });
            var scorer = new ContextScorer(new List<IContextSignal> { s1, s2 });

            var hints = scorer.Aggregate(AnySnapshot());

            Assert.Equal(2, hints.Trace.Count);
            Assert.Contains(hints.Trace, t => t.Contains("Sidecar") && t.Contains("L1_Block"));
            Assert.Contains(hints.Trace, t => t.Contains("Para") && t.Contains("L2_Paragraph"));
        }

        // ─── Default level weights match brief ─────────────────────────

        [Fact]
        public void Default_level_weights_match_brief_2026_05_07()
        {
            var w = ContextScorer.DefaultLevelWeights;
            Assert.Equal(1.0,  w[ZoomLevel.L0_Token]);
            Assert.Equal(0.9,  w[ZoomLevel.L1_Block]);
            Assert.Equal(0.7,  w[ZoomLevel.L2_Paragraph]);
            Assert.Equal(0.4,  w[ZoomLevel.L3_NeighborParas]);
            Assert.Equal(0.3,  w[ZoomLevel.L4_Section]);
            Assert.Equal(0.15, w[ZoomLevel.L5_Document]);
        }

        // ─── ScoringHints.Key helper ───────────────────────────────────

        [Theory]
        [InlineData("two-uppercase", 0, "two-uppercase:0")]
        [InlineData("two-uppercase", 1, "two-uppercase:1")]
        [InlineData("canonical-set", 5, "canonical-set:5")]
        [InlineData(null,            0, ":0")]
        public void Key_helper_formats_consistently(string? rule, int alt, string expected)
        {
            Assert.Equal(expected, ScoringHints.Key(rule, alt));
        }
    }
}
