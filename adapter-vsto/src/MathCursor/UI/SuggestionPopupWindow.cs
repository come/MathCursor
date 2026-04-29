using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WpfMath.Controls;

namespace MathCursor.UI
{
    /// <summary>
    /// Popup WPF affichée sous le caret. Modèle phase 5b2 :
    ///
    /// <list type="bullet">
    /// <item>Si pas d'ambiguïté → une seule ligne : la formule finale (fond
    ///   vert clair, signal "formule reconnue").</item>
    /// <item>Si ambiguïté → alternatives en colonnes en haut, formule finale
    ///   en bas, séparées par un trait. Up/Down navigue entre la zone alts
    ///   et la zone finale ; Enter sur alt résout localement (recompose la
    ///   formule finale, popup reste ouverte) ; Enter sur finale commit.</item>
    /// </list>
    ///
    /// Construite en code (pas de XAML) pour minimiser le set-up VSTO.
    /// </summary>
    public sealed class SuggestionPopupWindow : Window
    {
        private const double DisplayOpacity = 0.5;
        private const double NavOpacity = 0.9;
        private const int FadeMs = 150;

        private readonly StackPanel _altsRow;
        private readonly Border _altsRowBorder;
        private readonly Grid _finalContainer;

        private string _topLatex = "";
        private string _currentRuleId = "";
        private IReadOnlyList<MathCursor.Core.Lattice.AmbiguityAlternative> _alternatives
            = Array.Empty<MathCursor.Core.Lattice.AmbiguityAlternative>();
        // Tous les matches d'ambiguïté de la formule courante (pas juste le
        // current spot). Permet la résolution en cascade : quand l'utilisateur
        // valide vec pour BC, on applique vec aussi à AB et AC qui sont des
        // patterns du même RuleId déjà présents dans la zone.
        private IReadOnlyList<MathCursor.Core.Lattice.AmbiguityMatch> _allMatches
            = Array.Empty<MathCursor.Core.Lattice.AmbiguityMatch>();
        private int _spotStart = -1, _spotEnd = -1;
        private string _resolvedLatex = "";
        private bool _focusOnFinal = true;
        private int _altIndex;

        // Cache des résolutions par STRING (defaultLatex → altLatex). Appliqué
        // à chaque update du polling NER pour préserver les choix précis de
        // l'utilisateur (ex: "AB" → "\\vec{AB}" exactement, même si on retape).
        private readonly Dictionary<string, string> _resolvedSubstitutions
            = new Dictionary<string, string>();

        // Cache des préférences par TYPE de pattern (ruleId → altIndex). Si
        // l'utilisateur a résolu un "two-uppercase" en choisissant l'alt #0
        // (\vec), tous les "two-uppercase" suivants de la session se résolvent
        // auto en \vec sans avoir à reproposer l'ambig. Reset au HidePopup.
        private readonly Dictionary<string, int> _rulePreferences
            = new Dictionary<string, int>();

        private readonly TextBlock _debugFooter;
        private readonly TextBlock _reportLink;
        private bool _navMode;

        public bool IsNavMode => _navMode;
        public bool IsFocusOnFinal => _focusOnFinal;

        /// <summary>LaTeX qui sera commité dans Word à l'Enter sur la formule
        /// finale. Intègre les éventuelles résolutions d'alternatives faites
        /// par l'utilisateur en navigant + Enter sur les alts.</summary>
        public string CurrentFinalLatex => _resolvedLatex;

        public event Action ReportRequested;

        /// <summary>
        /// Levé quand l'utilisateur résout une alt dont la mutation source
        /// est non null (ex: V→forall). Le service hôte mémorise la
        /// préférence (ruleId, altIdx) et relance le pipeline. La popup
        /// elle-même n'a pas accès à la source brute — c'est l'hôte qui
        /// maintient la source dans Word.
        ///
        /// Args : `(ruleId, altIdx, mutation)`. ruleId+altIdx servent à
        /// mémoriser la pref par règle (V→forall pour la session). mutation
        /// est l'instance précise pour le V courant.
        /// </summary>
        public event Action<string, int, MathCursor.Core.Lattice.SourceMutation> SourceMutationRequested;

        /// <summary>
        /// Levé quand l'utilisateur clique sur la formule finale dans la popup
        /// (équivalent d'un Enter sur la finale). L'hôte fait le commit OMath.
        /// </summary>
        public event Action CommitRequested;

        public SuggestionPopupWindow()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Width = 320;
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

            // Ligne d'alternatives en colonnes
            _altsRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(4, 4, 4, 4),
            };
            _altsRowBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background = Brushes.White,
                Visibility = Visibility.Collapsed,
                Child = _altsRow,
            };

            // Conteneur de la formule finale (toujours présent en mode actif)
            _finalContainer = new Grid
            {
                Margin = new Thickness(0),
                Background = Brushes.White,
            };

            _debugFooter = new TextBlock
            {
                Text = "",
                FontSize = 10,
                FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)),
                Margin = new Thickness(8, 2, 8, 4),
                Padding = new Thickness(0),
                TextWrapping = TextWrapping.Wrap,
            };
            var topSeparator = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Margin = new Thickness(0, 4, 0, 0),
                Child = _debugFooter,
            };

            _reportLink = new TextBlock
            {
                Text = "Signaler une erreur",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(90, 110, 160)),
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(8, 2, 8, 4),
                TextDecorations = TextDecorations.Underline,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            _reportLink.MouseLeftButtonUp += (_, __) => ReportRequested?.Invoke();
            var reportLinkBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = _reportLink,
            };

            var stack = new StackPanel();
            stack.Children.Add(_altsRowBorder);
            stack.Children.Add(_finalContainer);
            stack.Children.Add(topSeparator);
            stack.Children.Add(reportLinkBorder);
            border.Child = stack;
            Content = border;

            SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            };
        }

        /// <summary>
        /// Rendu LaTeX → UIElement via WpfMath. Substitutions cosmétiques
        /// (mathbb, widehat, cases…) via WpfMathAdapter.
        /// </summary>
        private UIElement RenderMath(string latex)
        {
            string adapted = WpfMathAdapter.Adapt(latex ?? "");
            var container = new Grid { Margin = new Thickness(8, 4, 12, 4) };
            if (string.IsNullOrWhiteSpace(adapted))
            {
                container.Children.Add(new TextBlock { Text = "", FontSize = 14 });
                return container;
            }
            try
            {
                var formula = new FormulaControl
                {
                    Formula = adapted,
                    Scale = 18,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                container.Children.Add(formula);
            }
            catch
            {
                container.Children.Add(new TextBlock
                {
                    Text = adapted,
                    FontSize = 14,
                    FontFamily = new FontFamily("Cambria Math, Cambria, Segoe UI Symbol, Segoe UI"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                });
            }
            return container;
        }

        /// <summary>
        /// Affiche la popup. Si <paramref name="alternatives"/> est non-vide
        /// ET qu'aucune préférence n'est mémorisée pour <paramref name="ruleId"/>,
        /// la zone d'ambiguïté apparaît en haut. Sinon (pas d'alts, ou pref
        /// déjà mémorisée), juste la formule finale (fond vert clair).
        /// </summary>
        public void Show(
            string topLatex,
            string ruleId,
            IReadOnlyList<MathCursor.Core.Lattice.AmbiguityAlternative> alternatives,
            int spotStart,
            int spotEnd,
            IReadOnlyList<MathCursor.Core.Lattice.AmbiguityMatch> allMatches,
            double screenX,
            double screenY,
            string debugText = "")
        {
            LogPopup($"Show top=\"{topLatex}\" rule=\"{ruleId}\" alts={(alternatives?.Count ?? 0)} pos=({screenX:F0},{screenY:F0})");

            // 1) Si l'utilisateur a déjà choisi cette règle dans la session,
            //    on applique sa préférence en silence : la résolution est
            //    intégrée à la substitutions string et on n'affiche pas la
            //    zone d'alts.
            if (!string.IsNullOrEmpty(ruleId)
                && alternatives != null && alternatives.Count > 0
                && _rulePreferences.TryGetValue(ruleId, out int preferredIdx)
                && preferredIdx >= 0 && preferredIdx < alternatives.Count
                && spotStart >= 0 && spotEnd > spotStart && spotEnd <= (topLatex?.Length ?? 0))
            {
                string defaultLatex = topLatex!.Substring(spotStart, spotEnd - spotStart);
                var preferredAlt = alternatives[preferredIdx];
                _resolvedSubstitutions[defaultLatex] = preferredAlt.Latex;
                LogPopup($"auto-applied pref rule=\"{ruleId}\" altIdx={preferredIdx} → \"{preferredAlt.Latex}\"");
                alternatives = Array.Empty<MathCursor.Core.Lattice.AmbiguityAlternative>();
            }

            // 2) Applique les résolutions d'ambiguïté précédemment validées
            //    (par string ou par règle ci-dessus).
            string substitutedTop = topLatex ?? "";
            foreach (var kv in _resolvedSubstitutions)
                substitutedTop = substitutedTop.Replace(kv.Key, kv.Value);

            // 3) Recalculer la position du spot APRÈS substitutions.
            int newSpotStart = -1, newSpotEnd = -1;
            if (alternatives != null && alternatives.Count > 0
                && spotStart >= 0 && spotEnd > spotStart && spotEnd <= (topLatex?.Length ?? 0))
            {
                string defaultLatex = topLatex!.Substring(spotStart, spotEnd - spotStart);
                int newIdx = substitutedTop.LastIndexOf(defaultLatex, StringComparison.Ordinal);
                if (newIdx >= 0)
                {
                    newSpotStart = newIdx;
                    newSpotEnd = newIdx + defaultLatex.Length;
                }
            }

            _topLatex = substitutedTop;
            _currentRuleId = ruleId ?? "";
            _allMatches = allMatches ?? Array.Empty<MathCursor.Core.Lattice.AmbiguityMatch>();
            _alternatives = (newSpotStart >= 0 && alternatives != null)
                ? alternatives
                : Array.Empty<MathCursor.Core.Lattice.AmbiguityAlternative>();
            _spotStart = newSpotStart;
            _spotEnd = newSpotEnd;
            _resolvedLatex = substitutedTop;
            _focusOnFinal = true;
            _altIndex = 0;

            _debugFooter.Text = string.IsNullOrEmpty(debugText) ? "" : "NER: \"" + debugText + "\"";

            // Zone alternatives
            _altsRow.Children.Clear();
            if (_alternatives.Count > 0)
            {
                for (int i = 0; i < _alternatives.Count; i++)
                {
                    var altLatex = _alternatives[i].Latex;
                    var cell = new Border
                    {
                        BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                        BorderThickness = new Thickness(0, 0, 1, 0),
                        Padding = new Thickness(2),
                    };
                    cell.Child = RenderMath(altLatex);
                    int idx = i;
                    cell.MouseEnter += (_, __) =>
                    {
                        _altIndex = idx;
                        _focusOnFinal = false;
                        EnterNavMode();
                        UpdateHighlight();
                    };
                    // Clic sur alt = résout cette alt direct (équivalent
                    // navigation + Enter). UX : moins de friction.
                    cell.MouseLeftButtonUp += (_, __) =>
                    {
                        _altIndex = idx;
                        _focusOnFinal = false;
                        EnterNavMode();
                        UpdateHighlight();
                        ResolveCurrentAltIfFocused();
                    };
                    _altsRow.Children.Add(cell);
                }
                _altsRowBorder.Visibility = Visibility.Visible;
            }
            else
            {
                _altsRowBorder.Visibility = Visibility.Collapsed;
            }

            // Zone formule finale
            _finalContainer.Children.Clear();
            _finalContainer.Children.Add(BuildFinalRow(_resolvedLatex));

            // Reset navMode AVANT UpdateHighlight, sinon une popup réouverte
            // après un commit garde l'ancien _navMode=true et apparaît déjà
            // surlignée (l'élève croit qu'Enter va valider direct).
            _navMode = false;
            UpdateHighlight();
            Left = screenX;
            Top = screenY;
            if (!IsVisible) base.Show();
            BeginAnimation(OpacityProperty,
                new DoubleAnimation(DisplayOpacity, TimeSpan.FromMilliseconds(FadeMs)));
        }

        private UIElement BuildFinalRow(string latex)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0),
                Background = Brushes.White,
            };
            panel.Children.Add(RenderMath(latex));
            panel.Children.Add(new TextBlock
            {
                Text = "★",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 8, 0),
            });
            panel.MouseEnter += (_, __) =>
            {
                _focusOnFinal = true;
                EnterNavMode();
                UpdateHighlight();
            };
            // Clic sur la formule finale = commit OMath direct (équivalent
            // Enter). Délégué à l'hôte qui détient la logique d'insertion.
            panel.MouseLeftButtonUp += (_, __) =>
            {
                _focusOnFinal = true;
                EnterNavMode();
                UpdateHighlight();
                CommitRequested?.Invoke();
            };
            return panel;
        }

        private void UpdateHighlight()
        {
            // Highlight bleu uniquement en nav mode actif (l'utilisateur a
            // touché une flèche). Avant ça, AUCUN fond colorisé sur les
            // items — sinon l'élève croit qu'Enter va valider la ligne
            // visuellement mise en avant.
            for (int i = 0; i < _altsRow.Children.Count; i++)
            {
                if (_altsRow.Children[i] is Border cell)
                {
                    cell.Background = (_navMode && !_focusOnFinal && i == _altIndex)
                        ? new SolidColorBrush(Color.FromRgb(190, 215, 250))
                        : Brushes.Transparent;
                }
            }
            if (_finalContainer.Children.Count > 0 && _finalContainer.Children[0] is StackPanel finalPanel)
            {
                // Fond bleu UNIQUEMENT en nav mode + focus sur final. Sinon
                // blanc — pas de fond vert "formule complète" (se confondait
                // avec le highlight de nav, retiré sur demande user).
                finalPanel.Background = (_navMode && _focusOnFinal)
                    ? new SolidColorBrush(Color.FromRgb(190, 215, 250))
                    : Brushes.White;
            }
        }

        /// <summary>
        /// Si l'utilisateur a Enter sur une alternative (focus alts), résout
        /// localement : remplace la zone ambiguë dans la formule finale par
        /// l'alt sélectionnée, FERME la zone d'ambiguïté (la rangée d'alts
        /// disparaît), passe focus sur la formule finale qui devient le seul
        /// élément actif. Retourne true si une résolution a été faite.
        /// </summary>
        public bool ResolveCurrentAltIfFocused()
        {
            if (_focusOnFinal) return false;
            if (_alternatives.Count == 0) return false;
            if (_altIndex < 0 || _altIndex >= _alternatives.Count) return false;
            if (_spotStart < 0 || _spotEnd <= _spotStart) return false;

            var selectedAlt = _alternatives[_altIndex];

            // Branche source-mutation : la résolution n'est plus une sub
            // LaTeX locale, c'est une mutation de la source brute. On délègue
            // à l'hôte (qui détient la source) via l'event ; il appliquera la
            // mutation, relancera le pipeline et appellera Show() à nouveau
            // avec le nouveau résultat. La popup elle-même ne fait rien de
            // plus que propager.
            if (selectedAlt.Mutation != null)
            {
                LogPopup($"Resolved via SourceMutation rule=\"{_currentRuleId}\" altIdx={_altIndex} replacement=\"{selectedAlt.Mutation.Replacement}\"");
                SourceMutationRequested?.Invoke(_currentRuleId, _altIndex, selectedAlt.Mutation);
                return true;
            }

            // Mutation null = identity (ex: V garde V) → on ferme juste la
            // popup zone d'ambig, source inchangée. L'utilisateur peut
            // continuer à taper, V reste son interprétation.
            if (string.Equals(selectedAlt.Latex, _topLatex, StringComparison.Ordinal))
            {
                LogPopup($"Resolved as identity (alt[{_altIndex}] = current) — no change");
                _alternatives = Array.Empty<MathCursor.Core.Lattice.AmbiguityAlternative>();
                _altsRow.Children.Clear();
                _altsRowBorder.Visibility = Visibility.Collapsed;
                _spotStart = _spotEnd = -1;
                _focusOnFinal = true;
                UpdateHighlight();
                return true;
            }

            var alt = selectedAlt.Latex;
            int chosenAltIdx = _altIndex;
            string ruleId = _currentRuleId;

            // 1) Mémorise la pref par RÈGLE (pour les futures ambiguïtés du
            //    même type qui apparaîtront pendant que l'élève continue à
            //    taper).
            if (!string.IsNullOrEmpty(ruleId))
                _rulePreferences[ruleId] = chosenAltIdx;

            // 2) Cascade IMMÉDIATE : applique le même choix à TOUS les autres
            //    matches du même RuleId déjà présents dans la formule courante
            //    (ex: résoudre BC en vec → AB et AC deviennent aussi vec).
            foreach (var match in _allMatches)
            {
                if (match.Spot.RuleId == ruleId
                    && chosenAltIdx >= 0 && chosenAltIdx < match.Spot.Alternatives.Count)
                {
                    _resolvedSubstitutions[match.Spot.DefaultLatex] = match.Spot.Alternatives[chosenAltIdx].Latex;
                }
            }

            // 3) Recompose _resolvedLatex en applicant TOUTES les substitutions
            //    accumulées (cascade incluse).
            string newResolved = _topLatex;
            foreach (var kv in _resolvedSubstitutions)
                newResolved = newResolved.Replace(kv.Key, kv.Value);
            _resolvedLatex = newResolved;

            // Ferme la zone d'ambiguïté : la résolution est validée, l'alt
            // sélectionnée est intégrée dans la formule finale, plus rien à
            // choisir. La popup montre juste la formule finale (fond vert
            // clair pour signaler "OK, prête à commiter").
            _alternatives = Array.Empty<MathCursor.Core.Lattice.AmbiguityAlternative>();
            _altsRow.Children.Clear();
            _altsRowBorder.Visibility = Visibility.Collapsed;
            _spotStart = _spotEnd = -1;
            _topLatex = _resolvedLatex; // pour la cohérence des futures substitutions

            _finalContainer.Children.Clear();
            _finalContainer.Children.Add(BuildFinalRow(_resolvedLatex));

            _focusOnFinal = true;
            UpdateHighlight();
            LogPopup($"Resolved alt[{_altIndex}]=\"{alt}\" → resolved=\"{_resolvedLatex}\" (ambig zone closed)");
            return true;
        }

        public void EnterNavMode()
        {
            if (_navMode) return;
            _navMode = true;
            // À l'entrée en nav (Down depuis Word), focus sur le premier choix
            // d'ambiguïté s'il y en a, sinon sur la formule finale.
            if (_alternatives.Count > 0)
            {
                _focusOnFinal = false;
                _altIndex = 0;
            }
            else
            {
                _focusOnFinal = true;
            }
            UpdateHighlight();
            BeginAnimation(OpacityProperty,
                new DoubleAnimation(NavOpacity, TimeSpan.FromMilliseconds(FadeMs / 2)));
        }

        /// <summary>
        /// Up/Down navigue ENTRE les zones (alts ↔ finale). Aux bords (Up
        /// depuis les alts, Down depuis la finale), on sort du nav mode et
        /// on retourne false → la touche pass-through à Word, le curseur
        /// texte bouge normalement. Permet de quitter la popup en navigant
        /// "au-delà".
        /// </summary>
        public bool MoveSelection(int delta)
        {
            if (_alternatives.Count == 0) return false;
            if (delta > 0)
            {
                // Down
                if (!_focusOnFinal)
                {
                    _focusOnFinal = true;
                    UpdateHighlight();
                    return true;
                }
                // Down depuis finale → pass-through Word
                ExitNavMode();
                return false;
            }
            else
            {
                // Up
                if (_focusOnFinal)
                {
                    _focusOnFinal = false;
                    UpdateHighlight();
                    return true;
                }
                // Up depuis alts → sortir et pass-through Word
                ExitNavMode();
                return false;
            }
        }

        private void ExitNavMode()
        {
            if (!_navMode) return;
            _navMode = false;
            UpdateHighlight(); // retire le fond bleu sélection
            BeginAnimation(OpacityProperty,
                new DoubleAnimation(DisplayOpacity, TimeSpan.FromMilliseconds(FadeMs / 2)));
        }

        /// <summary>
        /// Left/Right navigue HORIZONTALEMENT dans les alternatives. N'a
        /// d'effet que si focus est sur la zone alts (sinon ignoré, retourne
        /// false). Retourne true si la touche est consommée par la popup.
        /// </summary>
        public bool MoveSelectionHorizontal(int delta)
        {
            if (_alternatives.Count == 0) return false;
            if (_focusOnFinal) return false;
            int next = _altIndex + delta;
            if (next < 0) next = 0;
            if (next >= _alternatives.Count) next = _alternatives.Count - 1;
            _altIndex = next;
            UpdateHighlight();
            return true;
        }

        public void HidePopup(bool resetCaches = true)
        {
            // Les caches de résolutions / préférences ne sont reset QUE quand
            // l'utilisateur ferme explicitement la session popup (Esc, commit,
            // sortie de zone). Ils sont préservés sur les hide transients
            // (NER ne détecte temporairement pas pendant la frappe), pour ne
            // pas perdre les choix au prochain tick.
            if (resetCaches)
            {
                _resolvedSubstitutions.Clear();
                _rulePreferences.Clear();
            }
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

        private static void LogPopup(string message)
        {
            try
            {
                var dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MathCursor", "logs");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "mathcursor.log"),
                    $"{DateTime.UtcNow:o} popup {message}{Environment.NewLine}");
            }
            catch { }
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
