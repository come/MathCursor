using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MathCursor.UI
{
    /// <summary>
    /// Popup d'édition affichée quand le caret entre dans un OMath produit
    /// par MathCursor (bookmark mcEq_… présent + handle dans l'EquationStore).
    /// Propose une seule action utile : « Revenir à la saisie initiale », qui
    /// remplace l'OMath par le texte source brut. L'élève peut alors corriger
    /// et reconvertir.
    ///
    /// Si l'utilisateur veut éditer l'OMath caractère par caractère (édition
    /// math native Word), il ferme la popup (Esc / Annuler) et utilise les
    /// contrôles natifs Word — la popup est une option, pas une obligation.
    ///
    /// Cf. brief docs/dev/briefs/2026-04-27-edit-mode-revert-to-source.md.
    /// </summary>
    public sealed class EditModePopupWindow : Window
    {
        private const double DisplayOpacity = 0.95;
        private const int FadeMs = 150;

        /// <summary>Déclenché quand l'utilisateur clique « Revenir à la saisie
        /// initiale ». L'abonné (SuggestionService) fait le remplacement Word
        /// + cleanup du store.</summary>
        public event Action RevertRequested;

        public EditModePopupWindow()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false; // ne prend pas le focus, Word reste actif
            Topmost = true;
            Width = 260;
            SizeToContent = SizeToContent.Height;
            Background = Brushes.White;
            AllowsTransparency = true;
            Opacity = 0;

            var border = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                BorderThickness = new Thickness(1),
                Background = Brushes.White,
            };

            var stack = new StackPanel { Margin = new Thickness(12, 10, 12, 10) };

            stack.Children.Add(new TextBlock
            {
                Text = "Modifier cette formule ?",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                Margin = new Thickness(0, 0, 0, 8),
            });

            var revertBtn = new Button
            {
                Content = "Revenir à la saisie initiale",
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromRgb(80, 130, 200)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            revertBtn.Click += (_, __) => RevertRequested?.Invoke();
            stack.Children.Add(revertBtn);

            var cancelBtn = new Button
            {
                Content = "Annuler",
                Padding = new Thickness(10, 4, 10, 4),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            cancelBtn.Click += (_, __) => HidePopup();
            stack.Children.Add(cancelBtn);

            border.Child = stack;
            Content = border;

            SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            };
        }

        public void ShowAt(double screenX, double screenY)
        {
            Left = screenX;
            Top = screenY;
            if (!IsVisible) Show();
            BeginAnimation(OpacityProperty,
                new DoubleAnimation(DisplayOpacity, TimeSpan.FromMilliseconds(FadeMs)));
        }

        public void HidePopup()
        {
            if (!IsVisible) return;
            var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(FadeMs));
            anim.Completed += (_, __) =>
            {
                if (Opacity <= 0.01)
                {
                    Hide();
                    BeginAnimation(OpacityProperty, null);
                    Opacity = 0;
                }
            };
            BeginAnimation(OpacityProperty, anim);
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
