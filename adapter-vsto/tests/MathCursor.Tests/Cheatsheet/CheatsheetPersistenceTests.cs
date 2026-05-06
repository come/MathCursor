using System.Linq;
using MathCursor.Cheatsheet;
using Xunit;

namespace MathCursor.Tests.Cheatsheet
{
    /// <summary>
    /// Tests pour <see cref="CheatsheetPersistence"/> — couches pures
    /// (Serialize / Deserialize / Capture / ApplyToViewModel). Les méthodes
    /// <c>Load</c> / <c>Save</c> qui touchent IsolatedStorage sont testées
    /// manuellement (impact disque user, not unit-tested).
    /// </summary>
    public sealed class CheatsheetPersistenceTests
    {
        private static CheatsheetDocument BuildDoc()
        {
            return new CheatsheetDocument
            {
                SchemaVersion = 2,
                Categories = new[]
                {
                    new CheatsheetCategory { Id = "a", LabelFr = "A", LabelEn = "A", Order = 1, Entries = new[] { new CheatsheetEntry { TitleFr = "A1", TitleEn = "A1", Stenos = new[] { "x" }, RenderedLatex = "x", Tags = new string[0] } } },
                    new CheatsheetCategory { Id = "b", LabelFr = "B", LabelEn = "B", Order = 2, Entries = new[] { new CheatsheetEntry { TitleFr = "B1", TitleEn = "B1", Stenos = new[] { "y" }, RenderedLatex = "y", Tags = new string[0] } } },
                    new CheatsheetCategory { Id = "c", LabelFr = "C", LabelEn = "C", Order = 3, Entries = new[] { new CheatsheetEntry { TitleFr = "C1", TitleEn = "C1", Stenos = new[] { "z" }, RenderedLatex = "z", Tags = new string[0] } } },
                },
            };
        }

        // ─────────────────────────────────────────────────────────────────
        //  Default state
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "Default : pane fermé, largeur 380, aucune cat collapsed")]
        public void Default_HasSensibleValues()
        {
            var s = CheatsheetState.Default();
            Assert.False(s.PaneOpen);
            Assert.Equal(380, s.PaneWidth);
            Assert.NotNull(s.CollapsedCategories);
            Assert.Empty(s.CollapsedCategories);
            Assert.Equal(1, s.SchemaVersion);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Round-trip Serialize / Deserialize
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "Serialize → Deserialize : round-trip identique")]
        public void RoundTrip_PreservesState()
        {
            var original = new CheatsheetState
            {
                SchemaVersion = 1,
                PaneOpen = true,
                PaneWidth = 420,
                CollapsedCategories = new[] { "fractions", "geometry" },
            };

            string json = CheatsheetPersistence.Serialize(original);
            var restored = CheatsheetPersistence.Deserialize(json);

            Assert.Equal(original.PaneOpen, restored.PaneOpen);
            Assert.Equal(original.PaneWidth, restored.PaneWidth);
            Assert.Equal(original.SchemaVersion, restored.SchemaVersion);
            Assert.Equal(original.CollapsedCategories, restored.CollapsedCategories);
        }

        [Fact(DisplayName = "Deserialize null/empty → Default()")]
        public void Deserialize_NullEmpty_ReturnsDefault()
        {
            Assert.NotNull(CheatsheetPersistence.Deserialize(null));
            Assert.Equal(380, CheatsheetPersistence.Deserialize(null).PaneWidth);
            Assert.Equal(380, CheatsheetPersistence.Deserialize("").PaneWidth);
        }

        [Fact(DisplayName = "Deserialize JSON corrompu → Default() (no-throw)")]
        public void Deserialize_GarbageJson_ReturnsDefault()
        {
            var s = CheatsheetPersistence.Deserialize("{not_valid_json");
            Assert.NotNull(s);
            Assert.Equal(380, s.PaneWidth);
        }

        [Fact(DisplayName = "Deserialize sans CollapsedCategories → array vide (pas null)")]
        public void Deserialize_MissingArrayField_ReturnsEmptyArray()
        {
            // JSON sans le champ collapsed_categories
            var s = CheatsheetPersistence.Deserialize("{\"schema_version\":1,\"pane_open\":true,\"pane_width\":350}");
            Assert.NotNull(s.CollapsedCategories);
            Assert.Empty(s.CollapsedCategories);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Capture : VM → State
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "Capture : VM avec 1 catégorie collapsed → state.CollapsedCategories = [id]")]
        public void Capture_FromVM_ReflectsCollapseState()
        {
            var vm = new CheatsheetViewModel(BuildDoc(), "fr");
            vm.SetExpanded("a", false);

            var state = CheatsheetPersistence.Capture(vm, paneOpen: true, paneWidth: 420);

            Assert.True(state.PaneOpen);
            Assert.Equal(420, state.PaneWidth);
            Assert.Single(state.CollapsedCategories);
            Assert.Equal("a", state.CollapsedCategories[0]);
        }

        [Fact(DisplayName = "Capture : VM sans collapse → CollapsedCategories vide")]
        public void Capture_NoCollapse_EmptyArray()
        {
            var vm = new CheatsheetViewModel(BuildDoc(), "fr");
            var state = CheatsheetPersistence.Capture(vm, paneOpen: false, paneWidth: 380);
            Assert.Empty(state.CollapsedCategories);
        }

        [Fact(DisplayName = "Capture : VM null → state cohérent (no-throw)")]
        public void Capture_NullVm_ReturnsConsistentState()
        {
            var state = CheatsheetPersistence.Capture(null, paneOpen: true, paneWidth: 380);
            Assert.NotNull(state);
            Assert.Empty(state.CollapsedCategories);
            Assert.True(state.PaneOpen);
        }

        // ─────────────────────────────────────────────────────────────────
        //  ApplyToViewModel : State → VM
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "ApplyToViewModel : state.CollapsedCategories=[a] → VM cat 'a' collapsed")]
        public void ApplyToViewModel_RestoresCollapseState()
        {
            var vm = new CheatsheetViewModel(BuildDoc(), "fr");
            var state = new CheatsheetState
            {
                SchemaVersion = 1,
                PaneOpen = true,
                PaneWidth = 380,
                CollapsedCategories = new[] { "a", "c" },
            };

            CheatsheetPersistence.ApplyToViewModel(state, vm);

            Assert.False(vm.IsExpanded("a"));
            Assert.True(vm.IsExpanded("b"));   // pas dans la list → default expanded
            Assert.False(vm.IsExpanded("c"));
        }

        [Fact(DisplayName = "ApplyToViewModel : state null → no-op (pas de crash, état VM inchangé)")]
        public void ApplyToViewModel_NullState_NoOp()
        {
            var vm = new CheatsheetViewModel(BuildDoc(), "fr");
            vm.SetExpanded("a", false);
            CheatsheetPersistence.ApplyToViewModel(null, vm);
            Assert.False(vm.IsExpanded("a"));  // état préservé
        }

        // ─────────────────────────────────────────────────────────────────
        //  Round-trip complet : VM → Capture → Serialize → Deserialize →
        //  ApplyToViewModel → équivalent au VM original
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "Round-trip complet VM → JSON → VM : collapse state préservé")]
        public void FullRoundTrip_VmCollapseState_Preserved()
        {
            // VM original avec catégories b et c collapsed
            var vmOriginal = new CheatsheetViewModel(BuildDoc(), "fr");
            vmOriginal.SetExpanded("b", false);
            vmOriginal.SetExpanded("c", false);

            // Capture → JSON → Deserialize → Apply
            var captured = CheatsheetPersistence.Capture(vmOriginal, true, 420);
            string json = CheatsheetPersistence.Serialize(captured);
            var restored = CheatsheetPersistence.Deserialize(json);

            var vmRestored = new CheatsheetViewModel(BuildDoc(), "fr");
            CheatsheetPersistence.ApplyToViewModel(restored, vmRestored);

            Assert.True(vmRestored.IsExpanded("a"));
            Assert.False(vmRestored.IsExpanded("b"));
            Assert.False(vmRestored.IsExpanded("c"));
        }
    }
}
