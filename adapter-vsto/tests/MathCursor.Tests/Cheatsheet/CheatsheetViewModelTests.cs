using System.Collections.Generic;
using System.Linq;
using MathCursor.Cheatsheet;
using Xunit;

namespace MathCursor.Tests.Cheatsheet
{
    /// <summary>
    /// Tests pour <see cref="CheatsheetViewModel"/> — filtrage temps réel
    /// (title + stenos[*] + tags) + état collapse/expand par catégorie.
    /// Pure logic, pas de WPF. Schema v2.
    /// </summary>
    public sealed class CheatsheetViewModelTests
    {
        // Document de test à 2 catégories pour cibler des cas précis.
        private static CheatsheetDocument BuildTestDoc()
        {
            return new CheatsheetDocument
            {
                SchemaVersion = 2,
                Categories = new[]
                {
                    new CheatsheetCategory
                    {
                        Id = "fractions",
                        LabelFr = "Équations simples",
                        LabelEn = "Simple equations",
                        Order = 1,
                        Entries = new[]
                        {
                            new CheatsheetEntry { TitleFr = "Fraction", TitleEn = "Fraction", Stenos = new[] { "1/2" }, RenderedLatex = "\\frac{1}{2}", Tags = new[] { "fraction", "demi" } },
                            new CheatsheetEntry { TitleFr = "Puissance", TitleEn = "Power", Stenos = new[] { "x^2", "x²" }, RenderedLatex = "x^2", Tags = new[] { "puissance", "carre" } },
                            new CheatsheetEntry { TitleFr = "Racine carrée", TitleEn = "Square root", Stenos = new[] { "racine x+1", "sqrt x+1" }, RenderedLatex = "\\sqrt{x+1}", Tags = new[] { "racine", "sqrt" } },
                        },
                    },
                    new CheatsheetCategory
                    {
                        Id = "geometry",
                        LabelFr = "Géométrie",
                        LabelEn = "Geometry",
                        Order = 2,
                        Entries = new[]
                        {
                            new CheatsheetEntry { TitleFr = "Vecteur AB", TitleEn = "Vector AB", Stenos = new[] { "vec AB", "vecteur AB" }, RenderedLatex = "\\vec{AB}", Tags = new[] { "vecteur" } },
                            new CheatsheetEntry { TitleFr = "Segment", TitleEn = "Segment", Stenos = new[] { "[AB]" }, RenderedLatex = "[AB]", Tags = new[] { "segment" } },
                        },
                    },
                },
            };
        }

        // ─────────────────────────────────────────────────────────────────
        //  Sans search : tout visible
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "Empty search : toutes les catégories + entrées visibles")]
        public void EmptySearch_AllVisible()
        {
            var vm = new CheatsheetViewModel(BuildTestDoc(), "fr");

            Assert.Equal("", vm.SearchQuery);
            Assert.Equal(2, vm.VisibleCategories.Count);
            Assert.Equal(3, vm.VisibleCategories[0].VisibleEntries.Count);
            Assert.Equal(2, vm.VisibleCategories[1].VisibleEntries.Count);
        }

        [Fact(DisplayName = "Lang fr : labels en français")]
        public void LangFr_LabelsInFrench()
        {
            var vm = new CheatsheetViewModel(BuildTestDoc(), "fr");
            Assert.Equal("Équations simples", vm.VisibleCategories[0].Label);
            Assert.Equal("Géométrie", vm.VisibleCategories[1].Label);
        }

        [Fact(DisplayName = "Lang en : labels en anglais")]
        public void LangEn_LabelsInEnglish()
        {
            var vm = new CheatsheetViewModel(BuildTestDoc(), "en");
            Assert.Equal("Simple equations", vm.VisibleCategories[0].Label);
            Assert.Equal("Geometry", vm.VisibleCategories[1].Label);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Search : filter sur title + stenos[*] + tags + label catégorie
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "Search match steno (1ʳᵉ syntaxe) : 1 entrée visible, sa cat auto-expand")]
        public void SearchMatchesFirstSteno_OnlyMatchingEntryVisible()
        {
            var vm = new CheatsheetViewModel(BuildTestDoc(), "fr");
            vm.SearchQuery = "racine";

            Assert.Single(vm.VisibleCategories);
            Assert.Equal("fractions", vm.VisibleCategories[0].Id);
            Assert.Single(vm.VisibleCategories[0].VisibleEntries);
            Assert.Equal("Racine carrée", vm.VisibleCategories[0].VisibleEntries[0].TitleFr);
            Assert.True(vm.VisibleCategories[0].IsExpanded, "Auto-expand sur match");
        }

        [Fact(DisplayName = "Search match steno alternatif (2ᵉ syntaxe) : entrée visible via stenos[1]")]
        public void SearchMatchesAlternativeSteno_EntryVisible()
        {
            var vm = new CheatsheetViewModel(BuildTestDoc(), "fr");
            vm.SearchQuery = "sqrt";  // 2ᵉ syntaxe de "Racine carrée"

            Assert.Single(vm.VisibleCategories);
            Assert.Single(vm.VisibleCategories[0].VisibleEntries);
            Assert.Equal("Racine carrée", vm.VisibleCategories[0].VisibleEntries[0].TitleFr);
        }

        [Fact(DisplayName = "Search match title : entrée visible via son titre")]
        public void SearchMatchesTitle_EntryVisibleViaTitle()
        {
            var vm = new CheatsheetViewModel(BuildTestDoc(), "fr");
            vm.SearchQuery = "Vecteur";  // matche TitleFr "Vecteur AB"

            Assert.Single(vm.VisibleCategories);
            Assert.Single(vm.VisibleCategories[0].VisibleEntries);
            Assert.Equal("Vecteur AB", vm.VisibleCategories[0].VisibleEntries[0].TitleFr);
        }

        [Fact(DisplayName = "Search match tag : entrée visible via son tag")]
        public void SearchMatchesTag_EntryVisibleViaTag()
        {
            var vm = new CheatsheetViewModel(BuildTestDoc(), "fr");
            vm.SearchQuery = "demi";  // tag de "Fraction" 1/2

            Assert.Single(vm.VisibleCategories);
            Assert.Single(vm.VisibleCategories[0].VisibleEntries);
            Assert.Equal("Fraction", vm.VisibleCategories[0].VisibleEntries[0].TitleFr);
        }

        [Fact(DisplayName = "Search match label catégorie : TOUTES les entrées de la catégorie visibles")]
        public void SearchMatchesCategoryLabel_AllEntriesShown()
        {
            var vm = new CheatsheetViewModel(BuildTestDoc(), "fr");
            vm.SearchQuery = "Géom";

            Assert.Single(vm.VisibleCategories);
            Assert.Equal("geometry", vm.VisibleCategories[0].Id);
            Assert.Equal(2, vm.VisibleCategories[0].VisibleEntries.Count);
        }

        [Fact(DisplayName = "Search case-insensitive : `RACINE` matche `racine`")]
        public void SearchCaseInsensitive()
        {
            var vm = new CheatsheetViewModel(BuildTestDoc(), "fr");
            vm.SearchQuery = "RACINE";

            Assert.Single(vm.VisibleCategories);
            Assert.Single(vm.VisibleCategories[0].VisibleEntries);
        }

        [Fact(DisplayName = "Search trim espaces aux bords")]
        public void SearchTrimsWhitespace()
        {
            var vm = new CheatsheetViewModel(BuildTestDoc(), "fr");
            vm.SearchQuery = "   racine   ";

            Assert.Single(vm.VisibleCategories);
        }

        [Fact(DisplayName = "Search match nothing : 0 catégorie visible")]
        public void SearchMatchesNothing_EmptyResult()
        {
            var vm = new CheatsheetViewModel(BuildTestDoc(), "fr");
            vm.SearchQuery = "blablabla";

            Assert.Empty(vm.VisibleCategories);
        }

        [Fact(DisplayName = "TitleFor : retourne le title localisé selon lang")]
        public void TitleFor_ReturnsLocalizedTitle()
        {
            var vmFr = new CheatsheetViewModel(BuildTestDoc(), "fr");
            var vmEn = new CheatsheetViewModel(BuildTestDoc(), "en");
            var entry = BuildTestDoc().Categories[1].Entries[0];  // Vecteur AB / Vector AB

            Assert.Equal("Vecteur AB", vmFr.TitleFor(entry));
            Assert.Equal("Vector AB", vmEn.TitleFor(entry));
        }

        // ─────────────────────────────────────────────────────────────────
        //  Collapse state : explicite vs auto-expand pendant search
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "Default : toutes les catégories expanded")]
        public void Default_AllExpanded()
        {
            var vm = new CheatsheetViewModel(BuildTestDoc(), "fr");
            Assert.True(vm.IsExpanded("fractions"));
            Assert.True(vm.IsExpanded("geometry"));
            Assert.True(vm.VisibleCategories.All(c => c.IsExpanded));
        }

        [Fact(DisplayName = "SetExpanded(false) : la catégorie passe à collapsed (mais reste visible)")]
        public void SetExpandedFalse_CategoryCollapsed()
        {
            var vm = new CheatsheetViewModel(BuildTestDoc(), "fr");
            vm.SetExpanded("fractions", false);

            Assert.False(vm.IsExpanded("fractions"));
            Assert.False(vm.VisibleCategories[0].IsExpanded);
            Assert.Equal(3, vm.VisibleCategories[0].VisibleEntries.Count);
        }

        [Fact(DisplayName = "Search auto-expand override l'état collapsed pendant la recherche")]
        public void Search_AutoExpandOverridesSavedCollapse()
        {
            var vm = new CheatsheetViewModel(BuildTestDoc(), "fr");
            vm.SetExpanded("fractions", false);
            Assert.False(vm.IsExpanded("fractions"));

            vm.SearchQuery = "racine";

            Assert.True(vm.VisibleCategories[0].IsExpanded);
        }

        [Fact(DisplayName = "Après search clear, l'état saved revient")]
        public void SearchClear_RestoresSavedCollapse()
        {
            var vm = new CheatsheetViewModel(BuildTestDoc(), "fr");
            vm.SetExpanded("fractions", false);
            vm.SearchQuery = "racine";
            vm.SearchQuery = "";

            Assert.False(vm.VisibleCategories[0].IsExpanded);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Persistance : export / import du collapse state
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "GetCollapseState : retourne uniquement les catégories explicitement collapsed")]
        public void GetCollapseState_ReturnsExplicitOnly()
        {
            var vm = new CheatsheetViewModel(BuildTestDoc(), "fr");
            vm.SetExpanded("fractions", false);

            var state = vm.GetCollapseState();
            Assert.True(state.ContainsKey("fractions"));
            Assert.False(state["fractions"]);
            Assert.False(state.ContainsKey("geometry"));
        }

        [Fact(DisplayName = "SetCollapseState : restaure l'état persisté")]
        public void SetCollapseState_RestoresState()
        {
            var vm = new CheatsheetViewModel(BuildTestDoc(), "fr");
            var saved = new Dictionary<string, bool> { { "fractions", false } };
            vm.SetCollapseState(saved);

            Assert.False(vm.IsExpanded("fractions"));
            Assert.True(vm.IsExpanded("geometry"));
        }

        [Fact(DisplayName = "SetCollapseState : ignore les IDs inconnus (sans crash)")]
        public void SetCollapseState_IgnoresUnknownIds()
        {
            var vm = new CheatsheetViewModel(BuildTestDoc(), "fr");
            vm.SetCollapseState(new Dictionary<string, bool> { { "ghost", false } });

            Assert.True(vm.IsExpanded("fractions"));
            Assert.True(vm.IsExpanded("geometry"));
        }
    }
}
