using System.Collections.Generic;
using MathCursor.Core.Resolution;
using MathCursor.Core.Resolution.Signals;
using Xunit;

namespace MathCursor.Core.Tests.Resolution
{
    public class ParagraphResolutionsSignalTests
    {
        private static ContextSnapshot Snapshot(IReadOnlyList<SpanPin> pins)
            => new ContextSnapshot(
                rawSource: "AD",
                sidecar: ResolutionSidecar.Empty,
                recentParagraphPins: pins);

        [Fact]
        public void Empty_paragraph_history_produces_no_deltas()
        {
            var deltas = new ParagraphResolutionsSignal().Score(Snapshot(new List<SpanPin>()));
            Assert.Empty(deltas);
        }

        [Fact]
        public void Null_snapshot_produces_no_deltas()
        {
            var deltas = new ParagraphResolutionsSignal().Score(null);
            Assert.Empty(deltas);
        }

        [Fact]
        public void Single_pin_produces_unit_delta()
        {
            // Cas du brief : ligne 1 du système résolue en vec (pin two-uppercase:0).
            // Le ¶ a un pin → signal contribue +1.0 brut sur cette alt.
            var pin = new SpanPin("two-uppercase", offset: 0, len: 2, altIdx: 0);
            var deltas = new ParagraphResolutionsSignal().Score(Snapshot(new List<SpanPin> { pin }));

            Assert.Single(deltas);
            Assert.Equal(1.0, deltas["two-uppercase:0"], 6);
        }

        [Fact]
        public void Multiple_pins_same_rule_alt_accumulate()
        {
            // 3 pins identiques (rule, alt) dans le ¶ → +3.0 brut.
            // Cas typique : système 3 lignes où l'user a déjà résolu vec
            // sur les 3 premières lignes, la 4e doit être musclée fort.
            var pins = new List<SpanPin>
            {
                new SpanPin("two-uppercase", 0, 2, 0),
                new SpanPin("two-uppercase", 5, 2, 0),
                new SpanPin("two-uppercase", 10, 2, 0),
            };

            var deltas = new ParagraphResolutionsSignal().Score(Snapshot(pins));

            Assert.Equal(3.0, deltas["two-uppercase:0"], 6);
        }

        [Fact]
        public void Pins_different_alts_get_separate_deltas()
        {
            // L'user a hésité dans le ¶ : 2× vec, 1× produit.
            var pins = new List<SpanPin>
            {
                new SpanPin("two-uppercase", 0, 2, 0),  // vec
                new SpanPin("two-uppercase", 5, 2, 1),  // produit
                new SpanPin("two-uppercase", 10, 2, 0), // vec
            };

            var deltas = new ParagraphResolutionsSignal().Score(Snapshot(pins));

            Assert.Equal(2, deltas.Count);
            Assert.Equal(2.0, deltas["two-uppercase:0"], 6);
            Assert.Equal(1.0, deltas["two-uppercase:1"], 6);
        }

        [Fact]
        public void Pins_different_rules_independent()
        {
            var pins = new List<SpanPin>
            {
                new SpanPin("two-uppercase", 0, 2, 0),
                new SpanPin("canonical-set", 5, 1, 1),
            };

            var deltas = new ParagraphResolutionsSignal().Score(Snapshot(pins));

            Assert.Equal(2, deltas.Count);
            Assert.Equal(1.0, deltas["two-uppercase:0"], 6);
            Assert.Equal(1.0, deltas["canonical-set:1"], 6);
        }

        [Fact]
        public void Invalid_pins_are_ignored()
        {
            // Pin avec rule null/empty ou altIdx négatif → ignoré.
            var pins = new List<SpanPin>
            {
                new SpanPin(null, 0, 2, 0),     // rule null
                new SpanPin("", 0, 2, 0),       // rule empty
                new SpanPin("two-uppercase", 0, 2, -1), // altIdx négatif
                new SpanPin("two-uppercase", 0, 2, 0),  // valide
            };

            var deltas = new ParagraphResolutionsSignal().Score(Snapshot(pins));

            Assert.Single(deltas);
            Assert.Equal(1.0, deltas["two-uppercase:0"], 6);
        }

        [Fact]
        public void Signal_metadata_matches_brief()
        {
            var sig = new ParagraphResolutionsSignal();
            Assert.Equal("ParagraphResolutions", sig.Name);
            Assert.Equal(ZoomLevel.L2_Paragraph, sig.Level);
        }
    }
}
