using System.Collections.Generic;
using MathCursor.Core.Resolution;
using MathCursor.Host;
using Xunit;

namespace MathCursor.Tests.Host
{
    /// <summary>
    /// Tests <see cref="EquationHandleRegistry"/> — registry in-memory pour
    /// les sidecars indexés par handleId. Phase B (2026-05-18) : plus de
    /// délégués bookmark (CC.Tag a remplacé les bookmarks).
    /// </summary>
    public sealed class EquationHandleRegistryTests
    {
        private static EquationHandleRegistry MakeRegistry(ResolutionSidecar popupSc = null)
            => new EquationHandleRegistry(popupSidecar: () => popupSc ?? ResolutionSidecar.Empty);

        [Fact(DisplayName = "NewHandleId génère des IDs uniques préfixés `eq_`")]
        public void NewHandleId_returns_unique_prefixed_ids()
        {
            var r = MakeRegistry();
            var ids = new HashSet<string>();
            for (int i = 0; i < 100; i++)
            {
                var id = r.NewHandleId();
                Assert.StartsWith("eq_", id);
                Assert.True(ids.Add(id), $"duplicate id: {id}");
            }
        }

        [Fact(DisplayName = "GetSidecar pour un handle inconnu → Empty")]
        public void GetSidecar_unknown_handle_returns_empty()
        {
            var r = MakeRegistry();
            Assert.True(r.GetSidecar("inconnu").IsEmpty);
            Assert.True(r.GetSidecar("").IsEmpty);
            Assert.True(r.GetSidecar(null).IsEmpty);
        }

        [Fact(DisplayName = "Stash avec override non-empty → mémorisé tel quel")]
        public void Stash_with_override_uses_override()
        {
            var r = MakeRegistry();
            var sc = new ResolutionSidecar(
                new[] { new SpanPin("two-uppercase", 0, 2, 0) },
                new Dictionary<string, IReadOnlyDictionary<int, int>>());

            r.Stash("h1", sc);

            Assert.Same(sc, r.GetSidecar("h1"));
        }

        [Fact(DisplayName = "Stash sans override → fallback popup.CurrentSidecar")]
        public void Stash_without_override_falls_back_to_popup()
        {
            var popupSc = new ResolutionSidecar(
                new[] { new SpanPin("two-uppercase", 5, 2, 1) },
                new Dictionary<string, IReadOnlyDictionary<int, int>>());
            var r = MakeRegistry(popupSc);

            r.Stash("h1");

            Assert.Same(popupSc, r.GetSidecar("h1"));
        }

        [Fact(DisplayName = "Stash avec override Empty + popup Empty → remove (pas de pollution)")]
        public void Stash_all_empty_removes_entry()
        {
            var sc = new ResolutionSidecar(
                new[] { new SpanPin("two-uppercase", 0, 2, 0) },
                new Dictionary<string, IReadOnlyDictionary<int, int>>());
            var r = MakeRegistry();

            r.Stash("h1", sc);
            Assert.False(r.GetSidecar("h1").IsEmpty);

            r.Stash("h1", ResolutionSidecar.Empty);
            Assert.True(r.GetSidecar("h1").IsEmpty);
        }

        [Fact(DisplayName = "Restore set le sidecar directement (pas de fallback)")]
        public void Restore_sets_sidecar_directly()
        {
            var r = MakeRegistry();
            var sc = new ResolutionSidecar(
                new[] { new SpanPin("two-uppercase", 0, 2, 0) },
                new Dictionary<string, IReadOnlyDictionary<int, int>>());

            r.Restore("h1", sc);
            Assert.Same(sc, r.GetSidecar("h1"));

            r.Restore("h1", ResolutionSidecar.Empty);
            Assert.Same(sc, r.GetSidecar("h1"));
        }

        [Fact(DisplayName = "Forget supprime le sidecar mémoire")]
        public void Forget_removes_sidecar()
        {
            var r = MakeRegistry();
            var sc = new ResolutionSidecar(
                new[] { new SpanPin("two-uppercase", 0, 2, 0) },
                new Dictionary<string, IReadOnlyDictionary<int, int>>());
            r.Stash("h1", sc);

            r.Forget("h1");

            Assert.True(r.GetSidecar("h1").IsEmpty);
        }
    }
}
