using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MathCursor.CaretPopup
{
    // Popup borderless topmost positionnée au caret (coords écran MSAA → DIP WPF).
    // Spike : labels texte (le rendu LaTeX viendra du WpfMath de l'adapter Word).
    internal sealed class PopupWindow : Window
    {
        public int PickedIndex { get; private set; } = -1;

        private readonly ListBox _list;
        private readonly double _px, _py, _ph;
        private readonly IntPtr _restoreFg;

        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

        public PopupWindow(List<string> cands, double px, double py, double ph, IntPtr restoreFg)
        {
            _px = px; _py = py; _ph = ph; _restoreFg = restoreFg;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;

            var border = new Border
            {
                Background = Brush("#252526"),
                BorderBrush = Brush("#454545"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.5 }
            };

            _list = new ListBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Brushes.White,
                MinWidth = 160
            };
            foreach (var c in cands)
                _list.Items.Add(new TextBlock
                {
                    Text = c,
                    Padding = new Thickness(6, 2, 6, 2),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 14
                });
            _list.SelectedIndex = 0;

            border.Child = _list;
            Content = border;

            PreviewKeyDown += OnKey;
            _list.MouseDoubleClick += (s, e) => Pick(_list.SelectedIndex);
            Loaded += (s, e) => { Activate(); _list.Focus(); Keyboard.Focus(_list); };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // MSAA renvoie des pixels physiques ; WPF positionne en DIP.
            var src = PresentationSource.FromVisual(this);
            var px = new Point(_px, _py + _ph); // juste sous le caret
            if (src?.CompositionTarget != null)
                px = src.CompositionTarget.TransformFromDevice.Transform(px);
            Left = px.X;
            Top = px.Y;
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down:
                    _list.SelectedIndex = Math.Min(_list.Items.Count - 1, _list.SelectedIndex + 1);
                    e.Handled = true; break;
                case Key.Up:
                    _list.SelectedIndex = Math.Max(0, _list.SelectedIndex - 1);
                    e.Handled = true; break;
                case Key.Enter:
                case Key.Tab:
                    Pick(_list.SelectedIndex); e.Handled = true; break;
                case Key.Escape:
                    PickedIndex = -1; CloseAndRestore(); e.Handled = true; break;
            }
        }

        private void Pick(int i)
        {
            PickedIndex = i;
            CloseAndRestore();
        }

        private void CloseAndRestore()
        {
            if (_restoreFg != IntPtr.Zero) SetForegroundWindow(_restoreFg);
            Close();
        }

        private static SolidColorBrush Brush(string hex) =>
            (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
    }
}
