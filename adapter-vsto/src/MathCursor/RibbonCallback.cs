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
        public string OnGetHelpGroupLabel(IRibbonControl control) => Strings.HelpGroupLabel;
        public string OnGetConvertButtonLabel(IRibbonControl control) => Strings.ConvertButtonLabel;
        public string OnGetConvertButtonScreentip(IRibbonControl control) => Strings.ConvertButtonScreentip;
        public string OnGetReportButtonLabel(IRibbonControl control) => Strings.ReportButtonLabel;
        public string OnGetReportButtonScreentip(IRibbonControl control) => Strings.ReportButtonScreentip;
        public string OnGetAboutButtonLabel(IRibbonControl control) => Strings.AboutButtonLabel;
        public string OnGetAboutButtonScreentip(IRibbonControl control) => Strings.AboutButtonScreentip;

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
