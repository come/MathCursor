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
        private KeyboardInterceptor _keyboard;

        private void ThisAddIn_Startup(object sender, System.EventArgs e)
        {
            try
            {
                _host = new VstoDocumentHost(this.Application);
                _store = new VstoEquationStore(this.Application);
                _surface = new VstoEditorSurface(this.Application);
                _feedback = new VstoUserFeedback();

                _orchestrator = new MathCursorOrchestrator(_host, _store, _surface, _feedback);

                // Hook clavier Tab
                _keyboard = new KeyboardInterceptor
                {
                    OnTabPressed = HandleTabPressed,
                };
                _keyboard.Install();

                _surface.Notify("MathCursor prêt. Tapez une expression puis Tab (ou Alt+M).", HostContract.NotificationLevel.Info);
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
            try { _keyboard?.Dispose(); } catch { }
        }

        /// <summary>
        /// Sur Tab pressé : retourne true si on a effectivement converti (→ consommer le Tab),
        /// false sinon (→ laisser Word insérer un tab normal / tabuler / outdent).
        /// </summary>
        private bool HandleTabPressed()
        {
            if (_orchestrator == null) return false;
            try
            {
                return _orchestrator.TryConvertAtCaret();
            }
            catch (Exception ex)
            {
                _feedback?.LogParsingError("(tab_hook)", ex.Message);
                return false; // en cas d'erreur, ne pas consommer le Tab
            }
        }

        /// <summary>Appelé par le bouton ribbon "Convertir".</summary>
        public void TriggerConversion()
        {
            _orchestrator?.TryConvertAtCaret();
        }

        protected override IRibbonExtensibility CreateRibbonExtensibilityObject()
        {
            return new RibbonCallback();
        }

        #region Code généré par VSTO

        private void InternalStartup()
        {
            this.Startup += new System.EventHandler(ThisAddIn_Startup);
            this.Shutdown += new System.EventHandler(ThisAddIn_Shutdown);
        }

        #endregion
    }
}
