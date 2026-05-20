using System;
using System.Runtime.InteropServices;
using Xunit;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Tests.Host.WordIntegration
{
    /// <summary>
    /// Fixture xUnit partagée pour les tests d'intégration Word : ouvre une
    /// instance <c>Word.Application</c> <c>Visible=false</c> au ctor,
    /// expose helpers pour créer doc / tableau / OMath, ferme proprement
    /// au Dispose.
    ///
    /// <para>Équivalent VSTO de Playwright : pilote une vraie instance
    /// Word via COM pour reproduire les bugs qui n'apparaissent qu'avec
    /// Word réel (cellules de tableau, OMath display, Bookmarks…).</para>
    ///
    /// <para>Skip en CI sans Word installé via le trait
    /// <c>Category=WordIntegration</c> :
    /// <c>dotnet test --filter "Category!=WordIntegration"</c>.</para>
    /// </summary>
    public sealed class WordIntegrationFixture : IDisposable
    {
        public Word.Application App { get; private set; }
        private bool _disposed;

        public WordIntegrationFixture()
        {
            App = new Word.Application();
            App.Visible = false;
            App.DisplayAlerts = Word.WdAlertLevel.wdAlertsNone;
        }

        /// <summary>Crée un doc vide. Caller responsable de fermer le doc
        /// (ou laisse Dispose le faire via Quit).</summary>
        public Word.Document CreateBlankDoc()
        {
            object missing = Type.Missing;
            object visibleArg = false;
            return App.Documents.Add(
                Template: ref missing, NewTemplate: ref missing,
                DocumentType: ref missing, Visible: ref visibleArg);
        }

        /// <summary>Crée un doc avec un tableau <paramref name="rows"/>×<paramref name="cols"/>
        /// inséré au début. Retourne le doc ; le tableau est à
        /// <c>doc.Tables[1]</c>.</summary>
        public Word.Document CreateDocWithTable(int rows, int cols)
        {
            var doc = CreateBlankDoc();
            var rng = doc.Range(0, 0);
            doc.Tables.Add(rng, rows, cols);
            return doc;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                // Ferme tous les docs sans save
                if (App != null)
                {
                    foreach (Word.Document d in App.Documents)
                    {
                        try { d.Close(SaveChanges: false); } catch { }
                    }
                    try { App.Quit(SaveChanges: false); } catch { }
                    try { Marshal.ReleaseComObject(App); } catch { }
                    App = null;
                }
            }
            finally
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
    }
}
