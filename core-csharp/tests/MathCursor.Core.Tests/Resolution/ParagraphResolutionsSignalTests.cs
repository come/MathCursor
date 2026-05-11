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
        public void Multiple_pins_same_rule_alt_accumulate_with_decay()
        {
            // 3 pins identiques (rule, alt) dans le ¶ → cumul avec décay :
            // exp(-1) + exp(-0.5) + exp(0) ≈ 0.368 + 0.607 + 1.000 ≈ 1.974.
            // Cumul historique pondéré par récence (plus récent = plus fort).
            var pins = new List<SpanPin>
            {
                new SpanPin("two-uppercase", 0, 2, 0),  // distance 2
                new SpanPin("two-uppercase", 5, 2, 0),  // distance 1
                new SpanPin("two-uppercase", 10, 2, 0), // distance 0 (dernier = max)
            };

            var deltas = new ParagraphResolutionsSignal().Score(Snapshot(pins));

            const double expected = 1.0 + 0.6065306597126334 + 0.36787944117144233;
            Assert.Equal(expected, deltas["two-uppercase:0"], 6);
        }

        [Fact]
        public void Pins_different_alts_get_separate_deltas_with_decay()
        {
            // L'user a hésité dans le ¶ : 2× vec, 1× produit.
            // Ordre d'ajout : vec(distance 2), produit(distance 1), vec(distance 0).
            var pins = new List<SpanPin>
            {
                new SpanPin("two-uppercase", 0, 2, 0),  // vec, distance 2
                new SpanPin("two-uppercase", 5, 2, 1),  // produit, distance 1
                new SpanPin("two-uppercase", 10, 2, 0), // vec, distance 0
            };

            var deltas = new ParagraphResolutionsSignal().Score(Snapshot(pins));

            Assert.Equal(2, deltas.Count);
            // vec : exp(0) + exp(-1) ≈ 1.0 + 0.368 ≈ 1.368.
            Assert.Equal(1.0 + 0.36787944117144233, deltas["two-uppercase:0"], 6);
            // produit : exp(-0.5) ≈ 0.607.
            Assert.Equal(0.6065306597126334, deltas["two-uppercase:1"], 6);
        }

        [Fact]
        public void Pins_different_rules_independent_with_decay()
        {
            // Deux pins, rules différentes. Le plus récent = canonical-set (distance 0).
            var pins = new List<SpanPin>
            {
                new SpanPin("two-uppercase", 0, 2, 0),  // distance 1
                new SpanPin("canonical-set", 5, 1, 1),  // distance 0
            };

            var deltas = new ParagraphResolutionsSignal().Score(Snapshot(pins));

            Assert.Equal(2, deltas.Count);
            Assert.Equal(0.6065306597126334, deltas["two-uppercase:0"], 6);
            Assert.Equal(1.0, deltas["canonical-set:1"], 6);
        }

        // ─── Décay : le plus récent l'emporte sur l'ancien (« muscler le plus proche ») ──

        [Fact]
        public void Recent_pin_outweights_older_different_alt()
        {
            // Cas user 2026-05-07 : vec(AB) puis paren(CD) → la prochaine
            // ambig two-uppercase doit pencher vers paren (récent), pas vec.
            var pins = new List<SpanPin>
            {
                new SpanPin("two-uppercase", 0, 2, 0),  // vec, distance 1
                new SpanPin("two-uppercase", 5, 2, 1),  // paren, distance 0
            };

            var deltas = new ParagraphResolutionsSignal().Score(Snapshot(pins));

            // paren (1.0) doit dominer vec (~0.607).
            Assert.True(deltas["two-uppercase:1"] > deltas["two-uppercase:0"]);
        }

        [Fact]
        public void Cumulative_old_can_still_outweight_single_recent()
        {
            // 3× vec anciens vs 1× paren récent : l'historique cumulé l'emporte
            // de peu sur le single récent (vec ≈ 1.197 vs paren = 1.0).
            var pins = new List<SpanPin>
            {
                new SpanPin("two-uppercase", 0,  2, 0),  // vec, distance 3
                new SpanPin("two-uppercase", 5,  2, 0),  // vec, distance 2
                new SpanPin("two-uppercase", 10, 2, 0),  // vec, distance 1
                new SpanPin("two-uppercase", 15, 2, 1),  // paren, distance 0
            };

            var deltas = new ParagraphResolutionsSignal().Score(Snapshot(pins));

            // vec gagne mais de peu — robuste au bruit ponctuel.
            Assert.True(deltas["two-uppercase:0"] > deltas["two-uppercase:1"]);
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
