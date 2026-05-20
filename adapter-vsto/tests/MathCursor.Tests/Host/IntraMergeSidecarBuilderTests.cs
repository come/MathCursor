using System.Collections.Generic;
using MathCursor.Core;
using MathCursor.Core.Resolution;
using MathCursor.Host;
using Xunit;

namespace MathCursor.Tests.Host
{
    /// <summary>
    /// Tests d'intégration de <see cref="IntraMergeSidecarBuilder"/> — la
    /// logique pure extraite de <c>SuggestionService.TryMergeWithAdjacentOMaths</c>
    /// qui calcule le sidecar fusionné quand deux OMaths adjacents même
    /// paragraphe sont absorbés au commit.
    /// <para>
    /// Bug 06-05 (intra-merge) couvert : tape <c>AB+BC</c> + désambig vec
    /// → commit. User rajoute <c> = AC</c> + désambig vec → commit. Avant
    /// le fix, le mergedSource <c>"AB+BC = AC"</c> repassait par
    /// <c>_resolver.Resolve(source)</c> sans sidecar → vec sautent au global.
    /// </para>
    /// <para>
    /// Ce test valide la chaîne complète sans Word : on construit les inputs
    /// que <c>TryMergeWithAdjacentOMaths</c> aurait collectés
    /// (leftSource/leftSidecar via <c>GetSidecarForHandle</c>, middle via
    /// <c>_popup.CurrentSidecar</c>), on appelle le builder, puis on
    /// vérifie via <see cref="ZoneResolver"/> que les vec sont préservés.
    /// </para>
    /// </summary>
    public sealed class IntraMergeSidecarBuilderTests
    {
        private const string RuleVec = "two-uppercase";

        // ─── Cas dégénérés ─────────────────────────────────────────────

        [Fact(DisplayName = "Pas de left ni right (commit isolé) → seul middle compte, shift 0")]
        public void Middle_only_no_neighbors_keeps_middle_pins()
        {
            var middleSc = MakeSc(new[] { new SpanPin(RuleVec, 2, 2, 0) });

            var r = IntraMergeSidecarBuilder.Build(
                leftSource: null, leftSidecar: null,
                middleSource: "= AC", middleSidecar: middleSc,
                rightSource: null, rightSidecar: null);

            Assert.Single(r.SpanPins);
            Assert.Equal(2, r.SpanPins[0].Offset);
        }

        [Fact(DisplayName = "Left null tolérant → builder ne plante pas, ignore silencieusement")]
        public void Left_sidecar_null_is_tolerated()
        {
            var r = IntraMergeSidecarBuilder.Build(
                leftSource: "AB", leftSidecar: null,
                middleSource: "+CD", middleSidecar: ResolutionSidecar.Empty,
                rightSource: null, rightSidecar: null);

            Assert.True(r.IsEmpty);
        }

        // ─── Bug 06-05 : scenario user complet ─────────────────────────

        [Fact(DisplayName = "Bug 06-05 : `AB+BC` (left) + ` = AC` (middle) → 3 pins recalibrés sur mergedSource")]
        public void IntraMerge_bug_scenario_recalibrates_offsets_with_space_separator()
        {
            // Reproduit exactement ce que TryMergeWithAdjacentOMaths fait quand
            // l'user a committé "AB+BC" puis tape " = AC" (= AC absorbé).
            // mergedSource = "AB+BC = AC" (10 chars).
            var leftSc = MakeSc(
                new[]
                {
                    new SpanPin(RuleVec, 0, 2, 0), // AB
                    new SpanPin(RuleVec, 3, 2, 0), // BC
                },
                ("two-uppercase", 0, 2));
            var middleSc = MakeSc(
                new[] { new SpanPin(RuleVec, 2, 2, 0) }, // AC dans "= AC" (offset 2)
                ("two-uppercase", 0, 1));

            var r = IntraMergeSidecarBuilder.Build(
                leftSource: "AB+BC", leftSidecar: leftSc,
                middleSource: "= AC", middleSidecar: middleSc,
                rightSource: null, rightSidecar: null);

            // 3 pins recalibrés sur "AB+BC = AC" (left + middle shift 6)
            Assert.Equal(3, r.SpanPins.Count);
            Assert.Equal(0, r.SpanPins[0].Offset); // AB
            Assert.Equal(3, r.SpanPins[1].Offset); // BC
            Assert.Equal(8, r.SpanPins[2].Offset); // AC (2 + 6)

            // Votes sommés (boost cascade pour spans futurs sans pin)
            Assert.Equal(3, r.GetVote(RuleVec, 0));
        }

        [Fact(DisplayName = "Bug 06-05 (chaîne complète) : `AB+BC = AC` via builder → resolver produit 3 \\vec")]
        public void IntraMerge_bug_full_chain_resolver_renders_three_vecs()
        {
            // Test bout en bout : on simule l'enchaînement
            //   1. TryMergeWithAdjacentOMaths construit mergedSource + appelle builder
            //   2. SuggestionService passe le sidecar fusionné à _resolver.Resolve
            //   3. Le LaTeX final doit contenir les 3 \vec
            var leftSc = MakeSc(
                new[]
                {
                    new SpanPin(RuleVec, 0, 2, 0),
                    new SpanPin(RuleVec, 3, 2, 0),
                });
            var middleSc = MakeSc(
                new[] { new SpanPin(RuleVec, 2, 2, 0) });

            var mergedSc = IntraMergeSidecarBuilder.Build(
                leftSource: "AB+BC", leftSidecar: leftSc,
                middleSource: "= AC", middleSidecar: middleSc,
                rightSource: null, rightSidecar: null);

            const string mergedSource = "AB+BC = AC"; // ce que le SB construit
            var resolver = new ZoneResolver(new LatticeEngine());
            var resolved = resolver.Resolve(mergedSource, mergedSc);

            Assert.Contains("\\vec{AB}", resolved.TopLatex);
            Assert.Contains("\\vec{BC}", resolved.TopLatex);
            Assert.Contains("\\vec{AC}", resolved.TopLatex);
        }

        // ─── Triple absorption (left + middle + right) ─────────────────

        [Fact(DisplayName = "Left + middle + right → 3 shifts cumulés (left=0, middle=L+1, right=L+1+M+1)")]
        public void Triple_merge_cumulates_shifts_correctly()
        {
            var leftSc = MakeSc(new[] { new SpanPin(RuleVec, 0, 2, 0) });   // AB
            var middleSc = MakeSc(new[] { new SpanPin(RuleVec, 1, 2, 0) }); // " CD"[1..3]
            var rightSc = MakeSc(new[] { new SpanPin(RuleVec, 0, 2, 0) });  // EF

            // mergedSource = "AB" + " " + " CD" + " " + "EF"
            //              = "AB  CD EF"
            //                 0123456789
            // shift left = 0, shift middle = 3, shift right = 3 + 4 = 7 (oui : middle.Length+1 = 4)
            var r = IntraMergeSidecarBuilder.Build(
                leftSource: "AB", leftSidecar: leftSc,
                middleSource: " CD", middleSidecar: middleSc,
                rightSource: "EF", rightSidecar: rightSc);

            Assert.Equal(3, r.SpanPins.Count);
            Assert.Equal(0, r.SpanPins[0].Offset);     // AB
            Assert.Equal(4, r.SpanPins[1].Offset);     // CD : 1 + 3
            Assert.Equal(7, r.SpanPins[2].Offset);     // EF : 0 + 7
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
