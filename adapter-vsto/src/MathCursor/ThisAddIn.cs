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
        private SuggestionService _suggestions;

        private void ThisAddIn_Startup(object sender, System.EventArgs e)
        {
            try
            {
                _host = new VstoDocumentHost(this.Application);
                _store = new VstoEquationStore(this.Application);
                _surface = new VstoEditorSurface(this.Application);
                _feedback = new VstoUserFeedback();

                _orchestrator = new MathCursorOrchestrator(_host, _store, _surface, _feedback);

                // Popup de suggestions ancrée au caret
                _suggestions = new SuggestionService(this.Application);
                _suggestions.Install();

                // Hook clavier global pour Tab + Enter + nav popup
                _keyboard = new KeyboardInterceptor
                {
                    OnTabPressed = HandleTabPressed,
                    OnEnterPressed = HandleEnterPressed,
                    OnUpPressed = HandleUpPressed,
                    OnDownPressed = HandleDownPressed,
                    OnEscapePressed = HandleEscapePressed,
                };
                _keyboard.Install();

                _surface.Notify("MathCursor prêt. Tapez une expression puis Tab.", HostContract.NotificationLevel.Info);
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
            try { _suggestions?.Dispose(); } catch { }
        }

        private bool HandleTabPressed()
        {
            if (_orchestrator == null) return false;
            try
            {
                bool converted = _orchestrator.TryConvertAtCaret();
                if (converted)
                {
                    _suggestions?.HidePopup();
                }
                return converted;
            }
            catch (Exception ex)
            {
                _feedback?.LogParsingError("(tab_hook)", ex.Message);
                return false;
            }
        }

        // Down : si popup visible
        //   - pas en mode nav → entrer en mode nav (highlight 1er, opacité 0.7)
        //   - déjà en mode nav → naviguer +1
        // Sinon → laisser Word gérer la flèche (déplacer le curseur).
        private bool HandleDownPressed()
        {
            if (_suggestions?.IsPopupVisible != true) return false;
            if (!_suggestions.IsNavMode)
            {
                _suggestions.EnterNavMode();
            }
            else
            {
                _suggestions.MoveSelection(+1);
            }
            return true;
        }

        // Up : navigue dans la popup uniquement en mode nav. En display, laisse Word.
        private bool HandleUpPressed()
        {
            if (_suggestions?.IsPopupVisible == true && _suggestions.IsNavMode)
            {
                _suggestions.MoveSelection(-1);
                return true;
            }
            return false;
        }

        // Enter : valide le choix sélectionné si en mode nav. En display, laisse
        // Word insérer un saut de paragraphe normalement.
        private bool HandleEnterPressed()
        {
            if (_suggestions?.IsPopupVisible == true && _suggestions.IsNavMode)
            {
                return HandleTabPressed();
            }
            return false;
        }

        // Esc : ferme la popup. Si elle est cachée, laisse passer (Word gère).
        private bool HandleEscapePressed()
        {
            if (_suggestions?.IsPopupVisible == true)
            {
                _suggestions.HidePopup();
                return true;
            }
            return false;
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
