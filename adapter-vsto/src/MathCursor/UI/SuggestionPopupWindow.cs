using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
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
        private readonly ListBox _list;

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
            Left = screenX;
            Top = screenY;
            if (!IsVisible) Show();
        }

        public void MoveSelection(int delta)
        {
            if (_list.Items.Count == 0) return;
            var n = _list.Items.Count;
            _list.SelectedIndex = (_list.SelectedIndex + delta + n) % n;
        }

        public void HidePopup()
        {
            if (IsVisible) Hide();
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
