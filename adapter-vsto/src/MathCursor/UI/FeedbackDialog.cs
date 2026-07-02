// MathCursor — capture d'intention mathématique depuis une saisie clavier linéaire.
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
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using MathCursor.Host;
using MathCursor.Host.Feedback;

namespace MathCursor.UI
{
    /// <summary>
    /// Fenêtre WPF modale "Signaler une erreur" — version v0.5.x avec 3
    /// boutons d'action (Annuler / Copier dans un mail / Envoyer) et
    /// 3 champs pré-remplis depuis le <see cref="LastActionSnapshot"/> :
    ///   - Ce que l'user a tapé
    ///   - Ce que MathCursor a proposé en popup
    ///   - Ce qui a été inséré dans Word
    ///
    /// Cf. brief 2026-04-30-feedback-form-with-cloudflare-backend.md et
    /// ADR 2026-04-30-Feat-feedback-form-cloudflare-backend.
    ///
    /// Comportement Envoyer :
    ///   POST async vers l'endpoint Cloudflare via <see cref="HttpFeedbackSender"/>.
    ///   En cas d'échec (timeout, proxy bloque), bascule automatique sur
    ///   "Copier dans un mail" sans perdre la saisie.
    ///
    /// Comportement Copier dans un mail :
    ///   Compose un payload texte lisible, le met dans le presse-papier,
    ///   ouvre le client mail par défaut via mailto:. L'user n'a qu'à Ctrl+V.
    ///
    /// Word perd le focus pendant que la fenêtre est ouverte (modale).
    /// </summary>
    public sealed class FeedbackDialog : Window
    {
        private const string ContactEmail = "come2percin@wanadev.fr";
        private const string PrivacyUrl = "https://mathcursor.pages.dev/privacy.html";

        private readonly FeedbackReport _report;
        private readonly IFeedbackSender _sender;

        private readonly TextBox _sourceBox;
        private readonly TextBox _proposedBox;
        private readonly TextBox _committedBox;
        private readonly TextBox _commentBox;
        private readonly CheckBox _includeScreenshot;
        private readonly CheckBox _includeLog;
        private readonly Image _screenshotPreview;
        private readonly Button _sendButton;
        private readonly Hyperlink _copyMailLink;
        private readonly TextBlock _statusLabel;

        public FeedbackDialog(FeedbackReport prefilledReport, IFeedbackSender sender)
        {
            _report = prefilledReport ?? throw new ArgumentNullException(nameof(prefilledReport));
            _sender = sender ?? throw new ArgumentNullException(nameof(sender));

            Title = Strings.FeedbackTitle;
            Width = 540;
            Height = 720;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            ShowInTaskbar = false;
            MinWidth = 460;
            MinHeight = 600;

            var root = new StackPanel { Margin = new Thickness(16) };

            root.Children.Add(new TextBlock
            {
                Text = Strings.FeedbackHeader,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4),
            });
            root.Children.Add(new TextBlock
            {
                Text = Strings.FeedbackIntro,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(110, 110, 110)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
            });

            // ── Section : dernière action (3 champs éditables) ──────────
            root.Children.Add(SectionHeader(Strings.FeedbackSectionLastAction));

            root.Children.Add(Label(Strings.FeedbackLabelWhatTyped));
            _sourceBox = ReadOnlyBox(_report.NerText);
            root.Children.Add(_sourceBox);

            root.Children.Add(Label(Strings.FeedbackLabelWhatProposed));
            _proposedBox = ReadOnlyBox(_report.RecognizedFormula);
            root.Children.Add(_proposedBox);

            root.Children.Add(Label(Strings.FeedbackLabelWhatInserted));
            _committedBox = ReadOnlyBox(_report.CommittedLatex);
            root.Children.Add(_committedBox);

            // ── Section : commentaire libre ────────────────────────────
            root.Children.Add(SectionHeader(Strings.FeedbackSectionDescribe));
            _commentBox = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 100,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontSize = 12,
                Padding = new Thickness(6),
                Margin = new Thickness(0, 0, 0, 10),
            };
            root.Children.Add(_commentBox);

            // ── Toggles (screenshot / log) ─────────────────────────────
            _includeScreenshot = new CheckBox
            {
                Content = Strings.FeedbackToggleScreenshot,
                IsChecked = true,
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 4),
            };
            _includeScreenshot.Checked += (_, __) => UpdateScreenshotPreview();
            _includeScreenshot.Unchecked += (_, __) => UpdateScreenshotPreview();
            root.Children.Add(_includeScreenshot);

            // Prévisualisation de la capture pré-enregistrée (par
            // OnReportRequested / OnReportIssueClicked AVANT l'ouverture).
            // L'user voit exactement ce qui sera envoyé. Visible/caché selon
            // le toggle ci-dessus.
            _screenshotPreview = new Image
            {
                MaxHeight = 180,
                MaxWidth = 480,
                Margin = new Thickness(0, 4, 0, 8),
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            root.Children.Add(_screenshotPreview);

            _includeLog = new CheckBox
            {
                Content = Strings.FeedbackToggleLog,
                IsChecked = false,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 8),
            };
            root.Children.Add(_includeLog);

            // ── Disclaimer + lien privacy ──────────────────────────────
            var disclaimer = new TextBlock
            {
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(110, 110, 110)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
            };
            disclaimer.Inlines.Add(Strings.FeedbackDisclaimerPart1);
            var privacyLink = new Hyperlink(new Run(Strings.FeedbackDisclaimerLink))
            {
                NavigateUri = new Uri(PrivacyUrl),
            };
            privacyLink.RequestNavigate += (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
                catch { }
                e.Handled = true;
            };
            disclaimer.Inlines.Add(privacyLink);
            disclaimer.Inlines.Add(".");
            root.Children.Add(disclaimer);

            _statusLabel = new TextBlock
            {
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 4),
                TextWrapping = TextWrapping.Wrap,
            };
            root.Children.Add(_statusLabel);

            // ── Boutons ────────────────────────────────────────────────
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0),
            };
            var cancelButton = new Button
            {
                Content = Strings.FeedbackButtonCancel,
                Padding = new Thickness(14, 5, 14, 5),
                Margin = new Thickness(0, 0, 8, 0),
                MinWidth = 80,
            };
            cancelButton.Click += (_, __) => Close();

            _sendButton = new Button
            {
                Content = Strings.FeedbackButtonSend,
                Padding = new Thickness(18, 6, 18, 6),
                MinWidth = 100,
                IsDefault = true,
                FontWeight = FontWeights.SemiBold,
            };
            _sendButton.Click += async (_, __) => await OnSendClickedAsync();

            buttons.Children.Add(cancelButton);
            buttons.Children.Add(_sendButton);
            root.Children.Add(buttons);

            // Lien secondaire en bas de fenêtre. L'envoi direct est l'action
            // principale (bouton accent à droite) ; "Copier dans un mail" est
            // un plan B (proxy d'entreprise, traçabilité mail souhaitée par
            // l'user). On le matérialise en lien discret pour ne pas
            // concurrencer visuellement le bouton Envoyer.
            var altActionPanel = new TextBlock
            {
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(110, 110, 110)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 12, 0, 0),
            };
            altActionPanel.Inlines.Add(Strings.FeedbackAltActionPrefix);
            _copyMailLink = new Hyperlink(new Run(Strings.FeedbackAltActionLink));
            _copyMailLink.Click += (_, __) => OnCopyMailClicked();
            altActionPanel.Inlines.Add(_copyMailLink);
            root.Children.Add(altActionPanel);

            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = root,
            };

            Loaded += (_, __) => { _commentBox.Focus(); UpdateScreenshotPreview(); };
        }

        /// <summary>Affiche/cache la prévisualisation du screenshot selon le
        /// toggle. Décode le PNG base64 stocké dans <see cref="_report"/>.</summary>
        private void UpdateScreenshotPreview()
        {
            if (_screenshotPreview == null) return;
            if (_includeScreenshot.IsChecked == true
                && !string.IsNullOrEmpty(_report.ScreenshotPngBase64))
            {
                try
                {
                    var bytes = Convert.FromBase64String(_report.ScreenshotPngBase64);
                    using (var ms = new MemoryStream(bytes))
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad; // détache du stream
                        bmp.StreamSource = ms;
                        bmp.EndInit();
                        bmp.Freeze();
                        _screenshotPreview.Source = bmp;
                    }
                    _screenshotPreview.Visibility = Visibility.Visible;
                    return;
                }
                catch { /* fallback : on cache */ }
            }
            _screenshotPreview.Source = null;
            _screenshotPreview.Visibility = Visibility.Collapsed;
        }

        /// <summary>Vérifie qu'au moins le commentaire OU la saisie source est
        /// rempli avant l'envoi. Affiche un message + focus le champ vide.
        /// Retourne true si OK pour envoyer.</summary>
        private bool ValidateBeforeSend()
        {
            bool hasComment = !string.IsNullOrWhiteSpace(_commentBox.Text);
            bool hasSource = !string.IsNullOrWhiteSpace(_sourceBox.Text);
            if (!hasComment && !hasSource)
            {
                _statusLabel.Foreground = new SolidColorBrush(Color.FromRgb(180, 0, 0));
                _statusLabel.Text = Strings.FeedbackValidationEmpty;
                _commentBox.Focus();
                return false;
            }
            return true;
        }

        /// <summary>Synchronise les TextBox éditables → champs du report,
        /// charge screenshot/log si togglés.</summary>
        private void SyncReportFromUI()
        {
            // Les 3 champs formules sont read-only → pas de sync nécessaire,
            // _report les contient déjà tels que pré-remplis par
            // SuggestionService.BuildFeedbackReport.
            _report.UserMessage = _commentBox.Text ?? "";

            if (_includeScreenshot.IsChecked == true)
            {
                // Si le caller a déjà pré-capturé (cas standard depuis
                // OnReportRequested / OnReportIssueClicked qui prennent le
                // screen AVANT que le dialog soit visible), on le garde.
                // Sinon on capture maintenant — le dialog sera dans l'image,
                // mais c'est mieux que pas de screenshot.
                if (string.IsNullOrEmpty(_report.ScreenshotPngBase64))
                {
                    try
                    {
                        var png = FeedbackBundle.CaptureScreenshotPng();
                        if (png != null && png.Length > 0)
                            _report.ScreenshotPngBase64 = Convert.ToBase64String(png);
                    }
                    catch { _report.ScreenshotPngBase64 = ""; }
                }
            }
            else _report.ScreenshotPngBase64 = "";

            if (_includeLog.IsChecked == true)
            {
                try
                {
                    var bytes = FeedbackBundle.ReadLogTail();
                    if (bytes != null && bytes.Length > 0)
                        _report.LogTail = System.Text.Encoding.UTF8.GetString(bytes);
                }
                catch { _report.LogTail = ""; }
            }
            else _report.LogTail = "";
        }

        private async Task OnSendClickedAsync()
        {
            if (!ValidateBeforeSend()) return;
            SyncReportFromUI();

            _sendButton.IsEnabled = false;
            _copyMailLink.IsEnabled = false;
            Cursor = Cursors.Wait;
            _statusLabel.Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80));
            _statusLabel.Text = Strings.FeedbackStatusSending;

            FeedbackResult result;
            try { result = await _sender.SendAsync(_report); }
            catch (Exception ex)
            {
                result = new FeedbackResult
                {
                    Success = false,
                    DisplayMessage = "Erreur inattendue pendant l'envoi.",
                    ErrorDetail = ex.Message,
                };
            }
            Cursor = Cursors.Arrow;

            if (result.Success)
            {
                _statusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0, 140, 0));
                _statusLabel.Text = Strings.FeedbackStatusSent;
                await Task.Delay(1500);
                Close();
                return;
            }

            // Bascule auto : on ne perd PAS la saisie. On informe et on bascule
            // sur "Copier dans un mail" sans que l'user ait à comprendre la
            // différence. Cf. brief §2.3 ("Bascule auto").
            _statusLabel.Foreground = new SolidColorBrush(Color.FromRgb(180, 0, 0));
            string detail = string.IsNullOrEmpty(result.ErrorDetail)
                ? result.DisplayMessage
                : result.DisplayMessage + " — " + Truncate(result.ErrorDetail, 200);
            _statusLabel.Text = Strings.FeedbackStatusSendFailed(detail);
            await Task.Delay(1500);
            OnCopyMailClicked();
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        private void OnCopyMailClicked()
        {
            if (!ValidateBeforeSend()) return;
            SyncReportFromUI();

            // Texte lisible pour coller dans un mail (markdown léger)
            string body = Strings.FeedbackMailBody(
                _report.Version, _report.Timestamp, _report.WordVersion, _report.OsVersion,
                _report.NerText, _report.RecognizedFormula, _report.CommittedLatex,
                _report.UserMessage, _report.ParagraphContext);

            try { Clipboard.SetText(body); } catch { /* certains contextes refusent */ }

            // mailto: avec subject prérempli, body laissé vide (limite ~2 KB
            // sur les clients mail, on ne risque pas de tronquer notre payload)
            string subject = Uri.EscapeDataString(Strings.FeedbackMailtoSubject(_report.Version));
            string mailto = $"mailto:{ContactEmail}?subject={subject}";
            try
            {
                Process.Start(new ProcessStartInfo(mailto) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _statusLabel.Foreground = new SolidColorBrush(Color.FromRgb(180, 0, 0));
                _statusLabel.Text = Strings.FeedbackStatusMailFailed(ex.Message);
                _sendButton.IsEnabled = true;
                _copyMailLink.IsEnabled = true;
                return;
            }

            _statusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0, 140, 0));
            _statusLabel.Text = Strings.FeedbackStatusMailCopied;
            // Pas de auto-close ici : on laisse l'user fermer quand il a fini
            // de paster dans son mail. Au prochain Annuler, fermeture.
        }

        // ── helpers UI ────────────────────────────────────────────────

        private static TextBlock SectionHeader(string text) => new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
            Margin = new Thickness(0, 8, 0, 6),
        };

        private static TextBlock Label(string text) => new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
            Margin = new Thickness(0, 0, 0, 3),
        };

        private static TextBox ReadOnlyBox(string text) => new TextBox
        {
            Text = text ?? "",
            IsReadOnly = true,
            IsReadOnlyCaretVisible = false,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = false,
            FontSize = 12,
            FontFamily = new FontFamily("Consolas, Courier New"),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 0, 0, 8),
            Background = new SolidColorBrush(Color.FromRgb(248, 248, 248)),
            Foreground = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
        };
    }
}
