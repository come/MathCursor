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

        public void OnAboutClicked(IRibbonControl control)
        {
            MessageBox.Show(
                "MathCursor — Notation math au clavier pour Word\n" +
                "Version 0.3.2 — beta\n\n" +
                "COMMENT ÇA MARCHE\n" +
                "  Tape simplement ton expression en texte (ex: f(x)=1/x, somme de k=1 à n, lim x→0).\n" +
                "  Quand MathCursor détecte de la math, une petite popup apparaît avec des propositions.\n\n" +
                "RACCOURCIS\n" +
                "  Ctrl+Espace  → forcer la popup sur ce que tu viens de taper\n" +
                "                 (utile si rien ne s'est ouvert tout seul)\n" +
                "  Flèche bas   → entrer dans la popup et naviguer\n" +
                "  Flèche haut  → remonter dans la liste\n" +
                "  Entrée       → valider la proposition sélectionnée (en mode nav)\n" +
                "  Échap        → masquer la popup\n\n" +
                "REVENIR SUR UNE ÉQUATION\n" +
                "  Replace ton curseur DANS une équation déjà insérée : la popup se rouvre avec\n" +
                "  les variantes. Valide pour remplacer, ou clique ailleurs pour garder.\n\n" +
                "UN SOUCI ? UNE IDÉE ?\n" +
                "  Bouton \"Signaler un souci\" à gauche → génère un rapport prêt à envoyer\n" +
                "  (WhatsApp ou email). Ton feedback fait avancer le produit !\n\n" +
                "Logs techniques : %AppData%\\MathCursor\\logs\\mathcursor.log",
                "MathCursor — Aide",
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
                    "Impossible de créer le rapport.\n" +
                    "Envoie-nous un message à " + FeedbackBundle.ContactEmail + " " +
                    "en décrivant ce qui s'est passé.",
                    "MathCursor — Signaler un souci",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            FeedbackBundle.CopyToClipboardAsFile(zipPath);

            var result = MessageBox.Show(
                "Le rapport est prêt !\n\n" +
                "Fichier copié dans le presse-papier — colle-le (Ctrl+V) dans :\n" +
                "  • Le groupe WhatsApp beta-testeurs :\n    " + FeedbackBundle.WhatsAppGroupUrl + "\n" +
                "  • Ou un email à " + FeedbackBundle.ContactEmail + "\n\n" +
                "Ajoute un petit mot : ce que tu voulais faire, ce que l'add-in a fait à la place.\n\n" +
                "Chemin du fichier : " + zipPath + "\n\n" +
                "Ouvrir le groupe WhatsApp dans le navigateur ?",
                "MathCursor — Rapport prêt",
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
