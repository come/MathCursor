using System;
using Microsoft.Office.Core;
using MathCursor.Core.Orchestration;
using MathCursor.Host;

namespace MathCursor
{
    public partial class ThisAddIn
    {
        private VstoDocumentHost _host;
        private VstoEquationStore _store;
        private VstoEditorSurface _surface;
        private VstoUserFeedback _feedback;
        private MathCursorOrchestrator _orchestrator;

        private void ThisAddIn_Startup(object sender, System.EventArgs e)
        {
            try
            {
                _host = new VstoDocumentHost(this.Application);
                _store = new VstoEquationStore(this.Application);
                _surface = new VstoEditorSurface(this.Application);
                _feedback = new VstoUserFeedback();

                _orchestrator = new MathCursorOrchestrator(_host, _store, _surface, _feedback);

                _surface.Notify("MathCursor prêt. Tapez une expression puis Alt+M.", HostContract.NotificationLevel.Info);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    "Échec du démarrage MathCursor :\n" + ex.Message,
                    "MathCursor",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        private void ThisAddIn_Shutdown(object sender, System.EventArgs e)
        {
        }

        /// <summary>
        /// Appelé par le bouton ribbon "Convertir". Si l'add-in n'est pas encore
        /// prêt (startup pas terminé), ça ne fait rien.
        /// </summary>
        public void TriggerConversion()
        {
            _host?.TriggerConversion();
        }

        /// <summary>
        /// Fournit le callback ribbon à Word. VSTO l'appelle AVANT Startup,
        /// donc on ne peut pas dépendre de this.Application ici.
        /// </summary>
        protected override IRibbonExtensibility CreateRibbonExtensibilityObject()
        {
            return new RibbonCallback();
        }

        #region Code généré par VSTO

        /// <summary>
        /// Méthode requise pour la prise en charge du concepteur - ne modifiez pas
        /// le contenu de cette méthode avec l'éditeur de code.
        /// </summary>
        private void InternalStartup()
        {
            this.Startup += new System.EventHandler(ThisAddIn_Startup);
            this.Shutdown += new System.EventHandler(ThisAddIn_Shutdown);
        }

        #endregion
    }
}
