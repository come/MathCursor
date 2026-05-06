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


        public string OnGetReportButtonLabel(IRibbonControl control)
            => Strings.ReportButtonLabel;

        public string OnGetReportButtonScreentip(IRibbonControl control)
            => Strings.ReportButtonScreentip;

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
