using System.Collections.Generic;
using MathCursor.Core.Resolution;
using Xunit;

namespace MathCursor.Core.Tests.Resolution
{
    /// <summary>
    /// Tests fondamentaux de la POCO <see cref="ResolutionSidecar"/> et
    /// <see cref="SpanPin"/>. Vérifie le contrat de base (lookups, equals,
    /// shift d'offset) qui sera consommé par <see cref="SidecarMerger"/>
    /// et la nouvelle API resolver.
    ///
    /// Ces tests doivent **passer GREEN dès maintenant** — ils sécurisent la
    /// couche fondation qu'on va construire dessus.
    /// </summary>
    public sealed class ResolutionSidecarTests
    {
        // ───────── SpanPin ─────────────────────────────────────────────

        [Fact(DisplayName = "SpanPin.WithOffsetShift décale uniquement Offset")]
        public void SpanPin_offset_shift_only_changes_offset()
        {
            var p = new SpanPin("two-uppercase", 0, 2, 0);
            var shifted = p.WithOffsetShift(9);
            Assert.Equal("two-uppercase", shifted.Rule);
            Assert.Equal(9, shifted.Offset);
            Assert.Equal(2, shifted.Len);
            Assert.Equal(0, shifted.AltIdx);
        }

        [Fact(DisplayName = "SpanPin equality basée sur tous les champs")]
        public void SpanPin_equality()
        {
            var a = new SpanPin("two-uppercase", 0, 2, 0);
            var b = new SpanPin("two-uppercase", 0, 2, 0);
            var c = new SpanPin("two-uppercase", 3, 2, 0);
            Assert.Equal(a, b);
            Assert.NotEqual(a, c);
        }

        // ───────── ResolutionSidecar ───────────────────────────────────

        [Fact(DisplayName = "Empty sidecar : pas de pins, pas de votes, IsEmpty true")]
        public void Empty_sidecar_is_empty()
        {
            Assert.Empty(ResolutionSidecar.Empty.SpanPins);
            Assert.Empty(ResolutionSidecar.Empty.ZoneVotes);
            Assert.True(ResolutionSidecar.Empty.IsEmpty);
        }

        [Fact(DisplayName = "FindPin retourne le pin qui match exactement (rule + offset + len)")]
        public void FindPin_returns_exact_match()
        {
            var sc = new ResolutionSidecar(
                new[]
                {
                    new SpanPin("two-uppercase", 0, 2, 0),
                    new SpanPin("two-uppercase", 3, 2, 0),
                },
                new Dictionary<string, IReadOnlyDictionary<int, int>>());
            var hit = sc.FindPin("two-uppercase", 3, 2);
            Assert.NotNull(hit);
            Assert.Equal(0, hit.AltIdx);
        }

        [Fact(DisplayName = "FindPin retourne null si rule, offset ou len ne match pas")]
        public void FindPin_no_match_returns_null()
        {
            var sc = new ResolutionSidecar(
                new[] { new SpanPin("two-uppercase", 0, 2, 0) },
                new Dictionary<string, IReadOnlyDictionary<int, int>>());
            Assert.Null(sc.FindPin("two-uppercase", 5, 2));      // mauvais offset
            Assert.Null(sc.FindPin("two-uppercase", 0, 3));      // mauvais len
            Assert.Null(sc.FindPin("three-uppercase", 0, 2));    // mauvaise rule
            Assert.Null(sc.FindPin(null, 0, 2));
            Assert.Null(sc.FindPin("", 0, 2));
        }

        [Fact(DisplayName = "GetVote retourne le compte ou 0 si absent")]
        public void GetVote_returns_count_or_zero()
        {
            var votes = new Dictionary<string, IReadOnlyDictionary<int, int>>
            {
                ["two-uppercase"] = new Dictionary<int, int> { [0] = 3, [1] = 1 },
            };
            var sc = new ResolutionSidecar(new List<SpanPin>(), votes);

            Assert.Equal(3, sc.GetVote("two-uppercase", 0));
            Assert.Equal(1, sc.GetVote("two-uppercase", 1));
            Assert.Equal(0, sc.GetVote("two-uppercase", 2));     // alt absente
            Assert.Equal(0, sc.GetVote("three-uppercase", 0));   // rule absente
            Assert.Equal(0, sc.GetVote(null, 0));
        }

        [Fact(DisplayName = "IsEmpty false dès qu'il y a au moins 1 pin OU 1 vote")]
        public void IsEmpty_only_when_no_pins_no_votes()
        {
            var withPin = new ResolutionSidecar(
                new[] { new SpanPin("r", 0, 1, 0) },
                new Dictionary<string, IReadOnlyDictionary<int, int>>());
            Assert.False(withPin.IsEmpty);

            var withVote = new ResolutionSidecar(
                new List<SpanPin>(),
                new Dictionary<string, IReadOnlyDictionary<int, int>>
                {
                    ["r"] = new Dictionary<int, int> { [0] = 1 },
                });
            Assert.False(withVote.IsEmpty);
        }
    }
}
