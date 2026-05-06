using System.Linq;
using MathCursor.Cheatsheet;
using Xunit;

namespace MathCursor.Tests.Cheatsheet
{
    /// <summary>
    /// Tests pour <see cref="CheatsheetData"/> — parsing JSON + validation
    /// d'intégrité (schema v2). Pas de dépendance Word/WPF, logique pure
    /// testable.
    /// </summary>
    public sealed class CheatsheetDataTests
    {
        // ─────────────────────────────────────────────────────────────────
        //  Parse : JSON minimal valide (v2)
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "Parse : 1 catégorie, 1 entrée minimale (v2) → document valide")]
        public void Parse_MinimalValid_ReturnsDocument()
        {
            const string json = @"{
              ""schema_version"": 2,
              ""categories"": [
                {
                  ""id"": ""basic"",
                  ""label_fr"": ""Basique"",
                  ""label_en"": ""Basic"",
                  ""order"": 1,
                  ""entries"": [
                    {
                      ""title_fr"": ""Fraction"",
                      ""title_en"": ""Fraction"",
                      ""stenos"": [""1/2""],
                      ""rendered_latex"": ""\\frac{1}{2}"",
                      ""tags"": [""fraction""]
                    }
                  ]
                }
              ]
            }";

            var doc = CheatsheetData.Parse(json);

            Assert.NotNull(doc);
            Assert.Equal(2, doc.SchemaVersion);
            Assert.Single(doc.Categories);
            var cat = doc.Categories[0];
            Assert.Equal("basic", cat.Id);
            Assert.Equal("Basique", cat.LabelFr);
            Assert.Equal("Basic", cat.LabelEn);
            Assert.Equal(1, cat.Order);
            Assert.Single(cat.Entries);
            var entry = cat.Entries[0];
            Assert.Equal("Fraction", entry.TitleFr);
            Assert.Equal("Fraction", entry.TitleEn);
            Assert.Single(entry.Stenos);
            Assert.Equal("1/2", entry.Stenos[0]);
            Assert.Equal(@"\frac{1}{2}", entry.RenderedLatex);
            Assert.Single(entry.Tags);
            Assert.Equal("fraction", entry.Tags[0]);
        }

        [Fact(DisplayName = "Parse : entrée multi-stenos préservée")]
        public void Parse_MultipleStenos_Preserved()
        {
            const string json = @"{
              ""schema_version"": 2,
              ""categories"": [{
                ""id"": ""x"", ""label_fr"": ""X"", ""label_en"": ""X"", ""order"": 1,
                ""entries"": [{
                  ""title_fr"": ""Vecteur AB"",
                  ""title_en"": ""Vector AB"",
                  ""stenos"": [""vec AB"", ""AB"", ""vecteur AB""],
                  ""rendered_latex"": ""\\vec{AB}"",
                  ""tags"": []
                }]
              }]
            }";

            var doc = CheatsheetData.Parse(json);
            var entry = doc.Categories[0].Entries[0];
            Assert.Equal(3, entry.Stenos.Length);
            Assert.Equal("vec AB", entry.Stenos[0]);
            Assert.Equal("AB", entry.Stenos[1]);
            Assert.Equal("vecteur AB", entry.Stenos[2]);
        }

        [Fact(DisplayName = "Parse : null/empty → null (pas de crash)")]
        public void Parse_NullEmpty_ReturnsNull()
        {
            Assert.Null(CheatsheetData.Parse(null));
            Assert.Null(CheatsheetData.Parse(""));
        }

        // ─────────────────────────────────────────────────────────────────
        //  Validate : intégrité document
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "Validate : doc null → 1 issue (`document null`)")]
        public void Validate_NullDoc_ReturnsIssue()
        {
            var issues = CheatsheetData.Validate(null);
            Assert.Single(issues);
            Assert.Contains("null", issues[0]);
        }

        [Fact(DisplayName = "Validate : schema_version != 2 → issue détectée")]
        public void Validate_WrongSchemaVersion_ReturnsIssue()
        {
            var doc = new CheatsheetDocument
            {
                SchemaVersion = 1,
                Categories = new[] {
                    new CheatsheetCategory {
                        Id = "x", LabelFr = "X", LabelEn = "X", Order = 1,
                        Entries = new[] {
                            new CheatsheetEntry { TitleFr = "T", TitleEn = "T", Stenos = new[] { "s" }, RenderedLatex = "r", Tags = new string[0] }
                        }
                    }
                }
            };
            var issues = CheatsheetData.Validate(doc);
            Assert.Contains(issues, i => i.Contains("schema_version"));
        }

        [Fact(DisplayName = "Validate : doc sans catégories → 1 issue")]
        public void Validate_NoCategories_ReturnsIssue()
        {
            var doc = new CheatsheetDocument { SchemaVersion = 2, Categories = null };
            var issues = CheatsheetData.Validate(doc);
            Assert.Single(issues);
            Assert.Contains("aucune catégorie", issues[0]);
        }

        [Fact(DisplayName = "Validate : entrée sans stenos → 1 issue")]
        public void Validate_EntryWithoutStenos_ReturnsIssue()
        {
            var doc = new CheatsheetDocument
            {
                SchemaVersion = 2,
                Categories = new[] {
                    new CheatsheetCategory {
                        Id = "x", LabelFr = "X", LabelEn = "X", Order = 1,
                        Entries = new[] {
                            new CheatsheetEntry { TitleFr = "T", TitleEn = "T", Stenos = new string[0], RenderedLatex = "ok", Tags = new string[0] }
                        }
                    }
                }
            };
            var issues = CheatsheetData.Validate(doc);
            Assert.Contains(issues, i => i.Contains("stenos vide"));
        }

        [Fact(DisplayName = "Validate : entrée avec steno vide dans le tableau → 1 issue")]
        public void Validate_EntryWithEmptyStenoInArray_ReturnsIssue()
        {
            var doc = new CheatsheetDocument
            {
                SchemaVersion = 2,
                Categories = new[] {
                    new CheatsheetCategory {
                        Id = "x", LabelFr = "X", LabelEn = "X", Order = 1,
                        Entries = new[] {
                            new CheatsheetEntry { TitleFr = "T", TitleEn = "T", Stenos = new[] { "ok", "" }, RenderedLatex = "r", Tags = new string[0] }
                        }
                    }
                }
            };
            var issues = CheatsheetData.Validate(doc);
            Assert.Contains(issues, i => i.Contains("stenos[1] vide"));
        }

        [Fact(DisplayName = "Validate : entrée sans rendered_latex → 1 issue")]
        public void Validate_EntryWithoutRenderedLatex_ReturnsIssue()
        {
            var doc = new CheatsheetDocument
            {
                SchemaVersion = 2,
                Categories = new[] {
                    new CheatsheetCategory {
                        Id = "x", LabelFr = "X", LabelEn = "X", Order = 1,
                        Entries = new[] {
                            new CheatsheetEntry { TitleFr = "T", TitleEn = "T", Stenos = new[] { "s" }, RenderedLatex = "", Tags = new string[0] }
                        }
                    }
                }
            };
            var issues = CheatsheetData.Validate(doc);
            Assert.Contains(issues, i => i.Contains("rendered_latex vide"));
        }

        [Fact(DisplayName = "Validate : entrée sans title_fr → 1 issue")]
        public void Validate_EntryWithoutTitleFr_ReturnsIssue()
        {
            var doc = new CheatsheetDocument
            {
                SchemaVersion = 2,
                Categories = new[] {
                    new CheatsheetCategory {
                        Id = "x", LabelFr = "X", LabelEn = "X", Order = 1,
                        Entries = new[] {
                            new CheatsheetEntry { TitleFr = "", TitleEn = "T", Stenos = new[] { "s" }, RenderedLatex = "r", Tags = new string[0] }
                        }
                    }
                }
            };
            var issues = CheatsheetData.Validate(doc);
            Assert.Contains(issues, i => i.Contains("title_fr vide"));
        }

        [Fact(DisplayName = "Validate : doc cheatsheet réel embarqué → 0 issue + 7 catégories")]
        public void Validate_EmbeddedCheatsheet_ZeroIssues()
        {
            var doc = CheatsheetData.LoadEmbedded();

            Assert.NotNull(doc);
            Assert.Equal(2, doc.SchemaVersion);
            Assert.Equal(7, doc.Categories.Length);
            Assert.True(doc.Categories.All(c => c.Entries.Length >= 3),
                "Toutes les catégories doivent avoir au moins 3 entrées");

            var issues = CheatsheetData.Validate(doc);
            Assert.Empty(issues);
        }

        [Fact(DisplayName = "Cheatsheet réelle : ordres 1..7 distincts")]
        public void EmbeddedCheatsheet_OrdersAreDistinctSequential()
        {
            var doc = CheatsheetData.LoadEmbedded();
            var orders = doc.Categories.Select(c => c.Order).OrderBy(o => o).ToArray();
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7 }, orders);
        }

        [Fact(DisplayName = "Cheatsheet réelle : tous les IDs distincts")]
        public void EmbeddedCheatsheet_IdsAreUnique()
        {
            var doc = CheatsheetData.LoadEmbedded();
            var ids = doc.Categories.Select(c => c.Id).ToArray();
            Assert.Equal(ids.Length, ids.Distinct().Count());
        }

        [Fact(DisplayName = "Cheatsheet réelle : toutes les entrées ont au moins 1 steno")]
        public void EmbeddedCheatsheet_AllEntriesHaveAtLeastOneSteno()
        {
            var doc = CheatsheetData.LoadEmbedded();
            foreach (var cat in doc.Categories)
            {
                foreach (var e in cat.Entries)
                {
                    Assert.True(e.Stenos != null && e.Stenos.Length > 0,
                        $"Entrée '{e.TitleFr}' (cat '{cat.Id}') doit avoir au moins 1 steno");
                }
            }
        }
    }
}
