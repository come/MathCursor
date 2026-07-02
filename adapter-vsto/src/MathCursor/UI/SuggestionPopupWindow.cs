// MathCursor — capture d'intention mathématique depuis une saisie clavier linéaire.
// Copyright (C) 2026  Côme Percin
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
        private const int MaxCandidatesCollapsed = 3;   // 2 → 3 (retour user 2026-06-12)

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

        /// <summary>Index de la sélection courante — permet au contrôleur de
        /// retrouver le candidat SANS préfixe d'affichage (blocs M2).</summary>
        public int SelectedIndex => _selectedIndex;

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
        /// (= TOUJOURS juste sous la ligne de la zone — jamais au-dessus,
        /// règle UX 2026-06-12 : la bascule au-dessus perturbait le flow).
        /// Clampée horizontalement à l'écran.
        /// <paramref name="anchorYAbove"/> est conservé pour compat mais
        /// IGNORÉ. <paramref name="sourceText"/> = texte source (footer).
        /// </summary>
        public void ShowCandidates(IReadOnlyList<string> candidates, double anchorX,
            double anchorYBelow, double anchorYAbove, string sourceText = "",
            string mergeKind = null, IReadOnlyList<string> mergePrevLines = null)
        {
            _mergeKind = mergeKind;
            _mergePrevLines = (mergePrevLines != null && mergePrevLines.Count > 0) ? mergePrevLines : null;
            // Rafraîchissement d'une popup DÉJÀ ouverte (frappe continue,
            // extension Ctrl+Espace…) : on PRÉSERVE le nav mode et la
            // sélection (index clampé). L'état DÉPLIÉ ne persiste qu'EN
            // NAVIGATION (règle UX 2026-06-12 : en frappe continue, chaque
            // rafraîchissement revient au repli 2 + « N de plus » — un dépli
            // passé ne doit pas saccager le visuel des popups suivantes).
            bool wasVisible = IsVisible;
            bool preserveNav = wasVisible && _navMode;
            int prevIndex = _selectedIndex;
            bool prevExpanded = preserveNav && _expanded;

            _candidates = candidates ?? Array.Empty<string>();
            _navMode = preserveNav;
            // _expanded = INTENTION UTILISATEUR uniquement (clic « + N autres »
            // ou flèche au-delà du repli), préservée en nav. L'ancien
            // « count <= Max ⇒ déplié » marquait déplié les petites listes
            // sans effet visuel… puis FUYAIT vers la liste suivante via le
            // hover-nav (mesuré 2026-06-12 : 5 candidats affichés). Le rendu
            // des petites listes n'en a pas besoin (visibleCount = min).
            _expanded = prevExpanded;
            _selectedIndex = preserveNav
                ? Math.Max(0, Math.Min(prevIndex, _candidates.Count - 1))
                : 0;
            if (_selectedIndex > MaxCandidatesCollapsed - 1) _expanded = true; // sélection hors zone repliée → déplie
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
            if (anchorX + w > wa.Right - 4) Left = Math.Max(wa.Left + 4, wa.Right - 4 - w);
            // JAMAIS de bascule au-dessus de la ligne (règle UX 2026-06-12) :
            // en bas d'écran la popup peut mordre la zone barre des tâches,
            // c'est préférable à un saut au-dessus qui casse le flow. Le
            // repli à 2 candidats la garde courte de toute façon.

            BeginAnimation(OpacityProperty,
                new DoubleAnimation(_navMode ? NavOpacity : DisplayOpacity, TimeSpan.FromMilliseconds(FadeMs)));
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
        /// Butée HAUTE : la première flèche ↑ sort du nav mode en CONSOMMANT
        /// la touche — le caret ne saute pas de ligne, la popup reste
        /// ouverte (retour user 2026-06-10) ; la flèche suivante, hors nav,
        /// passe à Word normalement. Butée BASSE : ↓ sur le dernier candidat
        /// RESTE dessus (retour user 2026-06-12 : ↓ en bas de liste sortait
        /// de la popup). Retourne true si consommée.
        /// </summary>
        public bool MoveSelection(int delta)
        {
            if (_candidates.Count == 0) return false;
            int next = _selectedIndex + delta;
            if (next < 0) { ExitNavMode(); return true; }

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
                else next = visibleMax; // butée basse : on RESTE sur le dernier
                                        // candidat (retour user 2026-06-12)
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

        /// <summary>Contexte de merge pour l'aperçu intégré (demande user
        /// 2026-06-10) : quand la ligne committée FUSIONNERA avec le bloc du
        /// dessus, chaque candidat est rendu DANS l'aperçu du bloc — lignes
        /// précédentes grisées (la dernière réelle, « ⋯ » au-delà de 2
        /// lignes), candidat en dessous, accolade à gauche pour les
        /// systèmes. Null = rendu candidat simple.</summary>
        private string _mergeKind;               // "chain" | "system" | null
        private IReadOnlyList<string> _mergePrevLines;

        /// <summary>Rendu d'une cellule candidat : simple, ou intégré dans
        /// l'aperçu de bloc si contexte de merge.</summary>
        private UIElement BuildCandidateContent(string candidateLatex)
        {
            var candidateEl = MixedLatexRenderer.Render(candidateLatex ?? "", 18);
            if (_mergePrevLines == null) return candidateEl;

            var lines = new StackPanel { Orientation = Orientation.Vertical };
            // La VRAIE ligne du dessus (dernière du bloc), grisée ; s'il y en
            // a d'autres encore au-dessus, « ⋯ » en tête (retour user).
            if (_mergePrevLines.Count > 1)
                lines.Children.Add(Dim(new TextBlock
                {
                    Text = "⋯",
                    FontSize = 12,
                    Margin = new Thickness(12, 0, 0, 0),
                    Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
                }));
            lines.Children.Add(Dim(MixedLatexRenderer.Render(
                _mergePrevLines[_mergePrevLines.Count - 1], 13)));
            if (candidateEl is FrameworkElement feCand) feCand.Margin = new Thickness(0, 1, 0, 0);
            lines.Children.Add(candidateEl);

            if (_mergeKind != "system") return lines;

            // Système : accolade ouvrante à gauche, étirée sur les lignes.
            int rowCount = _mergePrevLines.Count > 1 ? 3 : 2;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var brace = new TextBlock
            {
                Text = "{",
                FontSize = 12 + 11 * rowCount,
                FontWeight = FontWeights.Light,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
            };
            Grid.SetColumn(brace, 0);
            Grid.SetColumn(lines, 1);
            lines.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(brace);
            grid.Children.Add(lines);
            return grid;
        }

        private static UIElement Dim(UIElement el)
        {
            el.Opacity = 0.5;
            return el;
        }

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
                container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                container.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                container.Children.Add(BuildCandidateContent(_candidates[i]));
                // Badge discret quand le RENDU d'aperçu ne distingue pas le
                // candidat (matrice ligne aplatie ≈ tuple) — cf. CandidateHints.
                string hint = CandidateHints.GetHint(_candidates[i]);
                if (hint != null)
                {
                    var badge = new TextBlock
                    {
                        Text = hint,
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(10, 0, 0, 0),
                    };
                    Grid.SetColumn(badge, 1);
                    container.Children.Add(badge);
                }
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
