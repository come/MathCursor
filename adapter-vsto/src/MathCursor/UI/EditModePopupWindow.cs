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
    /// Popup affichée sous un OMath produit par MathCursor (bookmark mcEq_… +
    /// handle dans l'EquationStore). Une seule action utile :
    /// « Revenir à la saisie initiale ».
    ///
    /// Click souris UNIQUEMENT (pas de nav clavier) — les flèches et Enter
    /// restent interceptées par Word pour naviguer dans l'OMath natif. Esc
    /// ferme la popup. Style cohérent avec <see cref="SuggestionPopupWindow"/>.
    /// </summary>
    public sealed class EditModePopupWindow : Window
    {
        private const double DisplayOpacity = 0.92;
        private const int FadeMs = 150;

        public event Action RevertRequested;

        public EditModePopupWindow()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            // PAS de Topmost : fenêtre possédée par Word (cf. SourceInitialized).
            Width = 220;
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

            var actionRow = new Border
            {
                Padding = new Thickness(10, 8, 10, 8),
                Background = Brushes.White,
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            actionRow.Child = new TextBlock
            {
                Text = "Revenir à la saisie initiale",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                VerticalAlignment = VerticalAlignment.Center,
            };
            actionRow.MouseEnter += (_, __) =>
                actionRow.Background = new SolidColorBrush(Color.FromRgb(220, 235, 255));
            actionRow.MouseLeave += (_, __) =>
                actionRow.Background = Brushes.White;
            actionRow.MouseLeftButtonUp += (_, __) => RevertRequested?.Invoke();

            border.Child = actionRow;
            Content = border;

            SourceInitialized += (_, _) =>
            {
                var helper = new WindowInteropHelper(this);
                int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
                SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
                // Owner = fenêtre principale Word : z-order lié à Word.
                try { helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle; }
                catch { }
            };
        }

        /// <summary>
        /// Affiche à la position (X, Y) en DIP. <paramref name="alignRight"/>
        /// signifie : X est la coord du bord DROIT de la popup (alignée avec
        /// la droite de la boîte OMath par exemple), pas le bord gauche.
        /// </summary>
        public void ShowAt(double x, double y, bool alignRight)
        {
            // SizeToContent rend ActualWidth disponible après Show ; on calcule
            // la position après show pour avoir la vraie largeur si on aligne
            // à droite.
            if (!IsVisible) Show();
            UpdateLayout();
            double left = alignRight ? x - ActualWidth : x;
            if (left < 0) left = 0;
            Left = left;
            Top = y;
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

        // --- Win32 ---
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
