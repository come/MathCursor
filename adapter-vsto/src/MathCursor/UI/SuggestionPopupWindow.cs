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
    /// Popup WPF affichée sous le caret : liste VERTICALE des candidats LaTeX
    /// classés par le moteur forest (une ligne par candidat, top en premier).
    /// Réécriture Phase 2 beta-clean (cf. ADR 2026-06-10) — le modèle
    /// d'ambiguïté de l'ancien moteur (spots/splice/préférences) disparaît,
    /// remplacé par le classement de candidats.
    ///
    /// Acquis conservés :
    /// <list type="bullet">
    /// <item><c>WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW</c> : la popup ne vole
    ///   JAMAIS le focus de Word.</item>
    /// <item>Nav-mode opt-in : aucun surlignage avant la première flèche
    ///   (sinon l'élève croit qu'Enter va valider la ligne mise en avant).</item>
    /// <item>Max 2 candidats affichés, « + N autres » pour étendre.</item>
    /// <item>Fade in/out, opacité 0.5 affichage / 0.9 navigation.</item>
    /// </list>
    /// </summary>
    public sealed class SuggestionPopupWindow : Window
    {
        private const double DisplayOpacity = 0.5;
        private const double NavOpacity = 0.9;
        private const int FadeMs = 150;
        private const int MaxCandidatesCollapsed = 2;

        private readonly StackPanel _rows;
        private readonly TextBlock _debugFooter;

        private IReadOnlyList<string> _candidates = Array.Empty<string>();
        private int _selectedIndex;
        private bool _navMode;
        private bool _expanded;

        /// <summary>Levé quand l'utilisateur clique un candidat (le candidat
        /// cliqué devient la sélection). L'hôte fait le commit.</summary>
        public event Action CommitRequested;

        /// <summary>Levé par le lien « Signaler une erreur ».</summary>
        public event Action ReportRequested;

        public bool IsNavMode => _navMode;

        /// <summary>LaTeX qui sera commité : la sélection courante (= top
        /// par défaut si l'utilisateur n'a pas navigué).</summary>
        public string SelectedLatex =>
            _selectedIndex >= 0 && _selectedIndex < _candidates.Count ? _candidates[_selectedIndex] : null;

        public SuggestionPopupWindow()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            // PAS de Topmost : la popup est POSSÉDÉE par la fenêtre Word
            // (Owner, cf. SourceInitialized) → au-dessus de Word uniquement,
            // disparaît derrière les autres apps à l'Alt-Tab.
            Width = 320;
            SizeToContent = SizeToContent.Height;
            Background = Brushes.White;
            AllowsTransparency = true;
            Opacity = 0;

            _rows = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(4) };

            _debugFooter = new TextBlock
            {
                Text = "",
                FontSize = 10,
                FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)),
                Margin = new Thickness(8, 2, 8, 2),
                TextWrapping = TextWrapping.Wrap,
            };

            var reportLink = new TextBlock
            {
                Text = "Signaler une erreur",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(90, 110, 160)),
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(8, 2, 8, 4),
                TextDecorations = TextDecorations.Underline,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            reportLink.MouseLeftButtonUp += (_, __) => ReportRequested?.Invoke();

            var footer = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Margin = new Thickness(0, 4, 0, 0),
                Child = new StackPanel { Children = { _debugFooter, reportLink } },
            };

            Content = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                BorderThickness = new Thickness(1),
                Background = Brushes.White,
                Child = new StackPanel { Children = { _rows, footer } },
            };

            SourceInitialized += (_, _) =>
            {
                var helper = new WindowInteropHelper(this);
                int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
                SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
                // Owner = fenêtre principale Word (on tourne DANS Word) :
                // z-order lié à Word, pas au bureau entier.
                try { helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle; }
                catch { }
            };
        }

        /// <summary>
        /// Affiche la liste des candidats, ancrée en DIP : bord gauche à
        /// <paramref name="anchorX"/>, haut à <paramref name="anchorYBelow"/>
        /// (= juste sous la ligne de la zone). Si la popup déborde en bas
        /// d'écran, elle bascule AU-DESSUS de la ligne (son bas posé à
        /// <paramref name="anchorYAbove"/>). Clampée horizontalement à
        /// l'écran. <paramref name="sourceText"/> = texte source (footer).
        /// </summary>
        public void ShowCandidates(IReadOnlyList<string> candidates, double anchorX,
            double anchorYBelow, double anchorYAbove, string sourceText = "")
        {
            _candidates = candidates ?? Array.Empty<string>();
            _selectedIndex = 0;
            _navMode = false;
            _expanded = _candidates.Count <= MaxCandidatesCollapsed;
            _debugFooter.Text = string.IsNullOrEmpty(sourceText) ? "" : "« " + Truncate(sourceText, 60) + " »";

            BuildRows();
            UpdateHighlight();
            Left = anchorX;
            Top = anchorYBelow;
            if (!IsVisible) base.Show();

            // Clamp aux bords de l'écran APRÈS layout (SizeToContent → les
            // dimensions réelles ne sont connues qu'une fois le contenu mesuré).
            UpdateLayout();
            var wa = SystemParameters.WorkArea;
            double w = ActualWidth > 0 ? ActualWidth : Width;
            double h = ActualHeight > 0 ? ActualHeight : 120;
            if (anchorX + w > wa.Right - 4) Left = Math.Max(wa.Left + 4, wa.Right - 4 - w);
            if (anchorYBelow + h > wa.Bottom - 4 && !double.IsNaN(anchorYAbove))
                Top = Math.Max(wa.Top + 4, anchorYAbove - h); // bascule au-dessus de la ligne

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

        /// <summary>
        /// ↑/↓ dans la liste. Auto-étend « + N autres » quand on dépasse.
        /// Hors-bornes = sortie du nav mode + pass-through Word (le caret
        /// texte bouge normalement). Retourne true si la touche est consommée.
        /// </summary>
        public bool MoveSelection(int delta)
        {
            if (_candidates.Count == 0) return false;
            int next = _selectedIndex + delta;
            if (next < 0) { ExitNavMode(); return false; }

            int visibleMax = _expanded ? _candidates.Count - 1 : MaxCandidatesCollapsed - 1;
            if (next > visibleMax)
            {
                if (!_expanded)
                {
                    _expanded = true;
                    BuildRows();
                    visibleMax = _candidates.Count - 1;
                    if (next > visibleMax) next = visibleMax;
                }
                else { ExitNavMode(); return false; }
            }
            _selectedIndex = next;
            EnterNavMode();
            UpdateHighlight();
            return true;
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

        // ── Internals ────────────────────────────────────────────────────

        private void BuildRows()
        {
            _rows.Children.Clear();
            int total = _candidates.Count;
            int visibleCount = _expanded ? total : Math.Min(total, MaxCandidatesCollapsed);

            for (int i = 0; i < visibleCount; i++)
            {
                var cell = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(230, 230, 230)),
                    BorderThickness = new Thickness(0, 0, 0, i < visibleCount - 1 ? 1 : 0),
                    Padding = new Thickness(4, 3, 4, 3),
                    Background = Brushes.Transparent,
                };
                var container = new Grid { Margin = new Thickness(8, 4, 12, 4) };
                container.Children.Add(MixedLatexRenderer.Render(_candidates[i] ?? "", 18));
                cell.Child = container;

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
                _rows.Children.Add(cell);
            }

            if (!_expanded && total > MaxCandidatesCollapsed)
            {
                int hidden = total - MaxCandidatesCollapsed;
                var moreCell = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(230, 230, 230)),
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    Padding = new Thickness(6, 3, 6, 3),
                    Background = new SolidColorBrush(Color.FromRgb(245, 245, 250)),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Child = new TextBlock
                    {
                        Text = $"+ {hidden} autre" + (hidden > 1 ? "s" : ""),
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromRgb(80, 100, 180)),
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                moreCell.MouseLeftButtonUp += (_, __) =>
                {
                    _expanded = true;
                    BuildRows();
                    UpdateHighlight();
                };
                _rows.Children.Add(moreCell);
            }
        }

        private void UpdateHighlight()
        {
            for (int i = 0; i < _rows.Children.Count; i++)
            {
                if (_rows.Children[i] is Border cell && i < (_expanded ? _candidates.Count : Math.Min(_candidates.Count, MaxCandidatesCollapsed)))
                {
                    cell.Background = (_navMode && i == _selectedIndex)
                        ? new SolidColorBrush(Color.FromRgb(190, 215, 250))
                        : Brushes.Transparent;
                }
            }
        }

        private void ExitNavMode()
        {
            if (!_navMode) return;
            _navMode = false;
            UpdateHighlight();
            BeginAnimation(OpacityProperty,
                new DoubleAnimation(DisplayOpacity, TimeSpan.FromMilliseconds(FadeMs / 2)));
        }

        private static string Truncate(string s, int max)
            => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "…";

        // --- Win32 : WS_EX_NOACTIVATE / WS_EX_TOOLWINDOW ---
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
