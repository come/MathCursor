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
        private RibbonCallback _ribbonCallback;

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
        /// Expose le Ribbon callback à Word. Appelé par VSTO au démarrage
        /// APRÈS ThisAddIn_Startup — mais comme _host peut ne pas être
        /// encore initialisé, on crée le callback à la demande.
        /// </summary>
        protected override IRibbonExtensibility CreateRibbonExtensibilityObject()
        {
            // Si Startup n'a pas encore initialisé _host, forcer l'initialisation maintenant
            if (_host == null)
            {
                _host = new VstoDocumentHost(this.Application);
                _store = new VstoEquationStore(this.Application);
                _surface = new VstoEditorSurface(this.Application);
                _feedback = new VstoUserFeedback();
                _orchestrator = new MathCursorOrchestrator(_host, _store, _surface, _feedback);
            }
            _ribbonCallback = new RibbonCallback(_host);
            return _ribbonCallback;
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
