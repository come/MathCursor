using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Office.Core;
using MathCursor.Host;
using MathCursor.Host.Caret;
using MathCursor.Host.EditMode;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor
{
    /// <summary>
    /// Bootstrap de l'add-in — Phase 2 beta-clean (ADR 2026-06-10) :
    /// <list type="bullet">
    /// <item>Pas de polling : trigger manuel (Ctrl+Espace / ribbon) + event
    ///   natif <c>WindowSelectionChange</c> pour le mode édition.</item>
    /// <item>NER non démarré (différé Phase 4) — l'add-in démarre sans
    ///   modèle sur disque.</item>
    /// <item>Moteur de reconnaissance : portage forest
    ///   (<c>MathCursor.Engine.ForestEngine</c>), statique, rien à init.</item>
    /// </list>
    /// </summary>
    public partial class ThisAddIn
    {
        private ConversionController _conversion;
        private EditModeController _editMode;
        private EquationDeletionGuard _deletionGuard;
        private AutoDetectController _autoDetect;
        private Detection.MathNerDetector _ner;
        private KeyboardInterceptor _keyboard;
        private static readonly string SessionId = Guid.NewGuid().ToString("N").Substring(0, 12);

        /// <summary>Accès pour le ribbon. Null si le démarrage a échoué.</summary>
        internal ConversionController Conversion => _conversion;

        private void ThisAddIn_Startup(object sender, System.EventArgs e)
        {
            try
            {
                // Natifs ONNX : pointer le loader sur la bonne bitness AVANT
                // tout usage d'ONNX Runtime (Phase 4, NER). Harmless si le
                // dossier n'existe pas. Cf. commentaire de la méthode.
                ConfigureOnnxRuntimeNativeDir();

                // Désactive l'autocorrect math hors zones math. Sans ça, Word
                // anticipe une « nouvelle équation » quand on tape adjacent à
                // une OMath via TypeText programmatique (bug observé 2026-05-18).
                DisableOMathAutoCorrectOutsideMath();

                // Désactive le remplacement « 1/2 → ½ » : Word injecte un char
                // de fraction vulgaire que l'utilisateur ne contrôle pas et qui
                // brouille la frappe math. Cf. ADR 2026-06-18-Fix-input-
                // autocorrect-fraction-factorial (le moteur tolère ½ en repli).
                DisableAutoFormatFractions();

                _conversion = new ConversionController(this.Application, BuildFeedbackReport);

                _editMode = new EditModeController(
                    this.Application,
                    _conversion.Resolver,
                    hideSuggestionPopup: () => _conversion?.HidePopup(),
                    getCaretScreenPos: CaretScreenPositionReader.Read,
                    boxAtCaret: () => _conversion?.BoxAtCaret() ?? false,
                    log: LogStartup);

                // Suppression atomique Backspace/Suppr (héritier minimal de
                // l'ex-AnchorHygiene H1 ; H2/H3 caducs sans CC — ADR
                // hash-source-map).
                _deletionGuard = new EquationDeletionGuard(
                    this.Application, _conversion.Resolver, LogStartup);

                // Auto-détection NER en cours de frappe (ADR 2026-06-10-Feat-
                // ner-auto-detection-debounce). Inerte tant que le modèle
                // n'est pas chargé (cf. LoadNerDetectorAsync ci-dessous).
                _autoDetect = new AutoDetectController(
                    this.Application,
                    _conversion,
                    isEditPopupVisible: () => _editMode?.IsPopupVisible == true);

                // Hook clavier thread-local (Word UI thread, pas global).
                _keyboard = new KeyboardInterceptor
                {
                    OnCtrlSpacePressed = HandleCtrlSpacePressed,
                    OnTabPressed = HandleTabPressed,
                    OnEnterPressed = HandleEnterPressed,
                    OnUpPressed = HandleUpPressed,
                    OnDownPressed = HandleDownPressed,
                    OnEscapePressed = HandleEscapePressed,
                    OnBackspacePressed = () => _deletionGuard?.TrySelectEquationBeforeCaret() ?? false,
                    OnDeletePressed = () => _deletionGuard?.TrySelectEquationAfterCaret() ?? false,
                    OnTextKeyTyped = () => _autoDetect?.OnTextKeyTyped(),
                };
                _keyboard.Install();

                // Chargement du modèle NER HORS thread UI (~1-2 s : session
                // ONNX + warm-up). Modèle absent = pas d'auto-détection, pas
                // de crash — Ctrl+Espace reste pleinement fonctionnel.
                LoadNerDetectorAsync();

                // Event natif : pilote le mode édition + ferme la popup
                // suggestion quand le caret bouge. Pas de polling.
                this.Application.WindowSelectionChange += OnWindowSelectionChange;

                // Compteur d'usage : flush sur perte de focus de Word (ADR
                // 2026-06-18-Feat-usage-counter-telemetry). Anti-spam interne :
                // n'envoie que si le compteur > 0.
                this.Application.WindowDeactivate += OnWindowDeactivate;

                // Préchauffage (UX 2026-06-12) : la PREMIÈRE popup payait HWND
                // WPF + JIT WpfMath (~1 s de lag perçu). Hors écran, à
                // priorité idle (après le boot de Word), sur le thread UI.
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                    new Action(() => _conversion?.WarmUpPopup()));

                // Moteur + sérialiseur : chauffe JIT en tâche de fond (purs).
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        MathCursor.Engine.ForestEngine.Analyze("f(x)=1/x");
                        MathCursor.Serialization.LatexToOmml.Convert("f(x)=\\frac{1}{x}");
                    }
                    catch (Exception exW) { LogStartup("warmup_engine_error: " + exW.Message); }
                });

                // Vérif MAJ (indicateur « ● MAJ » sur l'onglet) : GET au démarrage,
                // fire-and-forget. Le rafraîchissement du ruban DOIT repasser sur le
                // thread UI Office → on capture son Dispatcher ici (Startup = thread UI).
                // Cf. ADR 2026-06-18-Feat-ribbon-update-badge.
                var uiDispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                System.Threading.Tasks.Task.Run(() => Host.Update.UpdateChecker.CheckAsync(
                    () => uiDispatcher.BeginInvoke(new Action(
                        () => RibbonCallback.Instance?.InvalidateUpdateBadge()))));

                this.Application.StatusBar = Strings.StatusReady;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    Strings.StartupFailed + ex.Message + "\n\n" + ex.StackTrace,
                    "MathCursor",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        private void ThisAddIn_Shutdown(object sender, System.EventArgs e)
        {
            try { this.Application.WindowSelectionChange -= OnWindowSelectionChange; } catch { }
            try { this.Application.WindowDeactivate -= OnWindowDeactivate; } catch { }
            // Dernier flush du compteur d'usage, best-effort, borné dans le temps
            // pour ne pas retarder la fermeture de Word (souvent déjà à 0 grâce au
            // flush sur perte de focus). Hors thread UI → pas de deadlock de contexte.
            try
            {
                System.Threading.Tasks.Task.Run(() => Host.Usage.UsageStatsClient.FlushAsync())
                    .Wait(TimeSpan.FromSeconds(2));
            }
            catch { }
            try { _keyboard?.Dispose(); } catch { }
            try { _autoDetect?.Dispose(); } catch { }
            try { _ner?.Dispose(); } catch { }
            try { _conversion?.Dispose(); } catch { }
            try { _editMode?.Close(); } catch { }
        }

        // ── NER (auto-détection) ─────────────────────────────────────────

        /// <summary>
        /// Charge le modèle NER en arrière-plan puis l'attache à
        /// l'<see cref="AutoDetectController"/>. Échec ou modèle introuvable
        /// → log + auto-détection inactive, jamais d'exception vers Word.
        /// </summary>
        private void LoadNerDetectorAsync()
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var modelDir = TryFindModelDir();
                    if (modelDir == null)
                    {
                        LogStartup("ner: modèle introuvable → auto-détection inactive (Ctrl+Espace seul)");
                        return;
                    }
                    var detector = new Detection.MathNerDetector(modelDir);
                    detector.Detect("x = 1"); // warm-up (1ʳᵉ inférence ~500 ms)
                    _ner = detector;
                    _autoDetect?.AttachDetector(detector);
                    LogStartup("ner: modèle chargé depuis " + modelDir);
                }
                catch (Exception ex)
                {
                    LogStartup("ner_load_error: " + ex.Message + " → auto-détection inactive");
                }
            });
        }

        /// <summary>
        /// Cherche le modèle NER (model_quantized.onnx + vocab.txt) dans les
        /// emplacements standards, en préférant l'alias stable <c>latest</c>
        /// (partagé entre adapters), fallback versionnés <c>distilmult-v7/v6</c>). Null si absent
        /// (le modèle ~132 Mo n'est pas dans git ; l'installer le déploie, et en
        /// dev le fallback DocMath s'applique).
        /// </summary>
        private static string TryFindModelDir()
        {
            var roots = new[]
            {
                Environment.GetEnvironmentVariable("MATHCURSOR_MODEL_DIR"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MathCursor", "models"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models"),
                @"D:\Software\DocMath\models", // dev
            };
            // Préférence à la version la plus récente, TOUTES racines confondues
            // (un v7 en dev prime sur un v6 résiduel ailleurs).
            foreach (var name in new[] { "latest", "distilmult-v7", "distilmult-v6" })
                foreach (var root in roots)
                {
                    if (string.IsNullOrEmpty(root)) continue;
                    var p = Path.Combine(root, name);
                    if (Directory.Exists(p)
                        && File.Exists(Path.Combine(p, "model_quantized.onnx"))
                        && File.Exists(Path.Combine(p, "vocab.txt")))
                        return p;
                }
            return null;
        }

        // ── Événement natif : caret bouge ────────────────────────────────

        private void OnWindowSelectionChange(Word.Selection sel)
        {
            try
            {
                if (_conversion?.IsCommitting == true) return;
                _conversion?.OnSelectionChanged();

                Word.OMath omAtCaret = null;
                try
                {
                    if (sel?.OMaths != null && sel.OMaths.Count > 0)
                        foreach (Word.OMath o in sel.OMaths) { omAtCaret = o; break; }
                }
                catch { }
                _editMode?.Sync(omAtCaret, inPostCommitCooldown: false);

                // QOL 2026-06-12 : un CLIC dans du texte (sélection réduite,
                // hors OMath) relance la détection — la popup se (re)propose
                // sur une expression existante, ou se ferme si rien au caret.
                try
                {
                    if (omAtCaret == null && sel != null && sel.Start == sel.End)
                        _autoDetect?.OnCaretMoved();
                }
                catch { }
            }
            catch { }
        }

        // ── Événement natif : Word perd le focus ─────────────────────────

        // Flush du compteur d'usage (fire-and-forget). FlushAsync ne fait rien
        // si l'opt-out est coupé ou si le compteur est à zéro.
        private void OnWindowDeactivate(Word.Document doc, Word.Window wn)
        {
            try { _ = Host.Usage.UsageStatsClient.FlushAsync(); } catch { }
        }

        // ── Hook clavier ─────────────────────────────────────────────────

        // Ctrl+Espace : trigger explicite (1er appui = propose, appuis
        // suivants popup ouverte = étend la zone d'un cran à gauche).
        private bool HandleCtrlSpacePressed()
        {
            _conversion?.Trigger();
            return true; // consomme (évite l'IME/compose de Word)
        }

        // Tab : popup visible → commit du candidat sélectionné (= top si
        // l'utilisateur n'a pas navigué). Sinon pass-through.
        private bool HandleTabPressed()
        {
            // Opt-in (toggle ruban « Tab valide », défaut OFF) : sans lui,
            // Tab reste une tabulation Word même popup ouverte.
            if (!Host.Settings.SettingsStore.Current.TabValidate) return false;
            if (_conversion?.IsPopupVisible != true) return false;
            return _conversion.CommitSelected();
        }

        // Enter : commit UNIQUEMENT en nav mode (sinon Enter = saut de ¶) ;
        // sinon, sortie du flow multiligne si la ligne = séparateur pré-placé
        // seul (M4 : le séparateur s'efface, la ligne reste vide).
        private bool HandleEnterPressed()
        {
            if (_conversion?.IsPopupVisible == true && _conversion.IsNavMode)
                return _conversion.CommitSelected();
            return _conversion?.TryExitFlowOnEnter() ?? false;
        }

        // Down : entre en nav mode, puis descend.
        private bool HandleDownPressed()
        {
            if (_conversion?.IsPopupVisible != true) return false;
            if (!_conversion.IsNavMode) { _conversion.EnterNavMode(); return true; }
            return _conversion.MoveSelection(+1);
        }

        // Up : remonte si déjà en nav mode, sinon pass-through.
        private bool HandleUpPressed()
        {
            if (_conversion?.IsPopupVisible != true || !_conversion.IsNavMode) return false;
            return _conversion.MoveSelection(-1);
        }

        // Esc : ferme la popup suggestion, sinon la popup edit.
        private bool HandleEscapePressed()
        {
            if (_conversion?.IsPopupVisible == true) { _conversion.HidePopup(); return true; }
            if (_editMode?.IsPopupVisible == true) { _editMode.HidePopup(); return true; }
            return false;
        }

        // ── Feedback ─────────────────────────────────────────────────────

        /// <summary>
        /// Rapport pré-rempli pour « Signaler un souci » : versions, ids,
        /// paragraphe courant, queue de log. Les champs zone (NerText,
        /// RecognizedFormula…) sont remplis par l'appelant selon le contexte.
        /// </summary>
        internal Host.Feedback.FeedbackReport BuildFeedbackReport()
        {
            var report = new Host.Feedback.FeedbackReport
            {
                Version = SafeVersion(),
                UserId = SafeUserId(),
                SessionId = SessionId,
                WordVersion = SafeWordVersion(),
                OsVersion = Environment.OSVersion.ToString(),
                DotNetVersion = Environment.Version.ToString(),
            };
            try
            {
                var sel = this.Application?.Selection;
                if (sel != null) report.ParagraphContext = sel.Paragraphs[1].Range.Text ?? "";
            }
            catch { }
            try
            {
                var tail = FeedbackBundle.ReadLogTail();
                if (tail != null) report.LogTail = System.Text.Encoding.UTF8.GetString(tail);
            }
            catch { }
            return report;
        }

        private static string SafeVersion()
        {
            try { return Strings.FormatVersion(Assembly.GetExecutingAssembly().GetName().Version); }
            catch { return "?"; }
        }

        private static string SafeUserId()
        {
            try { return Host.Feedback.UserIdStore.GetOrCreate(); }
            catch { return ""; }
        }

        private string SafeWordVersion()
        {
            try { return this.Application?.Version ?? "?"; }
            catch { return "?"; }
        }

        // ── Setup Word ───────────────────────────────────────────────────

        private void DisableOMathAutoCorrectOutsideMath()
        {
            try
            {
                var omac = this.Application.OMathAutoCorrect;
                if (omac == null) { LogStartup("OMathAutoCorrect: null sur cette version de Word"); return; }
                var type = omac.GetType();
                foreach (var name in new[] { "UseOutsideOMathRegion", "UseOutsideMathRegion", "ReplaceText" })
                {
                    var prop = type.GetProperty(name);
                    if (prop == null) continue;
                    try { prop.SetValue(omac, false, null); }
                    catch (Exception exSet) { LogStartup($"OMathAutoCorrect.{name} setter throw: {exSet.Message}"); }
                }
            }
            catch (Exception ex) { LogStartup("OMathAutoCorrect setup error: " + ex.Message); }
        }

        /// <summary>
        /// Coupe l'AutoFormat « Fractions (1/2) → ½ » de Word. Sans ça, taper
        /// <c>1/2</c> produit le caractère U+00BD que le moteur recevait comme
        /// « caractère inattendu ». On le désactive globalement au démarrage
        /// (le moteur sait quand même lire ½ en repli — copier-coller, doc
        /// existante). Cf. ADR 2026-06-18-Fix-input-autocorrect-fraction-factorial.
        /// </summary>
        private void DisableAutoFormatFractions()
        {
            try
            {
                var options = this.Application.Options;
                if (options != null) options.AutoFormatAsYouTypeReplaceFractions = false;
            }
            catch (Exception ex) { LogStartup("AutoFormatFractions setup error: " + ex.Message); }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetDllDirectory(string lpPathName);

        /// <summary>
        /// Pointe le loader Windows sur <c>onnxruntime-x86</c> / <c>-x64</c>
        /// selon la bitness de WINWORD.EXE (sinon BadImageFormatException au
        /// premier <c>new SessionOptions()</c>). Conservé pour la Phase 4
        /// (NER) — no-op silencieux si les dossiers natifs n'existent pas.
        /// </summary>
        private static void ConfigureOnnxRuntimeNativeDir()
        {
            var arch = IntPtr.Size == 4 ? "x86" : "x64";
            string targetSubdir = "onnxruntime-" + arch;

            var candidates = new List<string>();
            try
            {
                var loc = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(loc)) candidates.Add(Path.GetDirectoryName(loc));
            }
            catch { }
            try
            {
                var cb = Assembly.GetExecutingAssembly().CodeBase;
                if (!string.IsNullOrEmpty(cb)) candidates.Add(Path.GetDirectoryName(new Uri(cb).LocalPath));
            }
            catch { }
            try
            {
                var lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(lad)) candidates.Add(Path.Combine(lad, "MathCursor"));
            }
            catch { }

            foreach (var baseDir in candidates)
            {
                if (string.IsNullOrEmpty(baseDir)) continue;
                var nativeDir = Path.Combine(baseDir, targetSubdir);
                if (!Directory.Exists(nativeDir)) continue;
                try { SetDllDirectory(nativeDir); } catch { }
                return;
            }
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

        private static void LogStartup(string message)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MathCursor", "logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "mathcursor.log"),
                    $"{DateTime.UtcNow:o} startup {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
