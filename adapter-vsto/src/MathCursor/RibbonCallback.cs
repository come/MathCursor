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

        public string OnGetGroupLabel(IRibbonControl control)
            => Strings.GroupLabel(CurrentVersion());

        public string OnGetReportButtonLabel(IRibbonControl control)
            => Strings.ReportButtonLabel;

        public string OnGetReportButtonScreentip(IRibbonControl control)
            => Strings.ReportButtonScreentip;

        public string OnGetAboutButtonLabel(IRibbonControl control)
            => Strings.AboutButtonLabel;

        public string OnGetAboutButtonScreentip(IRibbonControl control)
            => Strings.AboutButtonScreentip;

        public void OnAboutClicked(IRibbonControl control)
        {
            MessageBox.Show(
                Strings.HelpDialogBody(CurrentVersion()),
                Strings.HelpDialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        /// <summary>
        /// Crée un zip avec log + screenshot Word + contexte (paragraphe,
        /// versions), le met dans le presse-papier comme fichier droppable,
        /// et affiche un dialogue expliquant comment l'envoyer (WhatsApp ou
        /// email). Un bouton "Ouvrir le dossier" comme plan B si le clipboard
        /// ne marche pas.
        /// </summary>
        public void OnReportIssueClicked(IRibbonControl control)
        {
            string zipPath;
            try
            {
                var app = Globals.ThisAddIn?.Application;
                zipPath = FeedbackBundle.Create(app);
            }
            catch (Exception ex)
            {
                LogDebug("report_create_error: " + ex.Message);
                zipPath = null;
            }

            if (string.IsNullOrEmpty(zipPath) || !File.Exists(zipPath))
            {
                MessageBox.Show(
                    Strings.ReportFailedBody(FeedbackBundle.ContactEmail),
                    Strings.ReportFailedTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            FeedbackBundle.CopyToClipboardAsFile(zipPath);

            var result = MessageBox.Show(
                Strings.ReportReadyBody(FeedbackBundle.WhatsAppGroupUrl, FeedbackBundle.ContactEmail, zipPath),
                Strings.ReportReadyTitle,
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = FeedbackBundle.WhatsAppGroupUrl,
                        UseShellExecute = true,
                    });
                }
                catch (Exception ex) { LogDebug("wa_open_error: " + ex.Message); }
            }
            else if (result == MessageBoxResult.No)
            {
                FeedbackBundle.RevealInExplorer(zipPath);
            }
        }
    }
}
