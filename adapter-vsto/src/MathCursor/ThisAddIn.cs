using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Office.Core;
using MathCursor.Detection;
using MathCursor.Host;

namespace MathCursor
{
    public partial class ThisAddIn
    {
        private VstoDocumentHost _host;
        private VstoEquationStore _store;
        private VstoEditorSurface _surface;
        private VstoUserFeedback _feedback;
        private MathNerDetector _ner;
        private SuggestionService _suggestions;
        private KeyboardInterceptor _keyboard;

        private void ThisAddIn_Startup(object sender, System.EventArgs e)
        {
            try
            {
                _host = new VstoDocumentHost(this.Application);
                _store = new VstoEquationStore(this.Application);
                _surface = new VstoEditorSurface(this.Application);
                _feedback = new VstoUserFeedback();

                // Charge le modèle NER (chemin dev hardcodé pour MVP)
                var modelDir = FindModelDir();
                _ner = new MathNerDetector(modelDir);

                // Warm-up async : la 1ère inférence prend ~500ms, on la fait
                // hors thread UI pour ne pas bloquer le chargement de Word.
                _ = _ner.WarmUpAsync();

                // Popup de suggestions
                _suggestions = new SuggestionService(this.Application, _ner);
                _suggestions.Install();

                // Hook clavier — Tab/Enter DÉSACTIVÉS (validation uniquement,
                // l'utilisateur veut être en contrôle, pas d'action automatique).
                // Esc seul reste actif pour cacher la popup manuellement.
                _keyboard = new KeyboardInterceptor
                {
                    OnTabPressed = () => false,         // pass-through, Word insère un tab
                    OnEnterPressed = () => false,       // pass-through, Word insère un saut
                    OnUpPressed = () => false,          // pass-through
                    OnDownPressed = () => false,        // pass-through
                    OnEscapePressed = HandleEscapePressed,
                };
                _keyboard.Install();

                _surface.Notify("MathCursor (NER) prêt — détection seule, pas de conversion.", HostContract.NotificationLevel.Info);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    "Échec du démarrage MathCursor :\n" + ex.Message + "\n\n" + ex.StackTrace,
                    "MathCursor",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        private void ThisAddIn_Shutdown(object sender, System.EventArgs e)
        {
            try { _keyboard?.Dispose(); } catch { }
            try { _suggestions?.Dispose(); } catch { }
            try { _ner?.Dispose(); } catch { }
        }

        /// <summary>
        /// Cherche le dossier du modèle dans plusieurs endroits standards.
        /// </summary>
        private static string FindModelDir()
        {
            var candidates = new[]
            {
                Environment.GetEnvironmentVariable("MATHCURSOR_MODEL_DIR"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MathCursor", "models"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models"),
                @"D:\Software\DocMath\models", // dev fallback
            };
            foreach (var p in candidates)
            {
                if (string.IsNullOrEmpty(p)) continue;
                if (Directory.Exists(p) && File.Exists(Path.Combine(p, "model_quantized.onnx")))
                    return p;
            }
            throw new DirectoryNotFoundException(
                "Modèle NER introuvable. Chemins testés :\n" + string.Join("\n", candidates));
        }

        // Esc : cache la popup. Seul handler clavier actif en mode validation.
        private bool HandleEscapePressed()
        {
            if (_suggestions?.IsPopupVisible == true)
            {
                _suggestions.HidePopup();
                return true;
            }
            return false;
        }

        /// <summary>Bouton ribbon "Convertir" : DÉSACTIVÉ en mode validation NER.</summary>
        public void TriggerConversion()
        {
            // No-op : on est en mode validation détection seule.
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
