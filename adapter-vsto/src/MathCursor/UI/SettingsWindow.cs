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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MathCursor.Host.Settings;

namespace MathCursor.UI
{
    /// <summary>
    /// Fenêtre WPF modale « Paramètres » (ADR 2026-06-10-Feat-ribbon-columns-
    /// settings-culture). Modèle culture + overrides nullables :
    ///
    /// <list type="bullet">
    /// <item>Changer la CULTURE re-preset tous les champs (les overrides sont
    ///   effacés : on repart « tout suit la culture »).</item>
    /// <item>Modifier un CHAMP le détache de la culture (override) ; le
    ///   remettre sur la valeur du preset le rattache (override = null).</item>
    /// <item>Un hint à droite de chaque champ dit « suit la culture » ou
    ///   « personnalisé ».</item>
    /// </list>
    ///
    /// Enregistrer → <see cref="SettingsStore.Save"/> ; la culture moteur est
    /// relue à chaque conversion, donc l'effet est immédiat sans redémarrage.
    /// </summary>
    public sealed class SettingsWindow : Window
    {
        private readonly AppSettings _draft;

        private readonly ComboBox _cultureBox;
        private readonly ComboBox _intervalSepBox;
        private readonly ComboBox _matrixBox;
        private readonly CheckBox _autoDetectBox;
        private readonly CheckBox _tabValidateBox;
        private readonly CheckBox _usageStatsBox;
        private readonly TextBlock _intervalSepHint;
        private readonly TextBlock _matrixHint;
        private readonly TextBlock _preview;
        private bool _loading; // supprime les handlers pendant un re-preset

        public SettingsWindow()
        {
            _draft = SettingsStore.Current.Clone();

            Title = Strings.SettingsWindowTitle;
            Width = 560;
            Height = 680;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;

            var root = new StackPanel { Margin = new Thickness(20) };

            root.Children.Add(new TextBlock
            {
                Text = Strings.SettingsIntro,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(110, 110, 110)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14),
            });

            // ── Culture ──────────────────────────────────────────────────
            root.Children.Add(SectionHeader(Strings.SettingsSectionCulture));
            _cultureBox = new ComboBox { FontSize = 12, Margin = new Thickness(0, 2, 0, 14) };
            _cultureBox.Items.Add(Strings.SettingsCultureFr);
            _cultureBox.Items.Add(Strings.SettingsCultureUs);
            _cultureBox.SelectionChanged += (_, __) => OnCultureChanged();
            root.Children.Add(_cultureBox);

            // ── Notation ─────────────────────────────────────────────────
            root.Children.Add(SectionHeader(Strings.SettingsSectionNotation));

            _intervalSepBox = new ComboBox { FontSize = 12, Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
            _intervalSepBox.Items.Add("[0 ; 1]");
            _intervalSepBox.Items.Add("[0 , 1]");
            _intervalSepBox.SelectionChanged += (_, __) => OnFieldChanged();
            _intervalSepHint = HintBlock();
            root.Children.Add(FieldRow(Strings.SettingsIntervalSepLabel, _intervalSepBox, _intervalSepHint));

            _matrixBox = new ComboBox { FontSize = 12, Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
            _matrixBox.Items.Add(Strings.SettingsMatrixParens);
            _matrixBox.Items.Add(Strings.SettingsMatrixBrackets);
            _matrixBox.SelectionChanged += (_, __) => OnFieldChanged();
            _matrixHint = HintBlock();
            root.Children.Add(FieldRow(Strings.SettingsMatrixLabel, _matrixBox, _matrixHint));

            // ── Détection (ADR 2026-06-10-Feat-ner-auto-detection-debounce) ─
            root.Children.Add(SectionHeader(Strings.SettingsSectionDetection));
            _autoDetectBox = new CheckBox
            {
                Content = Strings.SettingsAutoDetectLabel,
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 14),
            };
            _autoDetectBox.Checked += (_, __) => { if (!_loading) _draft.AutoDetect = true; };
            _autoDetectBox.Unchecked += (_, __) => { if (!_loading) _draft.AutoDetect = false; };
            root.Children.Add(_autoDetectBox);

            _tabValidateBox = new CheckBox
            {
                Content = Strings.SettingsTabValidateLabel,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 14),
            };
            _tabValidateBox.Checked += (_, __) => { if (!_loading) _draft.TabValidate = true; };
            _tabValidateBox.Unchecked += (_, __) => { if (!_loading) _draft.TabValidate = false; };
            root.Children.Add(_tabValidateBox);

            // ── Confidentialité (ADR 2026-06-18-Feat-usage-counter-telemetry) ─
            root.Children.Add(SectionHeader(Strings.SettingsSectionPrivacy));
            _usageStatsBox = new CheckBox
            {
                Content = Strings.SettingsUsageStatsLabel,
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 2),
            };
            _usageStatsBox.Checked += (_, __) => { if (!_loading) _draft.SendUsageStats = true; };
            _usageStatsBox.Unchecked += (_, __) => { if (!_loading) _draft.SendUsageStats = false; };
            root.Children.Add(_usageStatsBox);
            root.Children.Add(new TextBlock
            {
                Text = Strings.SettingsUsageStatsHint,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(110, 110, 110)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(24, 0, 0, 14),
            });

            // ── Aperçu ───────────────────────────────────────────────────
            root.Children.Add(SectionHeader(Strings.SettingsPreviewLabel));
            _preview = new TextBlock
            {
                FontSize = 13,
                FontFamily = new FontFamily("Cambria Math, Cambria"),
                Padding = new Thickness(10),
                Background = new SolidColorBrush(Color.FromRgb(245, 245, 245)),
                Margin = new Thickness(0, 2, 0, 16),
            };
            root.Children.Add(_preview);

            // ── Boutons ──────────────────────────────────────────────────
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = Strings.SettingsButtonCancel, Width = 100, Height = 28, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
            var save = new Button { Content = Strings.SettingsButtonSave, Width = 120, Height = 28, IsDefault = true };
            save.Click += (_, __) => SaveAndClose();
            buttons.Children.Add(cancel);
            buttons.Children.Add(save);
            root.Children.Add(buttons);

            Content = root;
            LoadDraftIntoControls();
        }

        // ── Modèle ↔ contrôles ──────────────────────────────────────────

        private void LoadDraftIntoControls()
        {
            _loading = true;
            _cultureBox.SelectedIndex = _draft.Culture == AppSettings.CultureUs ? 1 : 0;
            var effective = _draft.ToEngineCulture();
            _intervalSepBox.SelectedIndex = effective.IntervalSep == "," ? 1 : 0;
            _matrixBox.SelectedIndex = effective.MatrixEnv == "bmatrix" ? 1 : 0;
            _autoDetectBox.IsChecked = _draft.AutoDetect;
            _tabValidateBox.IsChecked = _draft.TabValidate;
            _usageStatsBox.IsChecked = _draft.SendUsageStats;
            _loading = false;
            RefreshHintsAndPreview();
        }

        private void OnCultureChanged()
        {
            if (_loading) return;
            _draft.Culture = _cultureBox.SelectedIndex == 1 ? AppSettings.CultureUs : AppSettings.CultureFr;
            // re-preset : les champs repartent « tout suit la culture »
            _draft.IntervalSepOverride = null;
            _draft.MatrixEnvOverride = null;
            LoadDraftIntoControls();
        }

        private void OnFieldChanged()
        {
            if (_loading) return;
            var chosen = _intervalSepBox.SelectedIndex == 1 ? "," : ";";
            var baseCulture = _draft.BaseCulture;
            _draft.IntervalSepOverride = chosen == baseCulture.IntervalSep ? null : chosen;

            var env = _matrixBox.SelectedIndex == 1 ? "bmatrix" : "pmatrix";
            _draft.MatrixEnvOverride = env == baseCulture.MatrixEnv ? null : env;

            RefreshHintsAndPreview();
        }

        private void RefreshHintsAndPreview()
        {
            _intervalSepHint.Text = _draft.IntervalSepOverride == null ? Strings.SettingsInheritedHint : Strings.SettingsOverrideHint;
            _matrixHint.Text = _draft.MatrixEnvOverride == null ? Strings.SettingsInheritedHint : Strings.SettingsOverrideHint;

            var c = _draft.ToEngineCulture();
            string dec = _draft.Culture == AppSettings.CultureUs ? "1.5" : "1,5";
            string matrix = c.MatrixEnv == "bmatrix" ? "[ 1 2 ; 3 4 ]" : "( 1 2 ; 3 4 )";
            _preview.Text = $"{dec}     x ∈ [0{c.IntervalSep}1]     {matrix}";
        }

        private void SaveAndClose()
        {
            if (!SettingsStore.Save(_draft))
                MessageBox.Show(Strings.SettingsSaveFailed, Strings.SettingsWindowTitle,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            // Les toggles ruban lisent les mêmes switches : resync.
            RibbonCallback.Instance?.InvalidateSettingsToggles();
            DialogResult = true;
            Close();
        }

        // ── Helpers visuels ─────────────────────────────────────────────

        private static TextBlock SectionHeader(string text) => new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
        };

        private static TextBlock HintBlock() => new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(110, 110, 110)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };

        private static UIElement FieldRow(string label, ComboBox box, TextBlock hint)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            row.Children.Add(new TextBlock { Text = label, FontSize = 12, Margin = new Thickness(0, 0, 0, 2) });
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(box);
            line.Children.Add(hint);
            row.Children.Add(line);
            return row;
        }
    }
}
