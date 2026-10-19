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
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MathCursor.UI
{
    /// <summary>
    /// Palette de flèches d'un tableau de variation : choix EN LIGNE (↗ ↘ →)
    /// sous le caret. Dérivée de <see cref="SuggestionPopupWindow"/> : même
    /// look (fond blanc, opacité 0.5 repos / 0.9 navigation, surlignage bleu),
    /// mêmes réglages Win32 (<c>WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW</c>, jamais
    /// de vol de focus ; owner re-posé à chaque show), même nav-mode opt-in
    /// (aucun surlignage avant la première flèche ↓). Sans pied de popup.
    /// Cf. ADR 2026-10-19-UX-variation-arrow-palette.
    /// </summary>
    public sealed class ArrowPalettePopupWindow : Window
    {
        private const double DisplayOpacity = 0.5;
        private const double NavOpacity = 0.9;
        private const int FadeMs = 150;

        private static readonly Brush HighlightBrush = new SolidColorBrush(Color.FromRgb(190, 215, 250));

        private readonly IReadOnlyList<string> _choices;
        private readonly List<Border> _cells = new List<Border>();
        private int _selectedIndex;
        private bool _navMode;

        /// <summary>Levé au clic sur une flèche (qui devient la sélection).
        /// L'hôte écrit la flèche dans la case.</summary>
        public event Action CommitRequested;

        public bool IsNavMode => _navMode;

        public string SelectedChoice =>
            _selectedIndex >= 0 && _selectedIndex < _choices.Count ? _choices[_selectedIndex] : null;

        public ArrowPalettePopupWindow(IReadOnlyList<string> choices)
        {
            _choices = choices ?? Array.Empty<string>();

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            Background = Brushes.White;
            AllowsTransparency = true;
            Opacity = 0;

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
            for (int i = 0; i < _choices.Count; i++)
            {
                var cell = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(230, 230, 230)),
                    BorderThickness = new Thickness(0, 0, i < _choices.Count - 1 ? 1 : 0, 0),
                    Padding = new Thickness(12, 2, 12, 2),
                    Background = Brushes.Transparent,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Child = new TextBlock
                    {
                        Text = _choices[i],
                        FontFamily = new FontFamily("Cambria Math"),
                        FontSize = 22,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                };
                int idx = i;
                cell.MouseEnter += (_, __) =>
                {
                    _selectedIndex = idx;
                    EnterNavMode();
                    UpdateHighlight();
                };
                cell.MouseLeftButtonUp += (_, __) =>
                {
                    _selectedIndex = idx;
                    CommitRequested?.Invoke();
                };
                _cells.Add(cell);
                row.Children.Add(cell);
            }

            Content = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                BorderThickness = new Thickness(1),
                Background = Brushes.White,
                Child = row,
            };

            SourceInitialized += (_, _) =>
            {
                var helper = new WindowInteropHelper(this);
                int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
                SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            };
        }

        /// <summary>
        /// Affiche la palette, bord gauche à <paramref name="anchorX"/>, haut à
        /// <paramref name="anchorYBelow"/> (DIP). <paramref name="current"/> =
        /// flèche déjà dans la case : c'est elle qui sera surlignée à l'entrée
        /// en navigation.
        /// </summary>
        public void ShowAt(double anchorX, double anchorYBelow, string current)
        {
            _navMode = false;
            int idx = -1;
            for (int i = 0; i < _choices.Count; i++)
                if (_choices[i] == current) { idx = i; break; }
            _selectedIndex = idx >= 0 ? idx : 0;
            UpdateHighlight();

            Left = anchorX;
            Top = anchorYBelow;
            ReparentToActiveWordWindow();
            if (!IsVisible) base.Show();

            UpdateLayout();
            var wa = SystemParameters.WorkArea;
            double w = ActualWidth;
            if (anchorX + w > wa.Right - 4) Left = Math.Max(wa.Left + 4, wa.Right - 4 - w);

            BeginAnimation(OpacityProperty,
                new DoubleAnimation(DisplayOpacity, TimeSpan.FromMilliseconds(FadeMs)));
        }

        public void EnterNavMode()
        {
            if (_navMode) return;
            _navMode = true;
            UpdateHighlight();
            BeginAnimation(OpacityProperty,
                new DoubleAnimation(NavOpacity, TimeSpan.FromMilliseconds(FadeMs / 2)));
        }

        public void ExitNavMode()
        {
            if (!_navMode) return;
            _navMode = false;
            UpdateHighlight();
            BeginAnimation(OpacityProperty,
                new DoubleAnimation(DisplayOpacity, TimeSpan.FromMilliseconds(FadeMs / 2)));
        }

        /// <summary>← / → en navigation : butée aux deux bouts (on reste sur
        /// la première / la dernière flèche).</summary>
        public void MoveSelection(int delta)
        {
            if (_choices.Count == 0) return;
            _selectedIndex = Math.Max(0, Math.Min(_choices.Count - 1, _selectedIndex + delta));
            EnterNavMode();
            UpdateHighlight();
        }

        public void HidePopup()
        {
            if (!IsVisible) return;
            _navMode = false;
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

        private void UpdateHighlight()
        {
            for (int i = 0; i < _cells.Count; i++)
                _cells[i].Background = (_navMode && i == _selectedIndex) ? HighlightBrush : Brushes.Transparent;
        }

        /// <summary>Owner = fenêtre Word active, re-posé à chaque show (Word
        /// SDI). Cf. ADR 2026-09-04-Fix-popup-owner-multiwindow.</summary>
        private void ReparentToActiveWordWindow()
        {
            try
            {
                var helper = new WindowInteropHelper(this);
                IntPtr popup = helper.EnsureHandle();
                if (popup == IntPtr.Zero) return;
                if (Host.Caret.CaretScreenPositionReader.TryGetActiveWindowHandle(out IntPtr active)
                    && active != IntPtr.Zero)
                    SetOwnerHandle(popup, active);
            }
            catch { }
        }

        // --- Win32 : WS_EX_NOACTIVATE / WS_EX_TOOLWINDOW + owner (GWLP_HWNDPARENT) ---
        private const int GWL_EXSTYLE = -20;
        private const int GWLP_HWNDPARENT = -8;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLongPtr32(IntPtr hWnd, int nIndex, int dwNewLong);

        private static void SetOwnerHandle(IntPtr hWnd, IntPtr owner)
        {
            if (IntPtr.Size == 8) SetWindowLongPtr64(hWnd, GWLP_HWNDPARENT, owner);
            else SetWindowLongPtr32(hWnd, GWLP_HWNDPARENT, owner.ToInt32());
        }
    }
}
