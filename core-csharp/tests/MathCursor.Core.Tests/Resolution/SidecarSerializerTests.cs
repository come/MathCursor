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

        [Fact(DisplayName = "Format JSON : clés courtes (v/pins/votes/r/o/l/a) pour économie payload")]
        public void Format_uses_short_keys()
        {
            var sidecar = new ResolutionSidecar(
                new[] { new SpanPin("two-uppercase", 0, 2, 0) },
                new Dictionary<string, IReadOnlyDictionary<int, int>>
                {
                    ["two-uppercase"] = new Dictionary<int, int> { [0] = 1 },
                });
            var json = SidecarSerializer.Serialize(sidecar);

            Assert.Contains("\"v\":1", json);
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
    }
}
