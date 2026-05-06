using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Office.Core;
// Microsoft.Office.Tools fournit CustomTaskPane (ambigu avec
// Microsoft.Office.Core.CustomTaskPane) → on alias.
using OfficeTools = Microsoft.Office.Tools;
using MathCursor.Cheatsheet;
using MathCursor.Detection;
using MathCursor.Host;
// Moteur lattice : enchaîne Lex → TopK → Parse → Render (cf. Lattice/).
using Engine = MathCursor.Core.LatticeEngine;

namespace MathCursor
{
    public partial class ThisAddIn
    {
        private VstoEquationStore _store;
        private MathNerDetector _ner;
        private Engine _engine;
        private SuggestionService _suggestions;
        private KeyboardInterceptor _keyboard;

        // Cheatsheet pane (cf. ADR 2026-05-05-Feat-ribbon-refactor-cheatsheet).
        // Lazy-init au premier toggle. Après création, on flippe juste Visible.
        private OfficeTools.CustomTaskPane _cheatsheetTaskPane;
        private CheatsheetPaneHost _cheatsheetHost;
        private CheatsheetViewModel _cheatsheetVm;

        /// <summary>Accès au service pour le ribbon (bouton "Signaler une erreur").
        /// Null si l'add-in a échoué à démarrer.</summary>
        internal SuggestionService Suggestions => _suggestions;

        private void ThisAddIn_Startup(object sender, System.EventArgs e)
        {
            try
            {
                // Sélectionne le natif onnxruntime.dll de la bonne bitness AVANT
                // toute interaction avec ONNX Runtime. Word peut être 32 ou 64
                // bits — sans cet appel, P/Invoke pioche dans %PATH% ou côté
                // assembly et tombe sur le mauvais binaire → BadImageFormatException
                // remontée en TypeInitializationException sur NativeMethods..cctor()
                // au premier `new SessionOptions()`. L'installer dépose les natifs
                // dans <InstallDir>\onnxruntime-x86\ et <InstallDir>\onnxruntime-x64\.
                ConfigureOnnxRuntimeNativeDir();

                // Store des sources (CustomXMLParts) : utilisé par le mode édition
                // d'un OMath existant (phase C2 : bookmark → source → popup).
                _store = new VstoEquationStore(this.Application);

                // Modèle NER (chemin dev hardcodé pour MVP)
                var modelDir = FindModelDir();
                _ner = new MathNerDetector(modelDir);

                // Warm-up async : la 1ère inférence prend ~500ms, on la fait
                // hors thread UI pour ne pas bloquer le chargement de Word.
                _ = _ner.WarmUpAsync();

                // Moteur lattice : convertit le texte détecté par le NER en
                // suggestions LaTeX via Lex → top-K Dijkstra → Parse → Render.
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
                    OnLeftPressed = HandleLeftPressed,
                    OnRightPressed = HandleRightPressed,
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
            // Save final pour capturer la largeur user (CustomTaskPane n'a pas
            // d'event WidthChanged → on lit la valeur courante au shutdown).
            try { SaveCheatsheetState(); } catch { }
            try { _keyboard?.Dispose(); } catch { }
            try { _suggestions?.Dispose(); } catch { }
            try { _ner?.Dispose(); } catch { }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetDllDirectory(string lpPathName);

        /// <summary>
        /// Pointe le loader Windows sur le sous-dossier <c>onnxruntime-x86</c>
        /// ou <c>onnxruntime-x64</c> selon la bitness de WINWORD.EXE, pour que
        /// le P/Invoke d'ONNX Runtime trouve un <c>onnxruntime.dll</c> de la
        /// bonne arch. Sinon : <see cref="BadImageFormatException"/> remontée
        /// en <see cref="TypeInitializationException"/> dans le .cctor de
        /// <c>Microsoft.ML.OnnxRuntime.NativeMethods</c>.
        ///
        /// Best-effort : si le dossier attendu n'existe pas (ex. dev sans
        /// installer où les natifs sont déjà copiés à plat par MSBuild), on
        /// laisse le loader chercher selon ses règles standard.
        /// </summary>
        private static void ConfigureOnnxRuntimeNativeDir()
        {
            var arch = IntPtr.Size == 4 ? "x86" : "x64";
            var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(baseDir)) return;
            var nativeDir = Path.Combine(baseDir, "onnxruntime-" + arch);
            if (Directory.Exists(nativeDir)) SetDllDirectory(nativeDir);
        }

        /// <summary>
        /// Cherche le dossier du modèle dans plusieurs endroits standards.
        /// </summary>
        private static string FindModelDir()
        {
            // Cherche le sous-dossier `distilmult-v5` (NER actif depuis
            // 2026-04-30, cf. ADR 2026-04-27 d'adoption initiale + bump v5).
            // Pas de fallback sur d'anciens layouts : l'installer propre
            // déploie v5, les anciennes versions disparaissent à l'update.
            var roots = new[]
            {
                Environment.GetEnvironmentVariable("MATHCURSOR_MODEL_DIR"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MathCursor", "models"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models"),
                @"D:\Software\DocMath\models",
            };
            var candidates = new List<string>();
            foreach (var root in roots)
            {
                if (string.IsNullOrEmpty(root)) continue;
                candidates.Add(Path.Combine(root, "distilmult-v5"));
            }
            foreach (var p in candidates)
            {
                if (Directory.Exists(p)
                    && File.Exists(Path.Combine(p, "model_quantized.onnx"))
                    && File.Exists(Path.Combine(p, "vocab.txt")))
                    return p;
            }
            throw new DirectoryNotFoundException(
                "Modèle NER introuvable (cherche model_quantized.onnx + vocab.txt). "
                + "Chemins testés :\n" + string.Join("\n", candidates));
        }

        /// <summary>
        /// Toggle du panneau Cheatsheet (cf. ADR 2026-05-05-Feat-ribbon-refactor-cheatsheet).
        /// Lazy-init au premier appel : charge le JSON embarqué, instancie le
        /// ViewModel, crée le CustomTaskPane ancré à droite. Appels suivants :
        /// flippe juste <c>Visible</c>. Le pane survit à la fermeture (état
        /// gardé en mémoire, persistance ajoutée en étape 2.E).
        /// </summary>
        internal void ToggleExamplesPane()
        {
            try
            {
                if (_cheatsheetTaskPane == null)
                {
                    var doc = MathCursor.Cheatsheet.CheatsheetData.LoadEmbedded();
                    if (doc == null)
                    {
                        System.Windows.MessageBox.Show(
                            "Exemples introuvables (ressource embarquée manquante).",
                            "MathCursor", System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Warning);
                        return;
                    }
                    _cheatsheetVm = new CheatsheetViewModel(doc, MathCursor.Strings.Lang);
                    // Restore l'état persisté (collapse) avant de monter l'UI
                    var savedState = MathCursor.Cheatsheet.CheatsheetPersistence.Load();
                    MathCursor.Cheatsheet.CheatsheetPersistence.ApplyToViewModel(savedState, _cheatsheetVm);

                    _cheatsheetHost = new CheatsheetPaneHost(_cheatsheetVm);
                    _cheatsheetHost.WpfPane.OnMissingShortcutRequested = OnMissingShortcutRequested;

                    _cheatsheetTaskPane = CustomTaskPanes.Add(
                        _cheatsheetHost, MathCursor.Strings.ExamplesPaneTitle);
                    _cheatsheetTaskPane.Width = savedState.PaneWidth > 0 ? savedState.PaneWidth : 380;
                    _cheatsheetTaskPane.DockPosition = MsoCTPDockPosition.msoCTPDockPositionRight;

                    // Sauvegardes auto : sur changement collapse (VM) +
                    // sur visibility (CustomTaskPane). La largeur est capturée
                    // au moment de chaque save (pas d'event "WidthChanged" en
                    // VSTO, on la lit depuis _cheatsheetTaskPane.Width).
                    _cheatsheetVm.StateChanged += SaveCheatsheetState;
                    _cheatsheetTaskPane.VisibleChanged += (s, e) => SaveCheatsheetState();
                }
                _cheatsheetTaskPane.Visible = !_cheatsheetTaskPane.Visible;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    "Échec d'ouverture du panneau Exemples :\n" + ex.Message,
                    "MathCursor", System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Capture l'état VM + UI courant et le persiste via IsolatedStorage.
        /// Appelée sur StateChanged (collapse), VisibleChanged (open/close),
        /// et au shutdown de l'add-in.
        /// </summary>
        private void SaveCheatsheetState()
        {
            try
            {
                if (_cheatsheetVm == null || _cheatsheetTaskPane == null) return;
                var state = MathCursor.Cheatsheet.CheatsheetPersistence.Capture(
                    _cheatsheetVm,
                    paneOpen: _cheatsheetTaskPane.Visible,
                    paneWidth: _cheatsheetTaskPane.Width);
                MathCursor.Cheatsheet.CheatsheetPersistence.Save(state);
            }
            catch
            {
                // Persistance best-effort, jamais propager
            }
        }

        /// <summary>
        /// Ouvre le FeedbackDialog avec un report pré-rempli "missing shortcut" :
        /// pas de zone math (champs source/proposed/committed/paragraph vides),
        /// préfixe <c>[missing_shortcut]</c> dans UserMessage pour que le
        /// backend / admin puisse trier ces feedbacks séparément des bugs.
        /// </summary>
        private void OnMissingShortcutRequested()
        {
            try
            {
                if (_suggestions == null)
                {
                    System.Windows.MessageBox.Show(
                        "Service indisponible.",
                        MathCursor.Strings.ExamplesMissingButton,
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }
                // BuildFeedbackReport renseigne la metadata (version, user_id,
                // session_id, word_version, os...). On clear les champs math
                // spécifiques (pas de zone NER ici) et on préfixe le commentaire.
                var report = _suggestions.BuildFeedbackReport();
                report.NerText = "";
                report.RecognizedFormula = "";
                report.CommittedLatex = "";
                report.ParagraphContext = "";
                report.UserMessage = "[missing_shortcut] ";

                var sender = MathCursor.Host.Feedback.FeedbackSenderFactory.Create();
                var dialog = new MathCursor.UI.FeedbackDialog(report, sender);
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    "Impossible d'ouvrir le formulaire de feedback :\n" + ex.Message,
                    MathCursor.Strings.ExamplesMissingButton,
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
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

        // Esc : cache la popup (suggestion OU édition). En mode édition d'OMath,
        // les flèches restent à Word pour la nav math native — seul Esc est
        // intercepté pour fermer la popup edit.
        private bool HandleEscapePressed()
        {
            if (_suggestions?.IsAnyPopupVisible == true)
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

        // Left/Right : navigation horizontale entre alternatives en mode
        // ambiguïté. N'a d'effet que si la popup est visible + en nav mode +
        // focus sur la zone alts. Sinon pass-through (Word déplace le caret
        // dans le texte normalement).
        private bool HandleLeftPressed()
        {
            if (_suggestions?.IsPopupVisible != true) return false;
            if (!_suggestions.IsNavMode) return false;
            return _suggestions.MoveSelectionHorizontal(-1);
        }

        private bool HandleRightPressed()
        {
            if (_suggestions?.IsPopupVisible != true) return false;
            if (!_suggestions.IsNavMode) return false;
            return _suggestions.MoveSelectionHorizontal(+1);
        }

        // Enter : (1) si popup visible + NavMode → commit du candidat
        // sélectionné, sinon (2) si list-mode actif (ADR 05-05) → préfixage
        // automatique du marker + commit cross-merge, sinon (3) pass-through
        // (Word insère un saut de ligne normal).
        private bool HandleEnterPressed()
        {
            if (_suggestions?.IsPopupVisible == true && _suggestions.IsNavMode)
            {
                return _suggestions.CommitSelected();
            }
            // Liste invisible : si une chaîne multi-ligne vient d'être créée
            // ou étendue, l'utilisateur peut taper sa nouvelle équation sans
            // re-saisir le marker. Voir ListModeStateMachine.
            if (_suggestions?.TryHandleListModeEnter() == true) return true;
            return false;
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
