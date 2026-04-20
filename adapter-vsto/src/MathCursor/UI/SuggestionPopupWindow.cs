using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MathCursor.Core.Symbols;

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
        private const double NavOpacity = 0.7;
        private const int FadeMs = 150;

        private readonly ListBox _list;
        private bool _navMode;

        public bool IsNavMode => _navMode;

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

            border.Child = _list;
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

        public void ShowSuggestions(IReadOnlyList<SymbolChoice> choices, double screenX, double screenY)
        {
            _list.Items.Clear();
            foreach (var c in choices)
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal };
                panel.Children.Add(new TextBlock
                {
                    Text = c.Display,
                    FontSize = 16,
                    FontFamily = new FontFamily("Cambria Math, Cambria, Segoe UI"),
                    Margin = new Thickness(8, 4, 12, 4),
                    VerticalAlignment = VerticalAlignment.Center,
                });
                panel.Children.Add(new TextBlock
                {
                    Text = c.Label,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
                    Margin = new Thickness(0, 4, 8, 4),
                    VerticalAlignment = VerticalAlignment.Center,
                });
                _list.Items.Add(new ListBoxItem
                {
                    Content = panel,
                    Padding = new Thickness(0),
                });
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
