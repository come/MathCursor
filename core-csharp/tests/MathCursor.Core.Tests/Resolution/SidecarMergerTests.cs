using System.Collections.Generic;
using MathCursor.Core.Resolution;
using Xunit;

namespace MathCursor.Core.Tests.Resolution
{
    /// <summary>
    /// Tests de la fusion <see cref="SidecarMerger.Merge"/> — utilisée au
    /// cross-merge multi-ligne pour combiner les sidecars des paragraphes
    /// absorbés. Tests GREEN dès maintenant : la fusion est de la logique
    /// pure sur les POCO, indépendante du resolver et du store.
    ///
    /// Cas couverts :
    ///   - Fusion vide → Empty
    ///   - 1 part = passthrough (avec shift)
    ///   - 2 parts → pins recalibrés + votes sommés
    ///   - Une part null/empty → ignorée silencieusement (cas dégradé)
    ///   - Mismatch tailles → ArgumentException
    /// </summary>
    public sealed class SidecarMergerTests
    {
        // ─── Cas dégénérés ─────────────────────────────────────────────

        [Fact(DisplayName = "Merge null → Empty")]
        public void Merge_null_returns_empty()
        {
            var r = SidecarMerger.Merge(null, null);
            Assert.True(r.IsEmpty);
        }

        [Fact(DisplayName = "Merge liste vide → Empty")]
        public void Merge_empty_list_returns_empty()
        {
            var r = SidecarMerger.Merge(new List<ResolutionSidecar>(), new List<int>());
            Assert.True(r.IsEmpty);
        }

        [Fact(DisplayName = "Mismatch tailles → ArgumentException")]
        public void Mismatched_sizes_throws()
        {
            var parts = new[] { ResolutionSidecar.Empty };
            var shifts = new[] { 0, 5 };
            Assert.Throws<System.ArgumentException>(() => SidecarMerger.Merge(parts, shifts));
        }

        // ─── Fusion réelle ─────────────────────────────────────────────

        [Fact(DisplayName = "1 part avec shift 0 → identique")]
        public void Single_part_no_shift_passthrough()
        {
            var sc = MakeSc(
                new[] { new SpanPin("two-uppercase", 0, 2, 0) },
                ("two-uppercase", 0, 1));
            var r = SidecarMerger.Merge(new[] { sc }, new[] { 0 });

            Assert.Single(r.SpanPins);
            Assert.Equal(0, r.SpanPins[0].Offset);
            Assert.Equal(1, r.GetVote("two-uppercase", 0));
        }

        [Fact(DisplayName = "1 part avec shift 9 → pin offset décalé de 9")]
        public void Single_part_with_shift_decales_pin_offset()
        {
            var sc = MakeSc(
                new[] { new SpanPin("two-uppercase", 3, 2, 0) },
                ("two-uppercase", 0, 1));
            var r = SidecarMerger.Merge(new[] { sc }, new[] { 9 });

            Assert.Single(r.SpanPins);
            Assert.Equal(12, r.SpanPins[0].Offset); // 3 + 9
            Assert.Equal(2, r.SpanPins[0].Len);     // inchangé
        }

        [Fact(DisplayName = "Bug 06-05 : merge cross-line `AB+BC=CD\\n= CH+HD` recalibre offsets ligne 2")]
        public void CrossMerge_2_lines_recalibrates_offsets_and_sums_votes()
        {
            // Ligne 1 : "AB+BC=CD" (8 chars) — pins offsets 0, 3, 6
            var line1 = MakeSc(
                new[]
                {
                    new SpanPin("two-uppercase", 0, 2, 0),
                    new SpanPin("two-uppercase", 3, 2, 0),
                    new SpanPin("two-uppercase", 6, 2, 0),
                },
                ("two-uppercase", 0, 3));

            // Ligne 2 : "= CH + HD" (9 chars) — pins offsets 2 (CH), 7 (HD)
            // après le marker "= "
            var line2 = MakeSc(
                new[]
                {
                    new SpanPin("two-uppercase", 2, 2, 0),
                    new SpanPin("two-uppercase", 7, 2, 0),
                },
                ("two-uppercase", 0, 2));

            // mergedSource = "AB+BC=CD\n= CH + HD"
            // shift ligne 1 = 0, shift ligne 2 = 9 (8 chars + 1 \n)
            var r = SidecarMerger.Merge(new[] { line1, line2 }, new[] { 0, 9 });

            // 5 pins : 3 (ligne 1) + 2 (ligne 2 décalés)
            Assert.Equal(5, r.SpanPins.Count);
            // Ligne 1 : offsets inchangés (shift 0)
            Assert.Equal(0, r.SpanPins[0].Offset);
            Assert.Equal(3, r.SpanPins[1].Offset);
            Assert.Equal(6, r.SpanPins[2].Offset);
            // Ligne 2 : offsets décalés de 9
            Assert.Equal(11, r.SpanPins[3].Offset); // CH : 2 + 9
            Assert.Equal(16, r.SpanPins[4].Offset); // HD : 7 + 9

            // Votes additionnés : 3 + 2 = 5 votes vec
            Assert.Equal(5, r.GetVote("two-uppercase", 0));
        }

        [Fact(DisplayName = "Bug 06-05 (intra-merge même-ligne) : `AB+BC` + ` = AC` recalibre offsets avec espace")]
        public void IntraMerge_same_line_recalibrates_with_space_separator()
        {
            // Reproduit ce que `TryMergeWithAdjacentOMaths` calcule pour le
            // scénario user : commit `AB+BC` (vec) ligne 1 → user rajoute
            // ` = AC` (vec) → adapter détecte adjacence, fusionne.
            //
            // mergedSource = leftSource + ' ' + middleSource = "AB+BC = AC"
            // shifts : left = 0, middle = len("AB+BC")+1 = 6.
            //
            // Avant le fix de SuggestionService.TryMergeWithAdjacentOMaths,
            // MergedSidecar restait Empty → vec sautaient au reranking. Ce
            // test garantit que le calcul est correct quand l'adapter
            // l'invoque (offsets recalibrés sur la mergedSource).

            // left = "AB+BC" (5 chars) — pins 0 (AB), 3 (BC) + 2 votes vec
            var left = MakeSc(
                new[]
                {
                    new SpanPin("two-uppercase", 0, 2, 0),
                    new SpanPin("two-uppercase", 3, 2, 0),
                },
                ("two-uppercase", 0, 2));

            // middle = "= AC" (4 chars) — pin 2 (AC) + 1 vote vec
            var middle = MakeSc(
                new[] { new SpanPin("two-uppercase", 2, 2, 0) },
                ("two-uppercase", 0, 1));

            var r = SidecarMerger.Merge(new[] { left, middle }, new[] { 0, 6 });

            // 3 pins : AB(0), BC(3), AC(2+6=8) — offsets dans "AB+BC = AC"
            Assert.Equal(3, r.SpanPins.Count);
            Assert.Equal(0, r.SpanPins[0].Offset);
            Assert.Equal(3, r.SpanPins[1].Offset);
            Assert.Equal(8, r.SpanPins[2].Offset);
            // Votes : 2 + 1 = 3 votes vec → boost cascade pour spans futurs
            Assert.Equal(3, r.GetVote("two-uppercase", 0));
        }

        [Fact(DisplayName = "Bug 06-05 (intra-merge round-trip) : sidecar fusionné survit serialize/deserialize")]
        public void IntraMerge_merged_sidecar_round_trip_through_json()
        {
            // Garantie de persistence Phase 3 : le sidecar fusionné après
            // intra-merge doit pouvoir être serialisé et redéserialisé
            // sans perte (ce qui se passe au commit → CustomXMLPart →
            // reload Word → entrée edit-mode).
            var left = MakeSc(
                new[]
                {
                    new SpanPin("two-uppercase", 0, 2, 0),
                    new SpanPin("two-uppercase", 3, 2, 0),
                },
                ("two-uppercase", 0, 2));
            var middle = MakeSc(
                new[] { new SpanPin("two-uppercase", 2, 2, 0) },
                ("two-uppercase", 0, 1));

            var merged = SidecarMerger.Merge(new[] { left, middle }, new[] { 0, 6 });
            string json = SidecarSerializer.Serialize(merged);
            var roundTripped = SidecarSerializer.Deserialize(json);

            Assert.Equal(merged.SpanPins.Count, roundTripped.SpanPins.Count);
            for (int i = 0; i < merged.SpanPins.Count; i++)
            {
                Assert.Equal(merged.SpanPins[i].Rule, roundTripped.SpanPins[i].Rule);
                Assert.Equal(merged.SpanPins[i].Offset, roundTripped.SpanPins[i].Offset);
                Assert.Equal(merged.SpanPins[i].Len, roundTripped.SpanPins[i].Len);
                Assert.Equal(merged.SpanPins[i].AltIdx, roundTripped.SpanPins[i].AltIdx);
            }
            Assert.Equal(
                merged.GetVote("two-uppercase", 0),
                roundTripped.GetVote("two-uppercase", 0));
        }

        [Fact(DisplayName = "Part null/empty ignorée silencieusement (cas OMath ancien sans sidecar)")]
        public void Null_or_empty_part_is_ignored()
        {
            var sc = MakeSc(
                new[] { new SpanPin("two-uppercase", 0, 2, 0) },
                ("two-uppercase", 0, 1));
            var r = SidecarMerger.Merge(
                new[] { null, sc, ResolutionSidecar.Empty },
                new[] { 0, 5, 10 });

            // Seul le 2e part contribue, pin offset = 0 + 5 = 5
            Assert.Single(r.SpanPins);
            Assert.Equal(5, r.SpanPins[0].Offset);
            Assert.Equal(1, r.GetVote("two-uppercase", 0));
        }

        [Fact(DisplayName = "Votes additionnés même sur alt différentes (vec et paren)")]
        public void Votes_accumulate_per_alt()
        {
            var line1 = MakeSc(new SpanPin[0],
                ("two-uppercase", 0, 3),  // 3 votes vec
                ("two-uppercase", 1, 1)); // 1 vote paren

            var line2 = MakeSc(new SpanPin[0],
                ("two-uppercase", 0, 2),  // 2 votes vec
                ("two-uppercase", 2, 1)); // 1 vote crochet

            var r = SidecarMerger.Merge(new[] { line1, line2 }, new[] { 0, 10 });

            Assert.Equal(5, r.GetVote("two-uppercase", 0)); // 3 + 2
            Assert.Equal(1, r.GetVote("two-uppercase", 1)); // 1 + 0
            Assert.Equal(1, r.GetVote("two-uppercase", 2)); // 0 + 1
        }

        // ─── helpers ───────────────────────────────────────────────────

        private static ResolutionSidecar MakeSc(
            SpanPin[] pins, params (string rule, int alt, int count)[] voteEntries)
        {
            var votes = new Dictionary<string, IReadOnlyDictionary<int, int>>();
            foreach (var v in voteEntries)
            {
                if (!votes.TryGetValue(v.rule, out var byAlt))
                {
                    byAlt = new Dictionary<int, int>();
                    votes[v.rule] = byAlt;
                }
                ((Dictionary<int, int>)byAlt)[v.alt] = v.count;
            }
            return new ResolutionSidecar(pins, votes);
        }
    }
}
