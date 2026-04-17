using System;
using System.IO;
using System.Reflection;
using System.Windows;
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
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "MathCursor.Ribbon.xml";
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException(
                        "Ressource ribbon introuvable : " + resourceName);
                }
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        public void OnRibbonLoad(IRibbonUI ribbon)
        {
            _ribbon = ribbon;
        }

        public void OnConvertClicked(IRibbonControl control)
        {
            Globals.ThisAddIn?.TriggerConversion();
        }

        public void OnAboutClicked(IRibbonControl control)
        {
            MessageBox.Show(
                "MathCursor 0.1.0\n\n" +
                "Notation math au clavier pour Word Desktop.\n\n" +
                "Usage : tapez votre expression (ex : f(x)=1/x), puis cliquez\n" +
                "'Convertir' (Alt+M) pour la transformer en équation Word.",
                "À propos de MathCursor",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
