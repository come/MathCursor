using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MathCursor.Core.Symbols;
using WpfMath.Controls;

namespace MathCursor.UI
{
    /// <summary>
    /// Petite fenêtre WPF affichée sous le caret pour montrer les candidats
    /// de conversion. TopMost, sans focus (Word reste actif pour la frappe).
    /// Construite en code (pas de XAML) pour minimiser le set-up VSTO.
    /// </summary>
    public sealed class SuggestionPopupWindow : Window
    {
        // Brief ergo : popup discrète, fondu in/out 150ms.
        // Display mode (popup informative) = 0.5
        // Nav mode (utilisateur a pressé Down et navigue dans les choix) = 0.7
        private const double DisplayOpacity = 0.5;
        private const double NavOpacity = 0.9;
        private const int FadeMs = 150;

        private readonly ListBox _list;
        private readonly TextBlock _debugFooter;
        private readonly TextBlock _reportLink;
        private bool _navMode;

        public bool IsNavMode => _navMode;

        /// <summary>
        /// Déclenché quand l'utilisateur clique sur "Signaler une erreur".
        /// <see cref="SuggestionService"/> y abonne son handler pour construire
        /// le FeedbackReport avec le contexte et ouvrir le dialog.
        /// </summary>
        public event Action ReportRequested;

        public SuggestionPopupWindow()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false; // ne prend pas le focus
            Topmost = true;
            Width = 280;
            SizeToContent = SizeToContent.Height;
            Background = Brushes.White;
            AllowsTransparency = true; // requis pour Opacity < 1 sur Window borderless
            Opacity = 0; // démarrer invisible, on fade in à Show

            var border = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                BorderThickness = new Thickness(1),
                Background = Brushes.White,
            };

            _list = new ListBox
            {
                BorderThickness = new Thickness(0),
                Background = Brushes.White,
                Focusable = false, // ne prend pas le focus clavier
            };
            _list.ItemContainerStyle = BuildItemContainerStyle();

            // Footer debug : affiche le texte NER extrait, aide à vérifier qu'on
            // analyse bien la zone attendue. Petite taille, gris discret.
            _debugFooter = new TextBlock
            {
                Text = "",
                FontSize = 10,
                FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)),
                Margin = new Thickness(8, 2, 8, 4),
                Padding = new Thickness(0),
                TextWrapping = TextWrapping.Wrap,
            };

            var topSeparator = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Margin = new Thickness(0, 4, 0, 0),
                Child = _debugFooter,
            };

            // Lien "Signaler une erreur" — dernière ligne, petit, discret.
            // Click → événement ReportRequested que SuggestionService écoute pour
            // ouvrir le FeedbackDialog.
            _reportLink = new TextBlock
            {
                Text = "Signaler une erreur",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(90, 110, 160)),
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(8, 2, 8, 4),
                TextDecorations = TextDecorations.Underline,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            _reportLink.MouseLeftButtonUp += (_, __) => ReportRequested?.Invoke();

            var reportLinkBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = _reportLink,
            };

            var stack = new StackPanel();
            stack.Children.Add(_list);
            stack.Children.Add(topSeparator);
            stack.Children.Add(reportLinkBorder);
            border.Child = stack;
            Content = border;

            // Renforce le no-focus via WS_EX_NOACTIVATE en plus de ShowActivated=false
            SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            };
        }

        public int SelectedIndex => _list.SelectedIndex;

        /// <summary>
        /// Style des ListBoxItem : fond transparent par défaut, teinte bleue
        /// claire au hover et à la sélection. Override la sélection système pour
        /// rester cohérent avec la popup transparente.
        /// </summary>
        private static Style BuildItemContainerStyle()
        {
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));

            var hoverColor = new SolidColorBrush(Color.FromRgb(220, 235, 255));
            var selectColor = new SolidColorBrush(Color.FromRgb(190, 215, 250));

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, hoverColor));
            style.Triggers.Add(hoverTrigger);

            var selectedTrigger = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, selectColor));
            style.Triggers.Add(selectedTrigger);

            return style;
        }

        /// <summary>
        /// Rendu LaTeX → UIElement via WpfMath <see cref="FormulaControl"/>,
        /// après passage par <see cref="WpfMathAdapter"/> qui substitue les
        /// macros non couvertes (\mathbb, \widehat, \begin{cases}, etc.) par
        /// leurs équivalents Unicode/macros supportées.
        ///
        /// Si le parse WpfMath échoue (formule trop exotique ou inattendue),
        /// fallback sur un TextBlock unicode lisible — pas de superposition
        /// double rendu/texte (qui donnerait l'impression de LaTeX "barré").
        /// Cf. ADR 2026-04-24-Feat-popup-revert-wpfmath.
        /// </summary>
        private UIElement RenderMath(string latex, bool isPartial = false)
        {
            string adapted = WpfMathAdapter.Adapt(latex ?? "");
            // Mode partiel : colorier \ldots en rouge si WpfMath le supporte ;
            // sinon on laisse \ldots tel quel (reste lisible).
            if (isPartial)
                adapted = adapted.Replace("\\ldots", "\\color{red}{\\ldots}");

            var container = new Grid { Margin = new Thickness(8, 4, 12, 4) };
            if (string.IsNullOrWhiteSpace(adapted))
            {
                container.Children.Add(new TextBlock { Text = "", FontSize = 14 });
                return container;
            }

            // Parse explicite via TexFormulaParser : si le LaTeX a un problème
            // (commande inconnue, syntaxe non supportée), Parse jette une
            // exception ici — alors que FormulaControl.Formula = ... avale les
            // erreurs en silence et affiche du vide.
            string parseError = null;
            try
            {
                var parser = WpfMath.Parsers.WpfTeXFormulaParser.Instance;
                parser.Parse(adapted);
            }
            catch (Exception ex)
            {
                parseError = ex.GetType().Name + ": " + ex.Message;
            }

            try
            {
                var formula = new FormulaControl
                {
                    Formula = adapted,
                    Scale = 18,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                container.Children.Add(formula);

                // Diagnostic : on mesure le FormulaControl avec une largeur
                // infinie (comme un layout WPF "naturel") pour comparer à ce
                // que la popup obtient réellement après contraintes parent.
                string adaptedSnapshot = adapted;
                formula.SizeChanged += (_, args) =>
                {
                    formula.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    string errors = "";
                    try
                    {
                        if (formula.Errors != null && formula.Errors.Count > 0)
                            errors = " errors=[" + string.Join(" | ", System.Linq.Enumerable.Select(formula.Errors, e => e?.ToString() ?? "null")) + "]";
                    }
                    catch (Exception exErr) { errors = " errors_read_failed=" + exErr.Message; }
                    LogPopup($"render measured formula=\"{adaptedSnapshot}\" newW={args.NewSize.Width:F1} newH={args.NewSize.Height:F1} desiredW={formula.DesiredSize.Width:F1} desiredH={formula.DesiredSize.Height:F1}{errors}");
                };

                if (parseError != null)
                    LogPopup($"render PARSE_ERROR formula=\"{adapted}\" ex={parseError}");
                else
                    LogPopup($"render OK formula=\"{adapted}\"");
            }
            catch (Exception ex)
            {
                LogPopup($"render CTRL_FAILED formula=\"{adapted}\" ex={ex.Message}");
                container.Children.Add(new TextBlock
                {
                    Text = adapted,
                    FontSize = 14,
                    FontFamily = new FontFamily("Cambria Math, Cambria, Segoe UI Symbol, Segoe UI"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                });
            }
            return container;
        }

        public void ShowSuggestions(IReadOnlyList<SymbolChoice> choices, double screenX, double screenY, string debugText = "")
        {
            LogPopup($"ShowSuggestions count={choices.Count} pos=({screenX:F0},{screenY:F0}) debug=\"{debugText}\"");
            _debugFooter.Text = string.IsNullOrEmpty(debugText) ? "" : "NER: \"" + debugText + "\"";
            _list.Items.Clear();
            for (int i = 0; i < choices.Count; i++)
            {
                var c = choices[i];
                var panel = new StackPanel { Orientation = Orientation.Horizontal };
                panel.Children.Add(RenderMath(c.Display, c.IsPartial));
                panel.Children.Add(new TextBlock
                {
                    Text = c.Label,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
                    Margin = new Thickness(0, 4, 8, 4),
                    VerticalAlignment = VerticalAlignment.Center,
                });
                var item = new ListBoxItem
                {
                    Content = panel,
                    Padding = new Thickness(0),
                };
                int index = i;
                // Hover souris : entre en mode nav + sélectionne l'item survolé
                item.MouseEnter += (_, __) =>
                {
                    _list.SelectedIndex = index;
                    EnterNavMode();
                };
                _list.Items.Add(item);
            }
            _list.SelectedIndex = 0;
            _navMode = false; // toute mise à jour ramène en display mode
            Left = screenX;
            Top = screenY;
            if (!IsVisible) Show();

            // Fade vers DisplayOpacity. DoubleAnimation à 1 paramètre anime
            // depuis la valeur courante. Si on était en cours de fade-out,
            // ça interrompt et repart en sens inverse.
            BeginAnimation(OpacityProperty,
                new DoubleAnimation(DisplayOpacity, TimeSpan.FromMilliseconds(FadeMs)));
        }

        /// <summary>L'utilisateur a pressé Down — on entre en mode navigation
        /// (opacité augmentée, sélection mise en avant). Up/Down navigueront
        /// désormais dans les choix, Enter validera le sélectionné.</summary>
        public void EnterNavMode()
        {
            if (_navMode) return;
            _navMode = true;
            BeginAnimation(OpacityProperty,
                new DoubleAnimation(NavOpacity, TimeSpan.FromMilliseconds(FadeMs / 2)));
        }

        public void MoveSelection(int delta)
        {
            if (_list.Items.Count == 0) return;
            var n = _list.Items.Count;
            _list.SelectedIndex = (_list.SelectedIndex + delta + n) % n;
        }

        public void HidePopup()
        {
            if (!IsVisible) return;
            var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(FadeMs));
            anim.Completed += (_, __) =>
            {
                // Si l'opacité est revenue à ~0 (pas interrompue par un Show
                // entre temps), on cache vraiment la fenêtre.
                if (Opacity <= 0.01)
                {
                    Hide();
                    BeginAnimation(OpacityProperty, null);
                    Opacity = 0;
                }
            };
            BeginAnimation(OpacityProperty, anim);
        }

        // Log dédié popup, partagé avec mathcursor.log (préfixe "popup").
        // Utilisé pendant le debug du rendu — garde un témoin si plus tard on
        // a besoin de tracer un autre symptôme visuel.
        private static void LogPopup(string message)
        {
            try
            {
                var dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MathCursor", "logs");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "mathcursor.log"),
                    $"{DateTime.UtcNow:o} popup {message}{Environment.NewLine}");
            }
            catch { }
        }

        // --- Win32 pour WS_EX_NOACTIVATE / WS_EX_TOOLWINDOW ---
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
