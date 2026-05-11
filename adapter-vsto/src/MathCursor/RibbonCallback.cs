using System;
using System.IO;
using System.Reflection;
using System.Windows;
using MathCursor.Host;
using Microsoft.Office.Core;

namespace MathCursor
{
    /// <summary>
    /// Implémente IRibbonExtensibility pour relier le Ribbon.xml aux actions.
    /// Utilise Globals.ThisAddIn pour accéder au host (qui peut ne pas encore
    /// être initialisé au moment où Word crée cette instance).
    /// </summary>
    [System.Runtime.InteropServices.ComVisible(true)]
    public sealed class RibbonCallback : IRibbonExtensibility
    {
        private IRibbonUI _ribbon;

        public string GetCustomUI(string ribbonID)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = "MathCursor.Ribbon.xml";
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        // Log les ressources disponibles pour diagnostic
                        var names = string.Join(", ", assembly.GetManifestResourceNames());
                        LogDebug($"Ressource '{resourceName}' introuvable. Disponibles: [{names}]");
                        return "";
                    }
                    using (var reader = new StreamReader(stream))
                    {
                        var xml = reader.ReadToEnd();
                        LogDebug($"Ribbon XML chargé ({xml.Length} caractères) pour ribbonID={ribbonID}");
                        return xml;
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug($"GetCustomUI exception: {ex.GetType().Name} {ex.Message}");
                return "";
            }
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
            catch { /* jamais d'exception depuis le logging */ }
        }

        public void OnRibbonLoad(IRibbonUI ribbon)
        {
            _ribbon = ribbon;
        }

        // ---------- getLabel / getScreentip callbacks (i18n + version) ----------

        /// <summary>Lit l'AssemblyVersion et formate "Major.Minor.Patch".</summary>
        private static string CurrentVersion()
            => Strings.FormatVersion(Assembly.GetExecutingAssembly().GetName().Version);

        public string OnGetToolsGroupLabel(IRibbonControl control)
            => Strings.ToolsGroupLabel(CurrentVersion());

        // ---------- TabHome (duo Convertir/Colonnes) + onglet dédié ----------

        public string OnGetHomeGroupLabel(IRibbonControl control)
            => Strings.HomeGroupLabel(CurrentVersion());

        public string OnGetMathCursorTabLabel(IRibbonControl control)
            => Strings.MathCursorTabLabel;

        public string OnGetInputGroupLabel(IRibbonControl control) => Strings.InputGroupLabel;
        public string OnGetLayoutGroupLabel(IRibbonControl control) => Strings.LayoutGroupLabel;
        public string OnGetConstructionsGroupLabel(IRibbonControl control) => Strings.ConstructionsGroupLabel;
        public string OnGetToolsTabGroupLabel(IRibbonControl control) => Strings.ToolsTabGroupLabel;

        public string OnGetConvertButtonLabel(IRibbonControl control) => Strings.ConvertButtonLabel;
        public string OnGetConvertButtonScreentip(IRibbonControl control) => Strings.ConvertButtonScreentip;

        public string OnGetColumnsMenuLabel(IRibbonControl control) => Strings.ColumnsMenuLabel;
        public string OnGetColumnsMenuScreentip(IRibbonControl control) => Strings.ColumnsMenuScreentip;
        public string OnGetColumns1Label(IRibbonControl control) => Strings.Columns1Label;
        public string OnGetColumns2Label(IRibbonControl control) => Strings.Columns2Label;
        public string OnGetColumns3Label(IRibbonControl control) => Strings.Columns3Label;
        public string OnGetColumns4Label(IRibbonControl control) => Strings.Columns4Label;

        public string OnGetCheatsheetButtonLabel(IRibbonControl control) => Strings.CheatsheetButtonLabel;
        public string OnGetCheatsheetButtonScreentip(IRibbonControl control) => Strings.CheatsheetButtonScreentip;
        public bool OnGetCheatsheetEnabled(IRibbonControl control) => false; // pane en pause, cf. ADR pivot

        public string OnGetConstructionSignTableLabel(IRibbonControl control) => Strings.ConstructionSignTableLabel;
        public string OnGetConstructionVariationTableLabel(IRibbonControl control) => Strings.ConstructionVariationTableLabel;
        public string OnGetConstructionCurveLabel(IRibbonControl control) => Strings.ConstructionCurveLabel;
        public string OnGetConstructionFigureLabel(IRibbonControl control) => Strings.ConstructionFigureLabel;
        public string OnGetConstructionComingSoonScreentip(IRibbonControl control) => Strings.ConstructionComingSoonScreentip;
        public bool OnGetConstructionEnabled(IRibbonControl control) => false; // roadmap, grisé

        public string OnGetSettingsButtonLabel(IRibbonControl control) => Strings.SettingsButtonLabel;
        public string OnGetSettingsButtonScreentip(IRibbonControl control) => Strings.SettingsButtonScreentip;

        public string OnGetAboutButtonLabel(IRibbonControl control) => Strings.AboutButtonLabel;
        public string OnGetAboutButtonScreentip(IRibbonControl control) => Strings.AboutButtonScreentip;

        // ---------- Actions ----------

        public void OnConvertClicked(IRibbonControl control)
        {
            try
            {
                var suggestions = Globals.ThisAddIn?.Suggestions;
                suggestions?.TriggerManual();
            }
            catch (Exception ex)
            {
                LogDebug("convert_click_error: " + ex.Message);
            }
        }

        /// <summary>
        /// Insère un tableau N colonnes au curseur (barres séparatrices,
        /// pas de bordures externes). N parsé depuis l'id du bouton
        /// (InsertColumns{1..4}Button ou InsertColumns{1..4}TabButton).
        /// </summary>
        public void OnInsertColumnsClicked(IRibbonControl control)
        {
            try
            {
                int n = ParseColumnCountFromId(control?.Id);
                if (n < 1 || n > 4)
                {
                    LogDebug($"insert_columns_invalid_n id={control?.Id ?? "<null>"}");
                    return;
                }
                var app = Globals.ThisAddIn?.Application;
                if (app == null) return;
                MathCursor.Host.ColumnLayoutInserter.Insert(app, n);
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
            // Format attendu : "InsertColumns{N}Button" ou
            // "InsertColumns{N}TabButton". On scan le 1er chiffre.
            foreach (char c in id)
                if (c >= '1' && c <= '9') return c - '0';
            return 0;
        }

        public void OnCheatsheetClicked(IRibbonControl control)
        {
            // Pane en pause (cf. ADR pivot) — bouton grisé via
            // OnGetCheatsheetEnabled, donc onAction ne devrait pas tirer.
            // Safe : no-op.
        }

        public void OnSettingsClicked(IRibbonControl control)
        {
            MessageBox.Show(
                Strings.SettingsComingSoonBody,
                Strings.SettingsButtonLabel,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        public void OnAboutClicked(IRibbonControl control)
        {
            MessageBox.Show(
                Strings.HelpDialogBody(CurrentVersion()),
                Strings.HelpDialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        // ---------- Legacy callbacks (existaient avant ADR 11-05) ----------

        public string OnGetReportButtonLabel(IRibbonControl control)
            => Strings.ReportButtonLabel;

        public string OnGetReportButtonScreentip(IRibbonControl control)
            => Strings.ReportButtonScreentip;

        public string OnGetContextInspectorButtonLabel(IRibbonControl control)
            => Strings.ContextInspectorButtonLabel;

        public string OnGetContextInspectorButtonScreentip(IRibbonControl control)
            => Strings.ContextInspectorButtonScreentip;

        /// <summary>
        /// Toggle du pane debug Context Inspector
        /// (cf. brief 2026-05-07-global-context-multi-zoom-ranking).
        /// </summary>
        public void OnContextInspectorClicked(IRibbonControl control)
        {
            try
            {
                Globals.ThisAddIn?.ToggleContextInspectorPane();
            }
            catch (Exception ex)
            {
                LogDebug("context_inspector_toggle_error: " + ex.Message);
                MessageBox.Show(
                    "Impossible d'ouvrir l'Inspecteur :\n" + ex.Message,
                    "MathCursor",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Ouvre la fenêtre WPF "Signaler une erreur" pré-remplie depuis le
        /// dernier <see cref="MathCursor.Host.LastActionSnapshot"/> (saisie /
        /// proposé / inséré). 3 actions dans la fenêtre : Annuler, Copier
        /// dans un mail, Envoyer (POST direct vers /api/v1/report).
        ///
        /// Cf. ADR 2026-04-30-Feat-feedback-form-cloudflare-backend.
        /// </summary>
        public void OnReportIssueClicked(IRibbonControl control)
        {
            try
            {
                var suggestions = Globals.ThisAddIn?.Suggestions;
                if (suggestions == null)
                {
                    MessageBox.Show(
                        Strings.ReportFailedBody(FeedbackBundle.ContactEmail),
                        Strings.ReportFailedTitle,
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
                // Capture le screen AVANT de cacher la popup (la popup de
                // suggestion fait partie du contexte du bug et est utile à
                // voir). Ensuite on cache la popup pour ne pas qu'elle se
                // superpose au dialog. Le dialog est ouvert APRÈS capture
                // donc n'apparaît jamais dans le screenshot.
                byte[] preScreenshot = null;
                try { preScreenshot = MathCursor.Host.FeedbackBundle.CaptureScreenshotPng(); } catch { }
                try { suggestions.HidePopup(); } catch { }
                var report = suggestions.BuildFeedbackReport();
                if (preScreenshot != null && preScreenshot.Length > 0)
                    report.ScreenshotPngBase64 = Convert.ToBase64String(preScreenshot);
                var sender = MathCursor.Host.Feedback.FeedbackSenderFactory.Create();
                var dialog = new MathCursor.UI.FeedbackDialog(report, sender);
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                LogDebug("report_dialog_error: " + ex.Message);
                MessageBox.Show(
                    Strings.ReportFailedBody(FeedbackBundle.ContactEmail),
                    Strings.ReportFailedTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }
}
