using System.Collections.Generic;
using MathCursor.Core.Resolution;
using MathCursor.Host;
using Xunit;

namespace MathCursor.Tests.Host
{
    /// <summary>
    /// Tests Phase 4b — <see cref="EquationHandleRegistry"/>.
    /// Cf. ADR <c>2026-05-06-Meta-l4-pipeline-and-session</c>.
    /// </summary>
    public sealed class EquationHandleRegistryTests
    {
        private static EquationHandleRegistry MakeRegistry(
            out List<(string id, int s, int e)> bookmarksCreated,
            out List<string> bookmarksDeleted,
            ResolutionSidecar popupSc = null)
        {
            var created = new List<(string, int, int)>();
            var deleted = new List<string>();
            bookmarksCreated = created;
            bookmarksDeleted = deleted;
            return new EquationHandleRegistry(
                createBookmark: (id, s, e) => created.Add((id, s, e)),
                deleteBookmark: id => deleted.Add(id),
                popupSidecar: () => popupSc ?? ResolutionSidecar.Empty);
        }

        [Fact(DisplayName = "NewHandleId génère des IDs uniques préfixés `eq_`")]
        public void NewHandleId_returns_unique_prefixed_ids()
        {
            var r = MakeRegistry(out _, out _);
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
            var r = MakeRegistry(out _, out _);
            Assert.True(r.GetSidecar("inconnu").IsEmpty);
            Assert.True(r.GetSidecar("").IsEmpty);
            Assert.True(r.GetSidecar(null).IsEmpty);
        }

        [Fact(DisplayName = "Stash avec override non-empty → mémorisé tel quel")]
        public void Stash_with_override_uses_override()
        {
            var r = MakeRegistry(out _, out _);
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
            var r = MakeRegistry(out _, out _, popupSc);

            r.Stash("h1");

            Assert.Same(popupSc, r.GetSidecar("h1"));
        }

        [Fact(DisplayName = "Stash avec override Empty + popup Empty → remove (pas de pollution)")]
        public void Stash_all_empty_removes_entry()
        {
            var sc = new ResolutionSidecar(
                new[] { new SpanPin("two-uppercase", 0, 2, 0) },
                new Dictionary<string, IReadOnlyDictionary<int, int>>());
            var r = MakeRegistry(out _, out _);

            r.Stash("h1", sc);
            Assert.False(r.GetSidecar("h1").IsEmpty);

            r.Stash("h1", ResolutionSidecar.Empty); // empty + popup empty
            Assert.True(r.GetSidecar("h1").IsEmpty);
        }

        [Fact(DisplayName = "Restore set le sidecar directement (pas de fallback)")]
        public void Restore_sets_sidecar_directly()
        {
            var r = MakeRegistry(out _, out _);
            var sc = new ResolutionSidecar(
                new[] { new SpanPin("two-uppercase", 0, 2, 0) },
                new Dictionary<string, IReadOnlyDictionary<int, int>>());

            r.Restore("h1", sc);
            Assert.Same(sc, r.GetSidecar("h1"));

            // Restore avec Empty → ne fait rien (préserve l'ancien)
            r.Restore("h1", ResolutionSidecar.Empty);
            Assert.Same(sc, r.GetSidecar("h1"));
        }

        [Fact(DisplayName = "Forget supprime le sidecar mémoire ET appelle delete bookmark")]
        public void Forget_removes_sidecar_and_deletes_bookmark()
        {
            var r = MakeRegistry(out _, out var deleted);
            var sc = new ResolutionSidecar(
                new[] { new SpanPin("two-uppercase", 0, 2, 0) },
                new Dictionary<string, IReadOnlyDictionary<int, int>>());
            r.Stash("h1", sc);

            r.Forget("h1");

            Assert.True(r.GetSidecar("h1").IsEmpty);
            Assert.Contains("h1", deleted);
        }

        [Fact(DisplayName = "CreateBookmark délègue avec les bonnes bornes")]
        public void CreateBookmark_delegates_with_bounds()
        {
            var r = MakeRegistry(out var created, out _);

            r.CreateBookmark("h1", 10, 25);

            Assert.Single(created);
            Assert.Equal(("h1", 10, 25), created[0]);
        }

        [Fact(DisplayName = "Forget tolérant aux exceptions du délégué deleteBookmark")]
        public void Forget_tolerates_exceptions_from_delete_delegate()
        {
            var r = new EquationHandleRegistry(
                createBookmark: (_, _, _) => { },
                deleteBookmark: _ => throw new System.InvalidOperationException("boom"),
                popupSidecar: () => ResolutionSidecar.Empty);

            // Ne doit pas throw
            r.Forget("h1");
        }
    }
}
