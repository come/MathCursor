using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Office.Core;
using MathCursor.Detection;
using MathCursor.Host;
// Placeholder pendant le pivot lattice (cf. core ILatexEngine).
using Engine = MathCursor.Core.NotImplementedEngine;

namespace MathCursor
{
    public partial class ThisAddIn
    {
        private VstoEquationStore _store;
        private MathNerDetector _ner;
        private Engine _engine;
        private SuggestionService _suggestions;
        private KeyboardInterceptor _keyboard;

        private void ThisAddIn_Startup(object sender, System.EventArgs e)
        {
            try
            {
                // Store des sources (CustomXMLParts) : utilisé par le mode édition
                // d'un OMath existant (phase C2 : bookmark → source → popup).
                _store = new VstoEquationStore(this.Application);

                // Modèle NER (chemin dev hardcodé pour MVP)
                var modelDir = FindModelDir();
                _ner = new MathNerDetector(modelDir);

                // Warm-up async : la 1ère inférence prend ~500ms, on la fait
                // hors thread UI pour ne pas bloquer le chargement de Word.
                _ = _ner.WarmUpAsync();

                // Moteur de patterns YAML : convertit le texte détecté par le NER
                // en suggestions LaTeX classées par score.
                _engine = Engine.LoadEmbedded("fr");

                _suggestions = new SuggestionService(this.Application, _ner, _engine, _store);
                _suggestions.Install();

                // Hook clavier : Enter valide le candidat sélectionné UNIQUEMENT
                // quand la popup est en NavMode. Tab pass-through, Esc masque.
                _keyboard = new KeyboardInterceptor
                {
                    OnTabPressed = () => false,
                    OnEnterPressed = HandleEnterPressed,
                    OnUpPressed = HandleUpPressed,
                    OnDownPressed = HandleDownPressed,
                    OnEscapePressed = HandleEscapePressed,
                    OnCtrlSpacePressed = HandleCtrlSpacePressed,
                };
                _keyboard.Install();

                this.Application.StatusBar = "MathCursor prêt";
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

        // Ctrl+Espace : trigger explicite — force la popup sur la span
        // "caret → stopword/délimiteur/OMath précédent". Escape hatch quand le
        // NER rate un bout (ex: "Soit f et g" avec f déjà converti → NER ne
        // capte plus "g" tout seul, on tape Ctrl+Espace pour forcer).
        private bool HandleCtrlSpacePressed()
        {
            _suggestions?.TriggerManual();
            return true; // consomme la touche, évite l'IME/compose de Word
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

        // Down : entre en mode nav et descend dans la liste si la popup est visible.
        private bool HandleDownPressed()
        {
            if (_suggestions?.IsPopupVisible != true) return false;
            if (!_suggestions.IsNavMode) _suggestions.EnterNavMode();
            else _suggestions.MoveSelection(+1);
            return true; // consomme la touche
        }

        // Up : remonte dans la liste si déjà en nav mode, sinon pass-through.
        private bool HandleUpPressed()
        {
            if (_suggestions?.IsPopupVisible != true) return false;
            if (!_suggestions.IsNavMode) return false;
            _suggestions.MoveSelection(-1);
            return true;
        }

        // Enter : si popup visible + NavMode → commit du candidat sélectionné,
        // sinon pass-through (Word insère un saut de ligne normal).
        private bool HandleEnterPressed()
        {
            if (_suggestions?.IsPopupVisible != true) return false;
            if (!_suggestions.IsNavMode) return false;
            return _suggestions.CommitSelected();
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
