using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MathCursor.UI;

namespace MathCursor.Cheatsheet
{
    /// <summary>
    /// Pane Exemples (WPF UserControl construit en code-behind, conforme au
    /// pattern projet : aucun XAML, tout en C#). Affiche une galerie d'exemples
    /// concrets organisés par catégorie repliable + recherche temps réel ;
    /// chaque entrée = titre + N syntaxes équivalentes empilées + rendu math
    /// via WpfMath. Lecture seule (pas de click-to-insert), cf. ADR
    /// 2026-05-06-Feat-ribbon-pane-examples-pivot.
    /// <para>
    /// Hébergé dans un <c>System.Windows.Forms.Integration.ElementHost</c>
    /// côté <see cref="CheatsheetPaneHost"/>, lui-même dans un
    /// <c>CustomTaskPane</c> Word.
    /// </para>
    /// </summary>
    internal sealed class CheatsheetPane : UserControl
    {
        private readonly CheatsheetViewModel _vm;
        private readonly TextBox _searchBox;
        private readonly StackPanel _categoriesPanel;

        /// <summary>
        /// Callback déclenché par le bouton « Il manque quelque chose ? »
        /// (étape 2.F : ouvrira le FeedbackDialog avec préfill missing_shortcut).
        /// </summary>
        public Action OnMissingShortcutRequested { get; set; }

        public CheatsheetPane(CheatsheetViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));

            // ─── Couleurs (alignés sur le site mathcursor.app : ink/paper) ───
            var ink = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
            var paper = new SolidColorBrush(Color.FromRgb(0xFA, 0xF7, 0xF2));
            var paperWhite = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            var muted = new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B));

            Background = paper;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            // ─── Layout root ────────────────────────────────────────────────
            var root = new DockPanel();
            Content = root;

            // ─── Search bar (top) ───────────────────────────────────────────
            var searchBorder = new Border
            {
                Background = paperWhite,
                BorderBrush = ink,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(10, 8, 10, 8),
            };
            DockPanel.SetDock(searchBorder, Dock.Top);

            _searchBox = new TextBox
            {
                Background = paperWhite,
                BorderBrush = muted,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 6, 8, 6),
                FontSize = 13,
            };
            // Placeholder via Tag + adorner (pas dispo nativement WPF, mais
            // un simple TextBlock superposé suffit pour V1).
            _searchBox.TextChanged += (s, e) =>
            {
                _vm.SearchQuery = _searchBox.Text;
                Refresh();
            };
            searchBorder.Child = _searchBox;
            root.Children.Add(searchBorder);

            // ─── Footer (bottom) ────────────────────────────────────────────
            var footerBorder = new Border
            {
                Background = paperWhite,
                BorderBrush = ink,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(10, 8, 10, 8),
            };
            DockPanel.SetDock(footerBorder, Dock.Bottom);

            var missingBtn = new Button
            {
                Content = Strings.ExamplesMissingButton,
                Padding = new Thickness(10, 6, 10, 6),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Background = paper,
                BorderBrush = ink,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
            };
            missingBtn.Click += (s, e) => OnMissingShortcutRequested?.Invoke();
            footerBorder.Child = missingBtn;
            root.Children.Add(footerBorder);

            // ─── Catégories (center, scrollable) ────────────────────────────
            _categoriesPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(8),
            };
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _categoriesPanel,
            };
            root.Children.Add(scroll);

            // Premier rendu
            Refresh();
        }

        /// <summary>
        /// Reconstruit la liste des catégories visibles depuis le VM. Appelé
        /// à chaque changement de search ou de collapse state.
        /// </summary>
        private void Refresh()
        {
            _categoriesPanel.Children.Clear();
            var ink = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
            var muted = new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B));

            var visible = _vm.VisibleCategories;
            if (visible.Count == 0)
            {
                _categoriesPanel.Children.Add(new TextBlock
                {
                    Text = Strings.ExamplesNoMatch,
                    Foreground = muted,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(4, 12, 4, 4),
                    HorizontalAlignment = HorizontalAlignment.Center,
                });
                return;
            }

            foreach (var cat in visible)
            {
                var expander = BuildCategoryExpander(cat, ink);
                _categoriesPanel.Children.Add(expander);
            }
        }

        private Expander BuildCategoryExpander(CategoryView cat, Brush ink)
        {
            var header = new TextBlock
            {
                Text = cat.Label,
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Foreground = ink,
            };
            var entriesPanel = new StackPanel { Orientation = Orientation.Vertical };
            foreach (var entry in cat.VisibleEntries)
            {
                entriesPanel.Children.Add(BuildEntryBlock(entry, ink));
            }

            var expander = new Expander
            {
                Header = header,
                Content = entriesPanel,
                IsExpanded = cat.IsExpanded,
                Margin = new Thickness(0, 0, 0, 6),
                Tag = cat.Id,  // pour retrouver l'ID au callback
            };
            // Hook sur expand/collapse explicite par l'user → persiste dans VM
            // (mais SEULEMENT quand pas de search active : pendant search,
            // l'auto-expand override l'état saved et un toggle pendant search
            // serait visuellement perdu au clear search).
            expander.Expanded += (s, e) =>
            {
                if (string.IsNullOrEmpty(_vm.SearchQuery)) _vm.SetExpanded(cat.Id, true);
            };
            expander.Collapsed += (s, e) =>
            {
                if (string.IsNullOrEmpty(_vm.SearchQuery)) _vm.SetExpanded(cat.Id, false);
            };
            return expander;
        }

        /// <summary>
        /// Construit le bloc visuel d'une entrée Exemple : titre + label
        /// "Tape :" + N stenos empilées + label "Rendu :" + WpfMath.
        /// Lecture seule.
        /// </summary>
        private UIElement BuildEntryBlock(CheatsheetEntry entry, Brush ink)
        {
            var muted = new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B));
            var border = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xDC, 0xCF)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(8, 8, 8, 10),
                Margin = new Thickness(0, 0, 0, 0),
            };

            var stack = new StackPanel { Orientation = Orientation.Vertical };
            border.Child = stack;

            // Titre
            stack.Children.Add(new TextBlock
            {
                Text = _vm.TitleFor(entry),
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = ink,
                Margin = new Thickness(0, 0, 0, 4),
            });

            // "Tape :" + stenos empilées
            stack.Children.Add(new TextBlock
            {
                Text = Strings.ExamplesEntryTypeLabel,
                FontSize = 11,
                Foreground = muted,
                Margin = new Thickness(0, 0, 0, 2),
            });
            var stenosPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(8, 0, 0, 6),
            };
            if (entry.Stenos != null)
            {
                foreach (var s in entry.Stenos)
                {
                    if (string.IsNullOrEmpty(s)) continue;
                    stenosPanel.Children.Add(new TextBlock
                    {
                        Text = s,
                        FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                        FontSize = 12,
                        Foreground = ink,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 1, 0, 1),
                    });
                }
            }
            stack.Children.Add(stenosPanel);

            // "Rendu :" + WpfMath (avec fallback texte si parse fail)
            stack.Children.Add(new TextBlock
            {
                Text = Strings.ExamplesEntryRenderLabel,
                FontSize = 11,
                Foreground = muted,
                Margin = new Thickness(0, 0, 0, 2),
            });
            var mathContainer = new ContentControl
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(8, 0, 0, 0),
            };
            // Cf. brief 2026-05-06-wpfmath-fallback-renderer : MixedLatexRenderer
            // gère mathbb/mapsto/iint via TextBlock Unicode, le reste via WpfMath.
            mathContainer.Content = MathCursor.UI.MixedLatexRenderer.Render(
                entry.RenderedLatex ?? "", 18);
            stack.Children.Add(mathContainer);

            return border;
        }
    }
}
