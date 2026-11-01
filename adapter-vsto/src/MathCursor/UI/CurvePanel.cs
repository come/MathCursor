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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MathCursor.Host;
using MathCursor.Host.Curves;

namespace MathCursor.UI
{
    /// <summary>
    /// Panneau de réglages d'un dessin de courbes, posé à côté de l'image
    /// sélectionnée. Onglet Fonctions : une ligne par fonction ; en tapant, ses
    /// lectures traçables s'ouvrent dessous comme la popup MathCursor (↑/↓ pour
    /// en changer, effet immédiat sur le dessin ; Entrée, Tab ou clic valident et
    /// referment la liste, ↑/↓ la rouvrent). Onglet
    /// Réglages : repère, style, grille. Pas de bouton Insérer : chaque pause de
    /// frappe émet <see cref="Edited"/>, le contrôleur met l'image à jour.
    /// Vit sur SON thread WPF (saisie fiable hors de la boucle de messages de
    /// Word, hors du hook clavier) : ni moteur ni COM ici, tout passe par les
    /// événements. Échap ou ✕ = retour au document.
    /// ADR 2026-10-31-UX-curve-floating-panel.
    /// </summary>
    internal sealed class CurvePanel : Window
    {
        public const double PanelWidth = 300;

        /// <summary>État d'une ligne vu par le panneau : lectures reçues du contrôleur et
        /// lecture choisie (<see cref="Pick"/> = son LaTeX, null = celle de tête).</summary>
        private sealed class Row
        {
            public Grid Root;
            public Ellipse Dot;
            public TextBox Box;
            public ContentControl Reading;
            public string[] Readings = new string[0];
            public int Chosen;
            public string Error;
            public string Pick;
            public bool ListOpen;   // liste des lectures ouverte (comme la popup)
        }

        private static readonly Brush Accent = Frozen(Color.FromRgb(36, 78, 168));
        private static readonly Brush ErrorBrush = Frozen(Color.FromRgb(176, 0, 32));
        private static readonly Brush ChosenBack = Frozen(Color.FromRgb(232, 240, 252));
        private static readonly Brush ListBorder = Frozen(Color.FromRgb(210, 216, 226));
        private static readonly Brush Muted = Frozen(Color.FromRgb(110, 116, 126));

        private readonly CurveSpec _spec;
        private readonly StackPanel _rows = new StackPanel();
        private readonly List<Row> _inputs = new List<Row>();
        private readonly TextBox _xMin, _xMax, _yMin, _yMax;
        private readonly CheckBox _grid;
        private readonly ComboBox _style;
        private readonly DispatcherTimer _debounce;
        private int _heightPx;
        private bool _silent;   // champs remplis par le contrôleur (zoom molette)

        /// <summary>Spec modifiée (copie), émise sur le thread du panneau après une pause.</summary>
        public event Action<CurveSpec> Edited;
        /// <summary>Échap ou ✕ : l'élève revient au document.</summary>
        public event Action Dismissed;

        public string CurveId { get; }

        /// <summary>Hauteur courante en DIP, lisible depuis le thread de Word.</summary>
        public double HeightDip => Volatile.Read(ref _heightPx);

        public CurvePanel(CurveSpec spec)
        {
            _spec = spec.Clone();
            CurveId = spec.Id;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            SizeToContent = SizeToContent.Height;
            Width = PanelWidth;
            Background = Brushes.White;
            BorderBrush = Frozen(Color.FromRgb(190, 196, 206));
            BorderThickness = new Thickness(1);
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 12;
            UseLayoutRounding = true;

            _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _debounce.Tick += (_, __) => { _debounce.Stop(); Emit(); };

            // onglets tout en haut ; le ✕ se pose dans leur rangée, à droite
            var root = new Grid { Margin = new Thickness(6, 6, 6, 8) };
            var close = new Button
            {
                Content = "✕", Width = 24, Height = 22, ToolTip = Strings.CurveCloseTip,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            };
            close.Click += (_, __) => Dismissed?.Invoke();

            // ── Onglet Fonctions ──
            var functions = new StackPanel { Margin = new Thickness(2, 8, 2, 2) };
            functions.Children.Add(new TextBlock { Text = Strings.CurveFunctionsHeader, Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            functions.Children.Add(_rows);
            var add = new Button { Content = Strings.CurveAddButton, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 4, 0, 0) };
            add.Click += (_, __) => AddRow("", null, focus: true);
            functions.Children.Add(add);

            // ── Onglet Réglages ──
            var settings = new StackPanel { Margin = new Thickness(2, 8, 2, 2) };
            settings.Children.Add(Header(Strings.CurveAxesHeader));
            _xMin = NumBox(_spec.XMin); _xMax = NumBox(_spec.XMax);
            _yMin = NumBox(_spec.YMin); _yMax = NumBox(_spec.YMax);
            settings.Children.Add(RangeRow("x", _xMin, _xMax));
            settings.Children.Add(RangeRow("y", _yMin, _yMax));
            var auto = new Button { Content = Strings.CurveAxesAuto, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(16, 2, 0, 4) };
            auto.Click += (_, __) =>
            {
                _silent = true;
                try { _xMin.Text = _xMax.Text = _yMin.Text = _yMax.Text = ""; }
                finally { _silent = false; }
                Now();
            };
            settings.Children.Add(auto);
            settings.Children.Add(new TextBlock { Text = Strings.CurveWheelHint, Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });

            settings.Children.Add(Header(Strings.CurveStyleHeader));
            var options = new StackPanel { Orientation = Orientation.Horizontal };
            _style = new ComboBox { Width = 120, Margin = new Thickness(0, 0, 14, 0) };
            _style.Items.Add(Strings.CurveStyleClean);
            _style.Items.Add(Strings.CurveStyleHand);
            _style.SelectedIndex = _spec.Style == CurveStyle.Hand ? 1 : 0;
            _style.SelectionChanged += (_, __) => { SyncGrid(); Now(); };
            _grid = new CheckBox { Content = Strings.CurveGrid, IsChecked = _spec.Grid, VerticalAlignment = VerticalAlignment.Center };
            _grid.Checked += (_, __) => Now();
            _grid.Unchecked += (_, __) => Now();
            options.Children.Add(_style);
            options.Children.Add(_grid);
            settings.Children.Add(options);
            SyncGrid();

            var tabs = new TabControl { Padding = new Thickness(4) };
            tabs.Items.Add(new TabItem { Header = Strings.CurveTabFunctions, Content = functions });
            tabs.Items.Add(new TabItem { Header = Strings.CurveTabSettings, Content = settings });
            root.Children.Add(tabs);
            root.Children.Add(close);                      // après : au-dessus de la rangée d'onglets
            Content = root;

            for (int i = 0; i < _spec.Functions.Count; i++) AddRow(_spec.Functions[i], _spec.PickOf(i), focus: false);
            if (_inputs.Count == 0) AddRow("", null, focus: false);

            // KeyDown (et non Preview) : Échap ferme d'abord la liste des lectures d'une ligne
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) { e.Handled = true; Dismissed?.Invoke(); }
            };
            SizeChanged += (_, __) => Volatile.Write(ref _heightPx, (int)System.Math.Ceiling(ActualHeight));
            // le hook clavier est sur le thread de Word ; ceinture : on le coupe tant
            // que le panneau a le focus
            Activated += (_, __) => KeyboardInterceptor.Suspended = true;
            Deactivated += (_, __) => KeyboardInterceptor.Suspended = false;
            Closed += (_, __) => KeyboardInterceptor.Suspended = false;
            Loaded += (_, __) => Emit();   // lectures initiales
        }

        /// <summary>Donne le focus au panneau : 1re ligne vide, sinon la 1re.</summary>
        public void FocusFirstField()
        {
            Activate();
            var first = (_inputs.FirstOrDefault(i => string.IsNullOrEmpty(i.Box.Text)) ?? _inputs[0]).Box;
            first.Focus();
            first.CaretIndex = first.Text.Length;
        }

        /// <summary>Lectures calculées par le contrôleur, une entrée par ligne : lectures
        /// traçables (LaTeX, ordre de la popup) et celle retenue, ou raison de l'échec.</summary>
        public void ShowReadings(IReadOnlyList<(string[] Readings, int Chosen, string Error)> lines)
        {
            for (int i = 0; i < _inputs.Count && i < lines.Count; i++)
            {
                var row = _inputs[i];
                row.Readings = lines[i].Readings ?? new string[0];
                row.Chosen = lines[i].Chosen;
                row.Error = lines[i].Error;
                RenderReading(row);
            }
        }

        /// <summary>Repère posé par le contrôleur (zoom à la molette) : champs mis à jour
        /// sans renvoyer de modification.</summary>
        public void SetAxes(double x0, double x1, double y0, double y1)
        {
            _silent = true;
            try
            {
                _xMin.Text = CurveRenderer.Fmt(x0); _xMax.Text = CurveRenderer.Fmt(x1);
                _yMin.Text = CurveRenderer.Fmt(y0); _yMax.Text = CurveRenderer.Fmt(y1);
            }
            finally { _silent = false; }
        }

        // ── Lignes de fonctions ────────────────────────────────────────

        private void AddRow(string text, string pick, bool focus)
        {
            var row = new Row { Pick = pick };
            row.Root = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Root.RowDefinitions.Add(new RowDefinition());
            row.Root.RowDefinitions.Add(new RowDefinition());

            row.Dot = new Ellipse { Width = 10, Height = 10, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            row.Box = new TextBox { Text = text, FontFamily = new FontFamily("Consolas"), FontSize = 13, Padding = new Thickness(3, 2, 3, 2) };
            var remove = new Button { Content = "✕", Width = 22, Margin = new Thickness(4, 0, 0, 0), ToolTip = Strings.CurveRemoveTip, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
            row.Reading = new ContentControl { Margin = new Thickness(16, 2, 0, 0), MinHeight = 20 };
            Grid.SetColumn(row.Box, 1); Grid.SetColumn(remove, 2);
            Grid.SetRow(row.Reading, 1); Grid.SetColumnSpan(row.Reading, 3);
            row.Root.Children.Add(row.Dot); row.Root.Children.Add(row.Box); row.Root.Children.Add(remove); row.Root.Children.Add(row.Reading);

            row.Box.TextChanged += (_, __) => { row.Pick = null; row.ListOpen = true; Changed(); };
            row.Box.LostKeyboardFocus += (_, __) => { row.ListOpen = false; RenderReading(row); };
            row.Box.PreviewKeyDown += (_, e) =>
            {
                bool list = row.ListOpen && row.Readings.Length > 1;
                if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { AddRow("", null, focus: true); e.Handled = true; }
                else if (e.Key == Key.Enter || (e.Key == Key.Tab && list)) { Validate(row); e.Handled = true; }
                else if (e.Key == Key.Escape && list) { Validate(row); e.Handled = true; }
                else if (e.Key == Key.Down && row.Readings.Length > 1) { Choose(row, list ? row.Chosen + 1 : row.Chosen); e.Handled = true; }
                else if (e.Key == Key.Up && row.Readings.Length > 1) { Choose(row, list ? row.Chosen - 1 : row.Chosen); e.Handled = true; }
            };
            remove.Click += (_, __) =>
            {
                if (_inputs.Count == 1) { row.Box.Text = ""; return; }
                _rows.Children.Remove(row.Root);
                _inputs.Remove(row);
                Recolor();
                Now();                                     // retrait : effet immédiat
            };
            _rows.Children.Add(row.Root);
            _inputs.Add(row);
            Recolor();
            if (focus) row.Box.Focus();
        }

        private void Choose(Row row, int k)
        {
            int n = row.Readings.Length;
            row.Chosen = ((k % n) + n) % n;                 // ↓ sur la dernière revient en tête
            row.Pick = row.Readings[row.Chosen];
            row.ListOpen = true;
            RenderReading(row);
            Now();
        }

        // Entrée, Tab, Échap ou clic : la lecture en surbrillance est retenue, la liste se ferme.
        private void Validate(Row row)
        {
            if (row.Readings.Length > 1) row.Pick = row.Readings[Clamp(row, row.Chosen)];
            row.ListOpen = false;
            RenderReading(row);
            Now();
        }

        // Ligne qui a le focus et plusieurs lectures : la liste façon popup, la lecture
        // retenue surlignée. Sinon : la lecture retenue seule, ou l'échec en rouge.
        private void RenderReading(Row row)
        {
            if (string.IsNullOrWhiteSpace(row.Box.Text)) { row.Reading.Content = null; return; }
            if (row.Error != null)
            {
                row.Reading.Content = new TextBlock { Text = Strings.CurveNotPlottable + row.Error, Foreground = ErrorBrush, FontSize = 11, TextWrapping = TextWrapping.Wrap };
                return;
            }
            if (row.Readings.Length == 0) { row.Reading.Content = null; return; }
            if (row.Readings.Length == 1 || !row.ListOpen || !row.Box.IsKeyboardFocused)
            {
                row.Reading.Content = Formula(row.Readings[Clamp(row, row.Chosen)]);
                return;
            }
            var list = new StackPanel();
            for (int k = 0; k < row.Readings.Length; k++)
            {
                int index = k;
                var item = new Border
                {
                    Child = Formula(row.Readings[k]),
                    Padding = new Thickness(6, 2, 6, 2),
                    Background = k == row.Chosen ? ChosenBack : Brushes.Transparent,
                    Cursor = Cursors.Hand,
                };
                item.MouseLeftButtonDown += (_, e) => { e.Handled = true; row.Chosen = index; Validate(row); row.Box.Focus(); };
                list.Children.Add(item);
            }
            list.Children.Add(new TextBlock { Text = Strings.CurveReadingsHint, Foreground = Muted, FontSize = 10, Margin = new Thickness(6, 2, 6, 2) });
            row.Reading.Content = new Border { Child = list, BorderBrush = ListBorder, BorderThickness = new Thickness(1), Margin = new Thickness(0, 1, 0, 2) };
        }

        private static int Clamp(Row row, int k) => System.Math.Max(0, System.Math.Min(row.Readings.Length - 1, k));

        private static UIElement Formula(string latex)
        {
            try { return MixedLatexRenderer.Render("y = " + latex, 14); }
            catch { return new TextBlock { Text = latex }; }
        }

        // Pastilles dans l'ordre de la palette (= couleurs du dessin).
        private void Recolor()
        {
            for (int i = 0; i < _inputs.Count; i++)
                _inputs[i].Dot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(CurveSpec.ColorOf(i)));
        }

        private void SyncGrid() =>
            _grid.Visibility = _style.SelectedIndex == 1 ? Visibility.Collapsed : Visibility.Visible;

        // ── Émission ───────────────────────────────────────────────────

        private void Changed() { if (_silent) return; _debounce.Stop(); _debounce.Start(); }

        private void Now() { _debounce.Stop(); Emit(); }

        private void Emit()
        {
            _spec.Functions = _inputs.Select(i => i.Box.Text.Trim()).ToList();
            _spec.Picks = _inputs.Select(i => i.Pick).ToList();
            _spec.XMin = Num(_xMin); _spec.XMax = Num(_xMax);
            _spec.YMin = Num(_yMin); _spec.YMax = Num(_yMax);
            _spec.Grid = _grid.IsChecked == true;
            _spec.Style = _style.SelectedIndex == 1 ? CurveStyle.Hand : CurveStyle.Clean;
            Edited?.Invoke(_spec.Clone());
        }

        // ── Champs du repère ───────────────────────────────────────────

        private TextBox NumBox(double? v)
        {
            var b = new TextBox { Width = 64, Text = v.HasValue ? CurveRenderer.Fmt(v.Value) : "", Padding = new Thickness(3, 1, 3, 1), HorizontalContentAlignment = HorizontalAlignment.Right };
            b.TextChanged += (_, __) => Changed();
            b.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { Now(); e.Handled = true; } };
            return b;
        }

        private static UIElement RangeRow(string axis, TextBox from, TextBox to)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            p.Children.Add(new TextBlock { Text = axis, FontStyle = FontStyles.Italic, FontFamily = new FontFamily("Cambria Math"), FontSize = 14, Width = 16, VerticalAlignment = VerticalAlignment.Center });
            p.Children.Add(new TextBlock { Text = Strings.CurveFrom, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            p.Children.Add(from);
            p.Children.Add(new TextBlock { Text = Strings.CurveTo, Margin = new Thickness(8, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            p.Children.Add(to);
            return p;
        }

        // « −2,5 », « -2.5 », « 1e3 » ; vide ou illisible = automatique
        internal static double? Num(TextBox b)
        {
            var s = (b.Text ?? "").Trim().Replace('−', '-').Replace(',', '.').Replace(" ", "");
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : (double?)null;
        }

        private static TextBlock Header(string text) => new TextBlock
        {
            Text = text, FontWeight = FontWeights.SemiBold, Foreground = Accent,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4),
        };

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }
}
