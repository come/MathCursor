using System.Collections.Generic;
using MathCursor.Core.Resolution;
using Xunit;

namespace MathCursor.Core.Tests.Resolution
{
    /// <summary>
    /// Tests roundtrip du <see cref="SidecarSerializer"/>. Phase 3 ADR 06-05 :
    /// le sidecar doit pouvoir être persisté en JSON et relu identique. Robuste
    /// au JSON malformé / version inconnue / champs futurs.
    /// </summary>
    public sealed class SidecarSerializerTests
    {
        // ─── Roundtrip ────────────────────────────────────────────────

        [Fact(DisplayName = "Empty sidecar → \"\" (économie payload)")]
        public void Empty_sidecar_serializes_to_empty_string()
        {
            Assert.Equal("", SidecarSerializer.Serialize(ResolutionSidecar.Empty));
        }

        [Fact(DisplayName = "Roundtrip : 3 pins + 1 vote → identique après deserialize")]
        public void Roundtrip_pins_and_votes()
        {
            var votes = new Dictionary<string, IReadOnlyDictionary<int, int>>
            {
                ["two-uppercase"] = new Dictionary<int, int> { [0] = 3 },
            };
            var original = new ResolutionSidecar(
                new[]
                {
                    new SpanPin("two-uppercase", 0, 2, 0),
                    new SpanPin("two-uppercase", 3, 2, 0),
                    new SpanPin("two-uppercase", 6, 2, 0),
                },
                votes);

            var json = SidecarSerializer.Serialize(original);
            var roundtripped = SidecarSerializer.Deserialize(json);

            Assert.Equal(3, roundtripped.SpanPins.Count);
            Assert.Equal("two-uppercase", roundtripped.SpanPins[0].Rule);
            Assert.Equal(0, roundtripped.SpanPins[0].Offset);
            Assert.Equal(2, roundtripped.SpanPins[0].Len);
            Assert.Equal(0, roundtripped.SpanPins[0].AltIdx);
            Assert.Equal(3, roundtripped.SpanPins[1].Offset);
            Assert.Equal(6, roundtripped.SpanPins[2].Offset);
            Assert.Equal(3, roundtripped.GetVote("two-uppercase", 0));
        }

        [Fact(DisplayName = "Roundtrip : votes sur plusieurs alts du même rule")]
        public void Roundtrip_multiple_alt_votes_per_rule()
        {
            var votes = new Dictionary<string, IReadOnlyDictionary<int, int>>
            {
                ["two-uppercase"] = new Dictionary<int, int> { [0] = 3, [1] = 1, [2] = 0 },
            };
            var original = new ResolutionSidecar(new SpanPin[0], votes);

            var json = SidecarSerializer.Serialize(original);
            var rt = SidecarSerializer.Deserialize(json);

            Assert.Equal(3, rt.GetVote("two-uppercase", 0));
            Assert.Equal(1, rt.GetVote("two-uppercase", 1));
            Assert.Equal(0, rt.GetVote("two-uppercase", 2));
        }

        [Fact(DisplayName = "Roundtrip : 2 rules avec votes distincts")]
        public void Roundtrip_two_rules_distinct_votes()
        {
            var votes = new Dictionary<string, IReadOnlyDictionary<int, int>>
            {
                ["two-uppercase"] = new Dictionary<int, int> { [0] = 2 },
                ["letter-sup-number"] = new Dictionary<int, int> { [0] = 5 },
            };
            var original = new ResolutionSidecar(new SpanPin[0], votes);

            var rt = SidecarSerializer.Deserialize(SidecarSerializer.Serialize(original));

            Assert.Equal(2, rt.GetVote("two-uppercase", 0));
            Assert.Equal(5, rt.GetVote("letter-sup-number", 0));
        }

        [Fact(DisplayName = "Format JSON v2 : clés courtes (v/pins/votes/rule_pins/span_overrides) pour économie payload")]
        public void Format_uses_short_keys()
        {
            var sidecar = new ResolutionSidecar(
                new[] { new SpanPin("two-uppercase", 0, 2, 0) },
                new Dictionary<string, IReadOnlyDictionary<int, int>>
                {
                    ["two-uppercase"] = new Dictionary<int, int> { [0] = 1 },
                });
            var json = SidecarSerializer.Serialize(sidecar);

            // v2 désormais. Format legacy v1 toléré au load (cf. brief
            // 2026-05-07-rule-pin-span-override-refactor).
            Assert.Contains("\"v\":2", json);
            Assert.Contains("\"pins\":", json);
            Assert.Contains("\"r\":\"two-uppercase\"", json);
            Assert.Contains("\"o\":0", json);
            Assert.Contains("\"l\":2", json);
            Assert.Contains("\"a\":0", json);
            Assert.Contains("\"votes\":", json);
        }

        // ─── Robustesse ─────────────────────────────────────────────

        [Theory(DisplayName = "Entrée invalide → Empty (jamais throw)")]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("garbage")]
        [InlineData("{")]
        [InlineData("{\"v\":1,\"pins\":[{")]
        [InlineData("{\"v\":\"oops\"}")]
        public void Invalid_input_returns_empty(string input)
        {
            var rt = SidecarSerializer.Deserialize(input);
            Assert.True(rt.IsEmpty);
        }

        [Fact(DisplayName = "Version future inconnue → Empty (migration silencieuse)")]
        public void Future_version_returns_empty()
        {
            var rt = SidecarSerializer.Deserialize("{\"v\":42,\"pins\":[]}");
            Assert.True(rt.IsEmpty);
        }

        [Fact(DisplayName = "Champ inconnu ignoré (forward-compat)")]
        public void Unknown_field_ignored()
        {
            // Si une future version ajoute un champ "extras", l'ancien
            // deserializer doit l'ignorer et garder ce qu'il connaît.
            var json = "{\"v\":1,\"pins\":[{\"r\":\"two-uppercase\",\"o\":0,\"l\":2,\"a\":0}],\"extras\":{\"foo\":42}}";
            var rt = SidecarSerializer.Deserialize(json);
            Assert.Single(rt.SpanPins);
            Assert.Equal("two-uppercase", rt.SpanPins[0].Rule);
        }

        // ─── v2 : RulePins + SpanOverrides ─────────────────────────────

        [Fact(DisplayName = "v2 roundtrip : RulePin")]
        public void V2_roundtrip_rule_pin()
        {
            var sc = new ResolutionSidecar(
                spanPins: null,
                zoneVotes: null,
                rulePins: new[] { new RulePin("two-uppercase", 0) },
                spanOverrides: null);
            var json = SidecarSerializer.Serialize(sc);
            Assert.Contains("\"rule_pins\":[{", json);
            var rt = SidecarSerializer.Deserialize(json);
            Assert.Single(rt.RulePins);
            Assert.Equal("two-uppercase", rt.RulePins[0].RuleId);
            Assert.Equal(0, rt.RulePins[0].AltIdx);
        }

        [Fact(DisplayName = "v2 roundtrip : SpanOverride avec signature complète")]
        public void V2_roundtrip_span_override()
        {
            var sig = new MatchSignature("two-uppercase", "AB", 3, 1);
            var sc = new ResolutionSidecar(null, null, null,
                new[] { new SpanOverride(sig, 1) });
            var json = SidecarSerializer.Serialize(sc);
            Assert.Contains("\"span_overrides\":[{", json);
            var rt = SidecarSerializer.Deserialize(json);
            Assert.Single(rt.SpanOverrides);
            var ov = rt.SpanOverrides[0];
            Assert.Equal("two-uppercase", ov.Signature.RuleId);
            Assert.Equal("AB", ov.Signature.DefaultLatex);
            Assert.Equal(3, ov.Signature.RawSourcePos);
            Assert.Equal(1, ov.Signature.OccurrenceIdx);
            Assert.Equal(1, ov.AltIdx);
            Assert.False(ov.IsRevert);
        }

        [Fact(DisplayName = "v2 roundtrip : SpanOverride revert (alt=-1)")]
        public void V2_roundtrip_span_override_revert()
        {
            var sig = new MatchSignature("two-uppercase", "AB", 0, 0);
            var sc = new ResolutionSidecar(null, null, null,
                new[] { new SpanOverride(sig, SpanOverride.AltIdxRevert) });
            var json = SidecarSerializer.Serialize(sc);
            Assert.Contains("\"a\":-1", json);
            var rt = SidecarSerializer.Deserialize(json);
            Assert.Single(rt.SpanOverrides);
            Assert.True(rt.SpanOverrides[0].IsRevert);
        }

        [Fact(DisplayName = "Lazy convert v1 → v2 : ZoneVotes argmax → RulePin")]
        public void V1_votes_converted_to_rule_pin_via_argmax()
        {
            // Sidecar v1 avec votes sur two-uppercase : alt 0 = 3 votes,
            // alt 1 = 1 vote. Argmax → RulePin two-uppercase:0.
            var json = "{\"v\":1,\"votes\":{\"two-uppercase\":{\"0\":3,\"1\":1}}}";
            var rt = SidecarSerializer.Deserialize(json);
            Assert.Single(rt.RulePins);
            Assert.Equal("two-uppercase", rt.RulePins[0].RuleId);
            Assert.Equal(0, rt.RulePins[0].AltIdx);
            // ZoneVotes legacy gardés aussi (pour ne pas perdre l'info).
            Assert.NotEmpty(rt.ZoneVotes);
        }

        [Fact(DisplayName = "Lazy convert v1 : argmax tie-break sur le plus petit altIdx")]
        public void V1_votes_argmax_tie_breaks_to_smaller_alt()
        {
            // alt 0 = 1 vote, alt 1 = 1 vote → tie → on garde alt 0.
            var json = "{\"v\":1,\"votes\":{\"two-uppercase\":{\"0\":1,\"1\":1}}}";
            var rt = SidecarSerializer.Deserialize(json);
            Assert.Single(rt.RulePins);
            Assert.Equal(0, rt.RulePins[0].AltIdx);
        }

        [Fact(DisplayName = "Lazy convert v1 : SpanPins legacy gardés tels quels")]
        public void V1_span_pins_kept_as_legacy()
        {
            // Les SpanPins ne sont pas convertis en SpanOverrides au load
            // (nécessite le rawSource, fait ailleurs). Ils restent dans
            // ResolutionSidecar.SpanPins et continuent à fonctionner via le
            // pin matching span-level dans ZoneResolver.
            var json = "{\"v\":1,\"pins\":[{\"r\":\"two-uppercase\",\"o\":0,\"l\":2,\"a\":0}]}";
            var rt = SidecarSerializer.Deserialize(json);
            Assert.Single(rt.SpanPins);
            Assert.Empty(rt.SpanOverrides); // pas converti ici
        }

        [Fact(DisplayName = "v2 explicite avec rule_pins ne triggers pas la lazy convert")]
        public void V2_explicit_does_not_trigger_lazy_convert()
        {
            // Si v2 contient déjà rule_pins, on ne re-convertit pas les votes.
            var json = "{\"v\":2,"
                       + "\"rule_pins\":[{\"r\":\"canonical-set\",\"a\":1}],"
                       + "\"votes\":{\"two-uppercase\":{\"0\":3}}}";
            var rt = SidecarSerializer.Deserialize(json);
            // rule_pins explicit reste seul (pas de RulePin two-uppercase
            // ajouté depuis votes).
            Assert.Single(rt.RulePins);
            Assert.Equal("canonical-set", rt.RulePins[0].RuleId);
        }
    }
}
