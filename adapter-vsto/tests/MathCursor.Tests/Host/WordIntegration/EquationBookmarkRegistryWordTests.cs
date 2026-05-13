using MathCursor.Host.Bookmarks;
using Xunit;
using Xunit.Abstractions;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Tests.Host.WordIntegration
{
    /// <summary>
    /// Tests d'intégration Word pour <see cref="EquationBookmarkRegistry"/>.
    /// Reproduit le bug 2026-05-13 (log user 13:42:59) : en cellule de
    /// tableau, <c>doc.Range(absStart, absEnd)</c> + <c>Bookmarks.Add</c>
    /// jette « Valeur en dehors des limites » → bookmark jamais créé →
    /// cross-merge cassé (le merger ne retrouve plus l'OMath précédente).
    ///
    /// <para>Catégorisé <c>WordIntegration</c> : skip en CI sans Word via
    /// <c>--filter "Category!=WordIntegration"</c>.</para>
    /// </summary>
    [Trait("Category", "WordIntegration")]
    public sealed class EquationBookmarkRegistryWordTests : IClassFixture<WordIntegrationFixture>
    {
        private readonly WordIntegrationFixture _fixture;
        private readonly ITestOutputHelper _output;

        public EquationBookmarkRegistryWordTests(
            WordIntegrationFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [Fact(DisplayName = "Bug 2026-05-13 : bookmark sur OMath dans cellule de tableau persiste")]
        public void Create_BookmarkInsideTableCell_PersistsInDocBookmarks()
        {
            var doc = _fixture.CreateDocWithTable(rows: 2, cols: 2);
            try
            {
                // Insère une OMath simple dans la cellule [1,1] :
                // 1) Place le texte "f" dans la cellule
                // 2) Convertit en OMath via le pipeline Word natif
                var cell = doc.Tables[1].Cell(1, 1);
                var insertStart = cell.Range.Start;
                var insertRange = doc.Range(insertStart, insertStart);
                insertRange.Text = "f";
                int omEnd = insertStart + 1;
                var mathRange = doc.Range(insertStart, omEnd);
                mathRange.OMaths.Add(mathRange);
                mathRange.OMaths.BuildUp();

                // Récupère l'OMath fraîchement créée pour ses VRAIES bornes
                // (Word peut ajuster start/end après BuildUp).
                Word.OMath om = null;
                foreach (Word.OMath o in doc.OMaths)
                {
                    if (o.Range.Start >= insertStart && o.Range.End <= cell.Range.End)
                    { om = o; break; }
                }
                Assert.NotNull(om);
                _output.WriteLine($"OMath range = [{om.Range.Start}, {om.Range.End}]");
                _output.WriteLine($"Cell range  = [{cell.Range.Start}, {cell.Range.End}]");

                string captured = null;
                var registry = new EquationBookmarkRegistry(
                    () => doc,
                    msg => { captured = msg; _output.WriteLine("LOG: " + msg); });

                registry.Create("test_handle", om.Range.Start, om.Range.End);

                if (captured != null)
                    _output.WriteLine("Diagnostic captured: " + captured);

                // RED actuel : Bookmarks.Exists retourne false car Word a
                // refusé doc.Range(start,end) en cellule.
                // GREEN après fix : retourne true.
                bool exists = doc.Bookmarks.Exists("mcEq_test_handle");
                _output.WriteLine($"Bookmark exists = {exists}");

                Assert.True(exists,
                    "Le bookmark doit être créé même en cellule de tableau. " +
                    "Log diag : " + (captured ?? "<rien>"));
            }
            finally
            {
                try { doc.Close(SaveChanges: false); } catch { }
            }
        }
    }
}
