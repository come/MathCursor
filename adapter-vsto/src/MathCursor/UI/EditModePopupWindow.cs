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
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MathCursor.UI
{
    /// <summary>
    /// Popup affichée sous un OMath produit par MathCursor (map hash→source).
    /// Deux actions (clic souris) empilées : « Encadrer cette formule »
    /// (post-hoc <c>\boxed</c>, ADR 2026-06-22) au-dessus de « Revenir à la
    /// saisie initiale ».
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
        public event Action BoxRequested;

        private readonly Border _boxRow;

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

            // Lignes empilées : encadrer (haut) puis revenir à la saisie (bas).
            _boxRow = MakeActionRow(Strings.EditBoxFormulaLabel, () => BoxRequested?.Invoke());
            var revertRow = MakeActionRow(Strings.EditRevertLabel, () => RevertRequested?.Invoke());

            var stack = new StackPanel { Orientation = Orientation.Vertical };
            stack.Children.Add(_boxRow);
            stack.Children.Add(revertRow);

            border.Child = stack;
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

        /// <summary>Construit une ligne d'action cliquable (style commun aux
        /// deux entrées : hover bleu pâle, curseur main).</summary>
        private static Border MakeActionRow(string text, Action onClick)
        {
            var row = new Border
            {
                Padding = new Thickness(10, 8, 10, 8),
                Background = Brushes.White,
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            row.Child = new TextBlock
            {
                Text = text,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                VerticalAlignment = VerticalAlignment.Center,
            };
            row.MouseEnter += (_, __) =>
                row.Background = new SolidColorBrush(Color.FromRgb(220, 235, 255));
            row.MouseLeave += (_, __) =>
                row.Background = Brushes.White;
            row.MouseLeftButtonUp += (_, __) => onClick?.Invoke();
            return row;
        }

        /// <summary>
        /// Affiche à la position (X, Y) en DIP. <paramref name="alignRight"/>
        /// signifie : X est la coord du bord DROIT de la popup (alignée avec
        /// la droite de la boîte OMath par exemple), pas le bord gauche.
        /// <paramref name="canBox"/> : la formule n'est pas déjà encadrée → on
        /// montre l'entrée d'encadrement (sinon masquée, pas de no-op silencieux).
        /// <paramref name="boxLabel"/> : libellé de cette entrée (« Encadrer cette
        /// formule » ou « Encadrer la dernière ligne » pour un bloc) ; null = garde
        /// le libellé courant.
        /// </summary>
        public void ShowAt(double x, double y, bool alignRight, bool canBox = true, string boxLabel = null)
        {
            _boxRow.Visibility = canBox ? Visibility.Visible : Visibility.Collapsed;
            if (boxLabel != null && _boxRow.Child is TextBlock boxText) boxText.Text = boxLabel;
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
