// MathCursor: capturing mathematical intent from linear keyboard input.
// Copyright (C) 2026  Côme de Percin
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

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

        /// <summary>Rafraîchit le label de l'onglet pour (dé)afficher le marqueur
        /// « MAJ dispo ». Appelé par UpdateChecker quand une version plus récente
        /// est détectée. Cf. ADR 2026-06-18-Feat-ribbon-update-badge.</summary>
        internal void InvalidateUpdateBadge()
        {
            // Invalidation COMPLÈTE (1×/session) : l'apparition d'un groupe via
            // getVisible est peu fiable avec InvalidateControl (Office cache la
            // mise en page des groupes) → on re-évalue tout le ruban une fois.
            try { _ribbon?.Invalidate(); }
            catch
            {
                // Repli ciblé si Invalidate() échoue pour une raison quelconque.
                try { _ribbon?.InvalidateControl("MathCursorTab"); } catch { }
                try { _ribbon?.InvalidateControl("MathCursorUpdateGroup"); } catch { }
            }
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

        public string OnGetTabLabel(IRibbonControl control) =>
            Host.Update.UpdateChecker.UpdateAvailable
                ? Strings.MathCursorTabLabelUpdate
                : Strings.MathCursorTabLabel;
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
        public string OnGetBoxResultButtonLabel(IRibbonControl control) => Strings.BoxResultButtonLabel;
        public string OnGetBoxResultButtonScreentip(IRibbonControl control) => Strings.BoxResultButtonScreentip;
        public string OnGetCalloutMenuLabel(IRibbonControl control) => Strings.CalloutMenuLabel;
        public string OnGetCalloutMenuScreentip(IRibbonControl control) => Strings.CalloutMenuScreentip;
        public string OnGetCalloutTheoremLabel(IRibbonControl control) => Host.CalloutInserter.Styles["theorem"].Label;
        public string OnGetCalloutDefinitionLabel(IRibbonControl control) => Host.CalloutInserter.Styles["definition"].Label;
        public string OnGetCalloutExampleLabel(IRibbonControl control) => Host.CalloutInserter.Styles["example"].Label;
        public string OnGetCalloutPropertyLabel(IRibbonControl control) => Host.CalloutInserter.Styles["property"].Label;
        public string OnGetReportButtonLabel(IRibbonControl control) => Strings.ReportButtonLabel;
        public string OnGetReportButtonScreentip(IRibbonControl control) => Strings.ReportButtonScreentip;
        public string OnGetAboutButtonLabel(IRibbonControl control) => Strings.AboutButtonLabel;
        public string OnGetAboutButtonScreentip(IRibbonControl control) => Strings.AboutButtonScreentip;
        public string OnGetTutorialButtonLabel(IRibbonControl control) => Strings.TutorialButtonLabel;
        public string OnGetTutorialButtonScreentip(IRibbonControl control) => Strings.TutorialButtonScreentip;
        // Groupe « Mise à jour » : visible UNIQUEMENT quand une MAJ est dispo.
        public bool OnGetUpdateGroupVisible(IRibbonControl control) => Host.Update.UpdateChecker.UpdateAvailable;
        public string OnGetUpdateGroupLabel(IRibbonControl control) => Strings.UpdateGroupLabel;
        public string OnGetUpdateButtonLabel(IRibbonControl control) => Strings.UpdateButtonLabel;
        public string OnGetUpdateButtonScreentip(IRibbonControl control) => Strings.UpdateButtonScreentip;

        // Icône custom « tuile jaune + flèche téléchargement » : le ruban Office
        // ne permet pas de fond coloré sur un bouton, donc le jaune passe par
        // l'icône (getImage). Mise en cache (l'image est constante).
        private static stdole.IPictureDisp _updateIcon;
        public stdole.IPictureDisp OnGetUpdateButtonImage(IRibbonControl control)
        {
            try
            {
                if (_updateIcon == null)
                    using (var bmp = BuildUpdateIcon())
                        _updateIcon = PictureConverter.ToPictureDisp(bmp);
                return _updateIcon;
            }
            catch (Exception ex) { LogDebug("update_image_error: " + ex.Message); return null; }
        }

        // ---------- Actions ----------

        /// <summary>Équivalent du raccourci Ctrl+Espace.</summary>
        public void OnConvertClicked(IRibbonControl control)
        {
            try { Globals.ThisAddIn?.Conversion?.Trigger(); }
            catch (Exception ex) { LogDebug("convert_clicked_error: " + ex.Message); }
        }

        /// <summary>Encadre l'équation sous le caret (\boxed). No-op si le
        /// caret n'est pas sur une de nos équations (cf. ConversionController).</summary>
        public void OnBoxResultClicked(IRibbonControl control)
        {
            try { Globals.ThisAddIn?.Conversion?.BoxAtCaret(); }
            catch (Exception ex) { LogDebug("box_result_clicked_error: " + ex.Message); }
        }

        /// <summary>Insère un encadré coloré typé autour de la sélection (ou du
        /// ¶ courant). Le type est résolu depuis l'id du bouton du menu.</summary>
        public void OnInsertCalloutClicked(IRibbonControl control)
        {
            try
            {
                string key = CalloutKeyFromId(control?.Id);
                if (key == null || !Host.CalloutInserter.Styles.TryGetValue(key, out var style))
                { LogDebug($"callout: id inconnu {control?.Id ?? "<null>"}"); return; }
                var app = Globals.ThisAddIn?.Application;
                if (app == null) { LogDebug("callout: app null"); return; }
                Host.CalloutInserter.Insert(app, style);
                LogDebug($"callout_done key={key}");
            }
            catch (Exception ex)
            {
                LogDebug("callout_error: " + ex.Message);
                MessageBox.Show(
                    Strings.CalloutInsertFailed + ex.Message,
                    "MathCursor", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // "CalloutTheoremButton" → "theorem", etc.
        private static string CalloutKeyFromId(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (id.IndexOf("Theorem", StringComparison.OrdinalIgnoreCase) >= 0) return "theorem";
            if (id.IndexOf("Definition", StringComparison.OrdinalIgnoreCase) >= 0) return "definition";
            if (id.IndexOf("Example", StringComparison.OrdinalIgnoreCase) >= 0) return "example";
            if (id.IndexOf("Property", StringComparison.OrdinalIgnoreCase) >= 0) return "property";
            return null;
        }

        // ---------- Sélecteur de police math (ADR 2026-06-22) ----------

        public string OnGetMathFontLabel(IRibbonControl control) => Strings.MathFontLabel;
        public string OnGetMathFontScreentip(IRibbonControl control) => Strings.MathFontScreentip;
        public int OnGetMathFontItemCount(IRibbonControl control) => Host.MathFontCatalog.Fonts.Length;
        public string OnGetMathFontItemId(IRibbonControl control, int index) => "mathfont_" + index;

        /// <summary>Libellé d'un item : nom de la fonte, suffixé « (défaut) » pour
        /// Cambria et « (à installer) » si absente du poste.</summary>
        public string OnGetMathFontItemLabel(IRibbonControl control, int index)
        {
            try
            {
                var fonts = Host.MathFontCatalog.Fonts;
                if (index < 0 || index >= fonts.Length) return "";
                string font = fonts[index];
                string label = Host.MathFontCatalog.IsDefault(font)
                    ? font + " " + Strings.MathFontDefaultSuffix
                    : font;
                var app = Globals.ThisAddIn?.Application;
                if (app != null && !Host.MathFontApplier.IsInstalled(app, font))
                    label += " " + Strings.MathFontNotInstalledSuffix;
                return label;
            }
            catch (Exception ex)
            {
                LogDebug("mathfont_item_label_error: " + ex.Message);
                return Host.MathFontCatalog.Fonts[index];
            }
        }

        public int OnGetMathFontSelectedIndex(IRibbonControl control)
        {
            try { return Host.MathFontCatalog.IndexOf(Host.Settings.SettingsStore.Current.MathFont); }
            catch { return 0; }
        }

        /// <summary>Choix d'une police : (1) mémorise le préréglage des futures
        /// insertions MathCursor, (2) pose la fonte sur toutes les équations du
        /// doc. Avertit si la fonte n'est pas installée (Word retombe sur Cambria).</summary>
        public void OnMathFontSelected(IRibbonControl control, string itemId, int index)
        {
            try
            {
                var fonts = Host.MathFontCatalog.Fonts;
                if (index < 0 || index >= fonts.Length) return;
                string font = fonts[index];

                // 1) Préréglage persistant (futures insertions MathCursor).
                var s = Host.Settings.SettingsStore.Current.Clone();
                s.MathFont = font;
                Host.Settings.SettingsStore.Save(s);

                // 2) Doc entier : poser la fonte sur toutes les équations actuelles.
                var app = Globals.ThisAddIn?.Application;
                var doc = app?.ActiveDocument;
                int n = Host.MathFontApplier.ApplyToDocument(doc, font, LogDebug);

                if (app != null && !Host.MathFontApplier.IsInstalled(app, font))
                {
                    string url = Host.MathFontCatalog.DownloadUrl(font);
                    if (url != null)
                    {
                        var r = MessageBox.Show(
                            Strings.MathFontNotInstalledBody(font),
                            Strings.MathFontNotInstalledTitle,
                            MessageBoxButton.YesNo, MessageBoxImage.Information);
                        if (r == MessageBoxResult.Yes)
                        {
                            try { System.Diagnostics.Process.Start(url); }
                            catch (Exception exU) { LogDebug("mathfont_open_url_error: " + exU.Message); }
                        }
                    }
                    else
                    {
                        MessageBox.Show(
                            Strings.MathFontNotInstalledBody(font),
                            Strings.MathFontNotInstalledTitle,
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }

                LogDebug($"mathfont_selected \"{font}\" applied={n}");
            }
            catch (Exception ex) { LogDebug("mathfont_selected_error: " + ex.Message); }
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
                    Strings.ColumnsInsertFailed + ex.Message,
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
            // Si une MAJ est dispo, l'onglet porte le marqueur « ● MAJ » : le clic
            // « À propos » sert alors de chemin d'action → propose d'ouvrir la page
            // de téléchargement. Cf. ADR 2026-06-18-Feat-ribbon-update-badge.
            if (Host.Update.UpdateChecker.UpdateAvailable)
            {
                var r = MessageBox.Show(
                    Strings.UpdateAvailableBody(CurrentVersion(), Host.Update.UpdateChecker.LatestVersion),
                    Strings.UpdateAvailableTitle,
                    MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (r == MessageBoxResult.Yes) OpenDownloadPage();
                return;
            }

            MessageBox.Show(
                Strings.HelpDialogBody(CurrentVersion()),
                Strings.HelpDialogTitle,
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>Bouton « Mettre à jour » (visible seulement si MAJ dispo) →
        /// ouvre la page de téléchargement. Cf. ADR 2026-06-18-Feat-ribbon-update-badge.</summary>
        public void OnUpdateClicked(IRibbonControl control) => OpenDownloadPage();

        private static void OpenDownloadPage()
        {
            try { System.Diagnostics.Process.Start("https://mathcursor.pages.dev/releases.html"); }
            catch (Exception ex) { LogDebug("open_releases_error: " + ex.Message); }
        }

        // ---------- Internals ----------

        private static string CurrentVersion()
        {
            try { return Strings.FormatVersion(Assembly.GetExecutingAssembly().GetName().Version); }
            catch { return "?"; }
        }

        /// <summary>Icône 32×32 du bouton « Mise à jour disponible » : tuile jaune
        /// arrondie (jaune de marque #FDE047) + flèche de téléchargement bleu encre
        /// (#00236F). Cf. ADR 2026-06-18-Feat-ribbon-update-badge.</summary>
        private static System.Drawing.Bitmap BuildUpdateIcon()
        {
            var bmp = new System.Drawing.Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(System.Drawing.Color.Transparent);

                var rect = new System.Drawing.Rectangle(1, 1, 30, 30);
                using (var path = RoundedRect(rect, 6))
                using (var fill = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0xFD, 0xE0, 0x47)))
                    g.FillPath(fill, path);

                using (var ink = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0x00, 0x23, 0x6F)))
                {
                    g.FillRectangle(ink, 14, 7, 4, 9); // tige de la flèche
                    g.FillPolygon(ink, new[]           // pointe vers le bas
                    {
                        new System.Drawing.Point(9, 14),
                        new System.Drawing.Point(23, 14),
                        new System.Drawing.Point(16, 24),
                    });
                    g.FillRectangle(ink, 9, 26, 14, 3); // bac (base du download)
                }
            }
            return bmp;
        }

        private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(System.Drawing.Rectangle r, int radius)
        {
            int d = radius * 2;
            var p = new System.Drawing.Drawing2D.GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>Convertit un <see cref="System.Drawing.Image"/> en
        /// <c>IPictureDisp</c> attendu par le ruban (callback getImage). Astuce
        /// AxHost standard (GetIPictureDispFromPicture est protégé statique).</summary>
        private sealed class PictureConverter : System.Windows.Forms.AxHost
        {
            private PictureConverter() : base("59EE46BA-677D-4d20-BF10-8D8067CB8B33") { }
            public static stdole.IPictureDisp ToPictureDisp(System.Drawing.Image image)
                => (stdole.IPictureDisp)GetIPictureDispFromPicture(image);
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
