using System;
using System.IO;
using System.Reflection;
using System.Windows;
using Microsoft.Office.Core;

namespace MathCursor
{
    /// <summary>
    /// Callbacks du ribbon Phase 2 beta-clean (cf. <c>Ribbon.xml</c> + ADR
    /// 2026-06-10-Refactor-phase2-adapter-orchestration-rewrite). Trois
    /// actions : Convertir (= Ctrl+Espace), Signaler un souci, À propos.
    /// </summary>
    [System.Runtime.InteropServices.ComVisible(true)]
    public class RibbonCallback : IRibbonExtensibility
    {
        private IRibbonUI _ribbon;

        /// <summary>Dernière instance créée par VSTO — permet à la fenêtre
        /// Paramètres de resynchroniser le toggle Détection auto du ruban.</summary>
        internal static RibbonCallback Instance { get; private set; }

        public RibbonCallback() { Instance = this; }

        /// <summary>Resynchronise l'état pressé des toggles liés aux réglages
        /// (à appeler après un changement de settings hors ruban).</summary>
        internal void InvalidateSettingsToggles()
        {
            try { _ribbon?.InvalidateControl("AutoDetectToggle"); } catch { }
            try { _ribbon?.InvalidateControl("TabValidateToggle"); } catch { }
        }

        public string GetCustomUI(string ribbonID)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                const string resourceName = "MathCursor.Ribbon.xml";
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        var names = string.Join(", ", assembly.GetManifestResourceNames());
                        LogDebug($"Ressource '{resourceName}' introuvable. Disponibles: [{names}]");
                        return "";
                    }
                    using (var reader = new StreamReader(stream))
                        return reader.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                LogDebug($"GetCustomUI exception: {ex.GetType().Name} {ex.Message}");
                return "";
            }
        }

        public void OnRibbonLoad(IRibbonUI ribbon) => _ribbon = ribbon;

        // ---------- Labels / screentips ----------

        public string OnGetTabLabel(IRibbonControl control) => Strings.MathCursorTabLabel;
        public string OnGetConversionGroupLabel(IRibbonControl control) => Strings.ConversionGroupLabel;
        public string OnGetLayoutGroupLabel(IRibbonControl control) => Strings.LayoutGroupLabel;
        public string OnGetToolsGroupLabel(IRibbonControl control) => Strings.ToolsTabGroupLabel;
        public string OnGetHelpGroupLabel(IRibbonControl control) => Strings.HelpGroupLabel;
        public string OnGetColumnsMenuLabel(IRibbonControl control) => Strings.ColumnsMenuLabel;
        public string OnGetColumnsMenuScreentip(IRibbonControl control) => Strings.ColumnsMenuScreentip;
        public string OnGetColumns1Label(IRibbonControl control) => Strings.Columns1Label;
        public string OnGetColumns2Label(IRibbonControl control) => Strings.Columns2Label;
        public string OnGetColumns3Label(IRibbonControl control) => Strings.Columns3Label;
        public string OnGetColumns4Label(IRibbonControl control) => Strings.Columns4Label;
        public string OnGetSettingsButtonLabel(IRibbonControl control) => Strings.SettingsButtonLabel;
        public string OnGetSettingsButtonScreentip(IRibbonControl control) => Strings.SettingsButtonScreentip;
        public string OnGetAutoDetectToggleLabel(IRibbonControl control) => Strings.AutoDetectToggleLabel;
        public string OnGetAutoDetectToggleScreentip(IRibbonControl control) => Strings.AutoDetectToggleScreentip;
        public bool OnGetAutoDetectPressed(IRibbonControl control) => Host.Settings.SettingsStore.Current.AutoDetect;
        public string OnGetTabValidateToggleLabel(IRibbonControl control) => Strings.TabValidateToggleLabel;
        public string OnGetTabValidateToggleScreentip(IRibbonControl control) => Strings.TabValidateToggleScreentip;
        public bool OnGetTabValidatePressed(IRibbonControl control) => Host.Settings.SettingsStore.Current.TabValidate;
        public string OnGetConvertButtonLabel(IRibbonControl control) => Strings.ConvertButtonLabel;
        public string OnGetConvertButtonScreentip(IRibbonControl control) => Strings.ConvertButtonScreentip;
        public string OnGetReportButtonLabel(IRibbonControl control) => Strings.ReportButtonLabel;
        public string OnGetReportButtonScreentip(IRibbonControl control) => Strings.ReportButtonScreentip;
        public string OnGetAboutButtonLabel(IRibbonControl control) => Strings.AboutButtonLabel;
        public string OnGetAboutButtonScreentip(IRibbonControl control) => Strings.AboutButtonScreentip;
        public string OnGetTutorialButtonLabel(IRibbonControl control) => Strings.TutorialButtonLabel;
        public string OnGetTutorialButtonScreentip(IRibbonControl control) => Strings.TutorialButtonScreentip;

        // ---------- Actions ----------

        /// <summary>Équivalent du raccourci Ctrl+Espace.</summary>
        public void OnConvertClicked(IRibbonControl control)
        {
            try { Globals.ThisAddIn?.Conversion?.Trigger(); }
            catch (Exception ex) { LogDebug("convert_clicked_error: " + ex.Message); }
        }

        public void OnReportIssueClicked(IRibbonControl control)
        {
            try
            {
                var addin = Globals.ThisAddIn;
                if (addin == null)
                {
                    MessageBox.Show(
                        Strings.ReportFailedBody(Host.FeedbackBundle.ContactEmail),
                        Strings.ReportFailedTitle,
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                // Screenshot AVANT de cacher la popup (elle fait partie du
                // contexte du bug), dialog ouvert APRÈS (jamais dans le screen).
                byte[] preScreenshot = null;
                try { preScreenshot = Host.FeedbackBundle.CaptureScreenshotPng(); } catch { }
                try { addin.Conversion?.HidePopup(); } catch { }
                var report = addin.BuildFeedbackReport();
                if (preScreenshot != null && preScreenshot.Length > 0)
                    report.ScreenshotPngBase64 = Convert.ToBase64String(preScreenshot);
                var sender = Host.Feedback.FeedbackSenderFactory.Create();
                new UI.FeedbackDialog(report, sender).ShowDialog();
            }
            catch (Exception ex) { LogDebug("report_clicked_error: " + ex.Message); }
        }

        /// <summary>
        /// Insère un tableau N colonnes au curseur (barres séparatrices,
        /// pas de bordures externes). N parsé depuis l'id du bouton
        /// (InsertColumns{1..4}Button).
        /// </summary>
        public void OnInsertColumnsClicked(IRibbonControl control)
        {
            try
            {
                int n = ParseColumnCountFromId(control?.Id);
                LogDebug($"insert_columns_click id={control?.Id ?? "<null>"} n={n}");
                if (n < 1 || n > 4) return;
                var app = Globals.ThisAddIn?.Application;
                if (app == null) { LogDebug("insert_columns: app null"); return; }
                Host.ColumnLayoutInserter.Insert(app, n);
                LogDebug($"insert_columns_done n={n}");
            }
            catch (Exception ex)
            {
                LogDebug("insert_columns_error: " + ex.Message);
                MessageBox.Show(
                    "Impossible d'insérer les colonnes :\n" + ex.Message,
                    "MathCursor",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private static int ParseColumnCountFromId(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            // Format attendu : "InsertColumns{N}Button". On scan le 1er chiffre.
            foreach (char c in id)
                if (c >= '1' && c <= '9') return c - '0';
            return 0;
        }

        /// <summary>Toggle Détection auto = écrit AppSettings.AutoDetect
        /// (persisté — même switch que la case de la fenêtre Paramètres).
        /// Ctrl+Espace reste actif quel que soit l'état (ADR NER).</summary>
        public void OnAutoDetectToggled(IRibbonControl control, bool pressed)
        {
            try
            {
                var s = Host.Settings.SettingsStore.Current.Clone();
                s.AutoDetect = pressed;
                Host.Settings.SettingsStore.Save(s);
                LogDebug($"autodetect_toggle → {pressed}");
            }
            catch (Exception ex) { LogDebug("autodetect_toggle_error: " + ex.Message); }
        }

        /// <summary>Toggle « Tab valide » = AppSettings.TabValidate (persisté,
        /// défaut OFF). Actif : Tab commit le candidat sélectionné quand la
        /// popup est ouverte, sans propager la tabulation.</summary>
        public void OnTabValidateToggled(IRibbonControl control, bool pressed)
        {
            try
            {
                var s = Host.Settings.SettingsStore.Current.Clone();
                s.TabValidate = pressed;
                Host.Settings.SettingsStore.Save(s);
                LogDebug($"tabvalidate_toggle → {pressed}");
            }
            catch (Exception ex) { LogDebug("tabvalidate_toggle_error: " + ex.Message); }
        }

        public void OnSettingsClicked(IRibbonControl control)
        {
            try
            {
                // Cacher la popup suggestion AVANT la modale (sinon elle
                // reste topmost au-dessus du dialog).
                try { Globals.ThisAddIn?.Conversion?.HidePopup(); } catch { }
                new UI.SettingsWindow().ShowDialog();
            }
            catch (Exception ex) { LogDebug("settings_clicked_error: " + ex.Message); }
        }

        /// <summary>Réouvre le docx tutoriel installé par le setup dans
        /// Documents\MathCursor\ (DestName universel FR/EN, cf. MathCursor.iss
        /// + ADR 2026-05-22-Feat-tutorial-docx-generated-onboarding).</summary>
        public void OnOpenTutorialClicked(IRibbonControl control)
        {
            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "MathCursor", "MathCursor-Tutorial.docx");
                if (!File.Exists(path))
                {
                    LogDebug("tutorial_missing: " + path);
                    MessageBox.Show(
                        Strings.TutorialMissingBody(path),
                        Strings.TutorialMissingTitle,
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                Globals.ThisAddIn?.Application?.Documents.Open(path);
                LogDebug("tutorial_opened");
            }
            catch (Exception ex) { LogDebug("tutorial_open_error: " + ex.Message); }
        }

        public void OnAboutClicked(IRibbonControl control)
        {
            MessageBox.Show(
                Strings.HelpDialogBody(CurrentVersion()),
                Strings.HelpDialogTitle,
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ---------- Internals ----------

        private static string CurrentVersion()
        {
            try { return Strings.FormatVersion(Assembly.GetExecutingAssembly().GetName().Version); }
            catch { return "?"; }
        }

        private static void LogDebug(string message)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MathCursor", "logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "mathcursor.log"),
                    $"{DateTime.UtcNow:o} ribbon {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
