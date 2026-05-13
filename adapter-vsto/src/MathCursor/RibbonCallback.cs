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

        // ---------- getImage (icônes PNG embarquées) ----------

        /// <summary>
        /// Callback générique <c>getImage</c> du Ribbon. Charge le PNG
        /// embarqué correspondant à <see cref="IRibbonControl.Id"/>.
        /// Taille fixée à 32×32 (Office downscale proprement vers 16
        /// pour les boutons <c>size="normal"</c>). PNG générés par
        /// <c>tools/icons/rasterize-ribbon-icons.ps1</c>.
        /// </summary>
        public System.Drawing.Bitmap OnGetButtonImage(IRibbonControl control)
        {
            if (control == null) return null;
            string icon = MapControlIdToIcon(control.Id);
            if (icon == null) return null;
            return LoadEmbeddedIcon(icon, 32);
        }

        /// <summary>
        /// Mappe <c>control.Id</c> du Ribbon vers le slug d'icône PNG.
        /// Liste exhaustive (cf. Ribbon.xml). Retourne null si pas mappé
        /// (Office fallback à pas d'image).
        /// </summary>
        private static string MapControlIdToIcon(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            // Plus spécifiques d'abord (ex. InsertColumns1Button avant
            // un éventuel Contains("Columns")).
            if (id.StartsWith("InsertColumns1", StringComparison.Ordinal)) return "columns-1";
            if (id.StartsWith("InsertColumns2", StringComparison.Ordinal)) return "columns-2";
            if (id.StartsWith("InsertColumns3", StringComparison.Ordinal)) return "columns-3";
            if (id.StartsWith("InsertColumns4", StringComparison.Ordinal)) return "columns-4";
            if (id.Contains("Columns"))                       return "columns-2";   // menu trigger
            if (id.Contains("Cheatsheet"))                    return "cheatsheet";
            if (id.Contains("SignTable"))                     return "sign-table";
            if (id.Contains("VariationTable"))                return "variation-table";
            if (id.Contains("Curve"))                         return "curve";
            if (id.Contains("Figure"))                        return "figure";
            if (id.Contains("Settings"))                      return "settings";
            if (id.Contains("ReportIssue"))                   return "report-bug";
            if (id.Contains("ContextInspector"))              return "inspector";
            if (id.Contains("About"))                         return "about";
            return null;
        }

        /// <summary>Charge un PNG embarqué <c>MathCursor.Resources.ribbon-{name}-{size}.png</c>.</summary>
        private static System.Drawing.Bitmap LoadEmbeddedIcon(string name, int size)
        {
            try
            {
                string resourceName = $"MathCursor.Resources.ribbon-{name}-{size}.png";
                var asm = Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        LogDebug($"ribbon_icon_missing: {resourceName}");
                        return null;
                    }
                    return new System.Drawing.Bitmap(stream);
                }
            }
            catch (Exception ex)
            {
                LogDebug($"ribbon_icon_load_error: {ex.Message}");
                return null;
            }
        }

        // ---------- Actions ----------

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
        /// Bouton de debug : insère une OMath simple <c>f(x)=1</c> à la
        /// position du curseur via le chemin Word natif minimal
        /// (Selection.TypeText + OMaths.Add + BuildUp), puis place le caret
        /// à la fin. Sert à isoler les bugs d'insertion/caret sans passer
        /// par le pipeline NER + popup + staging.
        /// </summary>
        public void OnDebugInsertOMathClicked(IRibbonControl control)
        {
            try
            {
                var app = Globals.ThisAddIn?.Application;
                if (app == null) return;
                var doc = app.ActiveDocument;
                if (doc == null) return;
                var sel = app.Selection;
                if (sel == null) return;

                int insertPos = sel.Start;
                LogDebug($"debug_insert: start at sel.Start={insertPos} docEnd={doc.Content.End}");

                // 1. Type "f(x)=1" à la position du caret.
                sel.TypeText("f(x)=1");

                // 2. Convertit la range typed en OMath via le pipeline Word natif.
                int afterTypedEnd = insertPos + 6;
                var typedRange = doc.Range(insertPos, afterTypedEnd);
                typedRange.OMaths.Add(typedRange);
                typedRange.OMaths.BuildUp();

                // 3. Retrouve l'OMath fraîchement créée pour logger sa range
                //    (= observer où Word l'a effectivement insérée).
                Microsoft.Office.Interop.Word.OMath om = null;
                foreach (Microsoft.Office.Interop.Word.OMath o in doc.OMaths)
                {
                    if (o.Range.Start >= insertPos && o.Range.End <= afterTypedEnd + 30)
                    { om = o; break; }
                }
                if (om != null)
                {
                    LogDebug($"debug_insert: om.Range=[{om.Range.Start},{om.Range.End}] (caret laissé où Word le place naturellement)");

                    // 4. Alignement gauche : patch m:jc=left dans le ¶ XML
                    //    via OMathParaJcPatcher, puis re-insère le ¶ patché.
                    //    Évite om.Justification setter (qui jette sur OMath
                    //    inline « Impossible de définir l'alignement »).
                    try
                    {
                        var paraRange = om.Range.Paragraphs[1].Range;
                        string xml = paraRange.WordOpenXML;
                        if (!string.IsNullOrEmpty(xml))
                        {
                            string patched = OMathParaJcPatcher.EnsureDisplayWithLeftJc(xml, out bool changed);
                            LogDebug($"debug_insert: align xml changed={changed} (xmlLen={xml.Length} patchedLen={patched?.Length ?? 0})");
                            if (changed)
                            {
                                paraRange.InsertXML(patched);
                            }
                        }
                    }
                    catch (Exception exAlign) { LogDebug("debug_insert.align_error: " + exAlign.Message); }
                }
                else
                {
                    LogDebug("debug_insert: OMath not found post-BuildUp");
                }

                // 4. Positionnement du caret — DÉSACTIVÉ pour observer où
                //    Word laisse le caret naturellement après OMaths.Add +
                //    BuildUp, sans qu'on touche au Selection.
                // if (om != null)
                // {
                //     int caretTarget = Math.Min(om.Range.End + 1, doc.Content.End);
                //     try { sel.SetRange(caretTarget, caretTarget); }
                //     catch (Exception exSet) { LogDebug("debug_insert.setrange_error: " + exSet.Message); }
                // }
            }
            catch (Exception ex)
            {
                LogDebug("debug_insert_error: " + ex.Message);
                MessageBox.Show(
                    "Debug insert OMath failed :\n" + ex.Message,
                    "MathCursor — Debug",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
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
