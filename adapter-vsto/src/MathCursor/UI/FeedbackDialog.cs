using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MathCursor.Host.Feedback;

namespace MathCursor.UI
{
    /// <summary>
    /// Dialog modal pour signaler une erreur sur la formule courante. Ouvert
    /// depuis le lien "Signaler une erreur" de <see cref="SuggestionPopupWindow"/>.
    ///
    /// Contenu :
    /// - Champs read-only pré-remplis : texte NER détecté, formule reconnue.
    /// - Textarea libre pour décrire le problème (focus initial).
    /// - Email optionnel.
    /// - Zone log tail (collapsée par défaut, dépliable via "Voir logs").
    /// - Boutons Annuler / Envoyer.
    ///
    /// Word perd le focus pendant que le dialog est ouvert (modal via ShowDialog).
    /// </summary>
    public sealed class FeedbackDialog : Window
    {
        private readonly FeedbackReport _report;
        private readonly IFeedbackSender _sender;

        private readonly TextBox _messageBox;
        private readonly TextBox _emailBox;
        private readonly Button _sendButton;
        private readonly TextBlock _statusLabel;

        public FeedbackDialog(FeedbackReport prefilledReport, IFeedbackSender sender)
        {
            _report = prefilledReport ?? throw new ArgumentNullException(nameof(prefilledReport));
            _sender = sender ?? throw new ArgumentNullException(nameof(sender));

            Title = "MathCursor — Signaler une erreur";
            Width = 460;
            Height = 560;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;

            var root = new StackPanel { Margin = new Thickness(16) };

            root.Children.Add(new TextBlock
            {
                Text = "Aide-nous à améliorer MathCursor",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4),
            });
            root.Children.Add(new TextBlock
            {
                Text = "Dis-nous ce qui t'a gêné sur cette suggestion.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(110, 110, 110)),
                Margin = new Thickness(0, 0, 0, 12),
            });

            root.Children.Add(Label("Texte détecté"));
            root.Children.Add(ReadOnlyBox(_report.NerText));

            root.Children.Add(Label("Formule reconnue"));
            root.Children.Add(ReadOnlyBox(
                string.IsNullOrWhiteSpace(_report.RecognizedFormula) ? "(aucune)" : _report.RecognizedFormula));

            root.Children.Add(Label("Ce qui ne va pas"));
            _messageBox = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 90,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontSize = 12,
                Padding = new Thickness(4),
                Margin = new Thickness(0, 0, 0, 10),
            };
            root.Children.Add(_messageBox);

            root.Children.Add(Label("Email (optionnel)"));
            _emailBox = new TextBox
            {
                FontSize = 12,
                Padding = new Thickness(4),
                Margin = new Thickness(0, 0, 0, 10),
            };
            root.Children.Add(_emailBox);

            var logExpander = new Expander
            {
                Header = "Logs techniques (joints automatiquement)",
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 10),
                Content = new TextBox
                {
                    Text = string.IsNullOrEmpty(_report.LogTail) ? "(logs vides)" : _report.LogTail,
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.NoWrap,
                    FontFamily = new FontFamily("Consolas, Courier New"),
                    FontSize = 10,
                    Height = 100,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Background = new SolidColorBrush(Color.FromRgb(248, 248, 248)),
                },
            };
            root.Children.Add(logExpander);

            _statusLabel = new TextBlock
            {
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            };
            root.Children.Add(_statusLabel);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0),
            };
            var cancelButton = new Button
            {
                Content = "Annuler",
                Padding = new Thickness(14, 4, 14, 4),
                Margin = new Thickness(0, 0, 8, 0),
                MinWidth = 80,
            };
            cancelButton.Click += (_, __) => Close();
            _sendButton = new Button
            {
                Content = "Envoyer",
                Padding = new Thickness(14, 4, 14, 4),
                MinWidth = 80,
                IsDefault = true,
            };
            _sendButton.Click += async (_, __) => await OnSendClickedAsync();
            buttons.Children.Add(cancelButton);
            buttons.Children.Add(_sendButton);
            root.Children.Add(buttons);

            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = root,
            };

            Loaded += (_, __) => _messageBox.Focus();
        }

        private async System.Threading.Tasks.Task OnSendClickedAsync()
        {
            _report.UserMessage = _messageBox.Text ?? "";
            _report.UserEmail = (_emailBox.Text ?? "").Trim();

            _sendButton.IsEnabled = false;
            _statusLabel.Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80));
            _statusLabel.Text = "Envoi en cours...";

            FeedbackResult result;
            try
            {
                result = await _sender.SendAsync(_report);
            }
            catch (Exception ex)
            {
                result = new FeedbackResult
                {
                    Success = false,
                    DisplayMessage = "Erreur inattendue pendant l'envoi.",
                    ErrorDetail = ex.Message,
                };
            }

            if (result.Success)
            {
                _statusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0, 140, 0));
                _statusLabel.Text = result.DisplayMessage;
                // Laisse le message visible 1.5s avant de fermer automatiquement.
                await System.Threading.Tasks.Task.Delay(1500);
                Close();
            }
            else
            {
                _statusLabel.Foreground = new SolidColorBrush(Color.FromRgb(180, 0, 0));
                _statusLabel.Text = result.DisplayMessage;
                _sendButton.IsEnabled = true;
            }
        }

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
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Padding = new Thickness(4),
            Margin = new Thickness(0, 0, 0, 10),
            Background = new SolidColorBrush(Color.FromRgb(248, 248, 248)),
        };
    }
}
