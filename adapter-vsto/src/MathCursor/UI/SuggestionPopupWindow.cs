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
        // Span-pins accumulés à mesure que l'utilisateur résout des alts.
        // Mapping index UI (= position dans _alternatives) → altIdx réel
        // de la rule. -1 = alt-revert. Construit à chaque Show() en
        // tenant compte du filtrage de l'alt active.
        private System.Collections.Generic.IReadOnlyList<int> _altIdxMap
            = System.Array.Empty<int>();

        // P7d (2026-05-21) : PatternCompletion[] reçues du ZoneResolver et
        // affichées en tête de la liste d'alternatives. Quand l'user
        // sélectionne une entry pattern, TryResolveAlt set
        // _resolvedLatex = selectedAlt.Latex (= PreviewLatex du pattern)
        // pour que le commit Enter insère cet OMath.
        private IReadOnlyList<MathCursor.Core.Patterns.PatternCompletion> _patternCompletions
            = Array.Empty<MathCursor.Core.Patterns.PatternCompletion>();

        /// <summary>
        /// Base sentinel pour <see cref="_altIdxMap"/> qui marque une entry
        /// comme PatternCompletion (vs ambig closed). L'index dans
        /// <see cref="_patternCompletions"/> est encodé via
        /// <c>AltIdxPatternBase - patternIndex</c> (-1000 = pattern 0,
        /// -1001 = pattern 1, etc.). Permet de retrouver le PatternCompletion
        /// original (et son PreviewLatex pour le commit, distinct du
        /// HintLatex affiché). Local au popup (pas dans Core).
        /// </summary>
        private const int AltIdxPatternBase = -1000;

        private readonly TextBlock _debugFooter;
        private readonly TextBlock _reportLink;
        private bool _navMode;

        public bool IsNavMode => _navMode;
        public bool IsFocusOnFinal => _focusOnFinal;

        /// <summary>LaTeX qui sera commité dans Word à l'Enter sur la formule
        /// finale. Intègre les éventuelles résolutions d'alternatives faites
        /// par l'utilisateur en navigant + Enter sur les alts.</summary>
        public string CurrentFinalLatex => _resolvedLatex;

        /// <summary>
        /// Résout le DefaultLatex correct pour une ambig au span (spotStart,
        /// spotEnd) en cherchant dans <paramref name="allMatches"/>. Évite le
        /// piège du <c>topLatex.Substring(...)</c> qui peut renvoyer du
        /// gibberish quand le topLatex a été splicé par un RulePin (= les
        /// bornes spotStart/End restent du baseTopLatex pré-splice).
        /// Fallback substring si aucun match trouvé.
        /// </summary>
        private static string ResolveDefaultLatex(
            string topLatex,
            int spotStart,
            int spotEnd,
            IReadOnlyList<MathCursor.Core.Lattice.AmbiguityMatch> allMatches)
        {
            if (allMatches != null)
            {
                foreach (var m in allMatches)
                {
                    if (m?.Spot == null) continue;
                    if (m.Start == spotStart && m.End == spotEnd)
                        return m.Spot.DefaultLatex ?? "";
                }
            }
            if (string.IsNullOrEmpty(topLatex)) return "";
            if (spotStart < 0 || spotEnd <= spotStart || spotEnd > topLatex.Length) return "";
            return topLatex.Substring(spotStart, spotEnd - spotStart);
        }


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
        /// Rendu LaTeX → UIElement via MixedLatexRenderer (mixed-rendering
        /// FormulaControl + TextBlock Unicode pour <c>\mathbb</c>, <c>\mapsto</c>,
        /// <c>\iint</c>, <c>\iiint</c>). Cf. brief 2026-05-06-wpfmath-fallback-renderer.
        /// </summary>
        private UIElement RenderMath(string latex)
        {
            var container = new Grid { Margin = new Thickness(8, 4, 12, 4) };
            container.Children.Add(MixedLatexRenderer.Render(latex ?? "", 18));
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
            string debugText = "",
            IReadOnlyList<MathCursor.Core.Patterns.PatternCompletion>? patternCompletions = null)
        {
            LogPopup($"Show top=\"{topLatex}\" rule=\"{ruleId}\" alts={(alternatives?.Count ?? 0)} pos=({screenX:F0},{screenY:F0}) patterns={(patternCompletions?.Count ?? 0)}");

            // P7d (2026-05-21) : rendering définitif des PatternCompletion[].
            // Pattern d'abord en tête des alternatives (Choix 5 du plan P7),
            // converties en AmbiguityAlternative virtuelles (Latex =
            // PreviewLatex, Mutation préservée). Sentinel AltIdxPattern dans
            // _altIdxMap pour distinguer ces entries des ambig closed.
            // Quand l'user sélectionne une entry Pattern : TryResolveAlt
            // détecte le sentinel et set _resolvedLatex = selectedAlt.Latex.
            // Le commit Enter standard insère cet OMath. Cf. ADR
            // 2026-05-21-Feat-popup-pattern-completion-rendering (P7d).
            _patternCompletions = patternCompletions
                ?? Array.Empty<MathCursor.Core.Patterns.PatternCompletion>();
            if (_patternCompletions.Count > 0)
            {
                LogPopup($"Pattern completions: {_patternCompletions.Count}, first preview=\"{_patternCompletions[0].PreviewLatex}\" desc=\"{_patternCompletions[0].Description}\"");
            }

            // Spot bounds : le topLatex est déjà splicé par le ZoneResolver
            // (= AppliedAltIdx reflète la pref active). Pas de re-substitution
            // locale → bornes inchangées par rapport à l'entrée. Le fallback
            // sur les bornes d'entrée couvre le cas defaultLatex introuvable.
            int newSpotStart = -1, newSpotEnd = -1;
            if (alternatives != null && alternatives.Count > 0
                && spotStart >= 0 && spotEnd > spotStart && spotEnd <= (topLatex?.Length ?? 0))
            {
                newSpotStart = spotStart;
                newSpotEnd = spotEnd;
            }

            _topLatex = topLatex ?? "";
            _currentRuleId = ruleId ?? "";
            _allMatches = allMatches ?? Array.Empty<MathCursor.Core.Lattice.AmbiguityMatch>();
            // Construction de la liste affichée dans la popup d'ambig.
            // Règle invariant 2026-05-07 (user) : « le choix final (par
            // défaut si pas de choix) d'une désambig n'est jamais montré
            // dans une popup de désambig ».
            //
            //   - Si activeAltIdx = -1 (cas vierge, pas de RulePin) : la
            //     finale = defaultLatex (brut). On affiche TOUTES les vraies
            //     alts. Pas d'alt-revert (= redondant avec la finale).
            //   - Si activeAltIdx >= 0 (RulePin / scoring actif) : la finale
            //     = alts[activeAltIdx]. On AJOUTE l'alt-revert (permet de
            //     revenir au default brut) + les autres vraies alts (sauf
            //     l'active filtrée).
            //
            // Précédence pour activeAltIdx :
            //   1) _rulePreferences[ruleId] (in-session courante)
            //   2) activeAltIdxFromCaller (RulePin cross-commit via _globalCtx)
            if (newSpotStart >= 0 && alternatives != null && alternatives.Count > 0)
            {
                string defaultLatex = ResolveDefaultLatex(topLatex!, spotStart, spotEnd, allMatches);

                // Logique pure de filter extraite dans MathCursor.Core.Resolution.PopupAltFilter
                // pour testabilité (cf. tests PopupAltFilterTests).
                var filtered = MathCursor.Core.Resolution.PopupAltFilter.Filter(
                    spotStart, spotEnd, alternatives, allMatches, defaultLatex);

                // P7d : prepend les PatternCompletion en tête (Choix 5 plan P7).
                _alternatives = PrependPatternCompletions(filtered.Built, out var prependedMap);
                _altIdxMap = MergePrependedMap(prependedMap, filtered.AltIdxMap);

                // Diag pour debug bugs filter rapportés (« click X fait Y »).
                LogPopup($"filter activeAltIdx={filtered.ActiveAltIdx} spotStart={spotStart} spotEnd={spotEnd} allMatches={allMatches?.Count ?? 0} prepended={prependedMap.Count}");
                if (allMatches != null)
                {
                    for (int dbgI = 0; dbgI < allMatches.Count; dbgI++)
                    {
                        var dm = allMatches[dbgI];
                        LogPopup($"  match[{dbgI}] Start={dm?.Start} End={dm?.End} AppliedAltIdx={dm?.AppliedAltIdx} ruleId=\"{dm?.Spot?.RuleId}\"");
                    }
                }
                var altDbg = new System.Text.StringBuilder("Show alts map: ");
                for (int dbgI = 0; dbgI < _alternatives.Count; dbgI++)
                {
                    altDbg.Append($"[{dbgI}]→real={_altIdxMap[dbgI]} latex=\"{_alternatives[dbgI].Latex}\" ");
                }
                LogPopup(altDbg.ToString());
            }
            else if (_patternCompletions.Count > 0)
            {
                // P7d : pas d'ambig closed mais des Pattern → la popup ne
                // montre QUE les Patterns. Cas typique : V x app a R sans
                // ambig closed (pas de AB / tight-chain / etc.) dans la zone.
                _alternatives = PrependPatternCompletions(
                    Array.Empty<MathCursor.Core.Lattice.AmbiguityAlternative>(),
                    out var patternMap);
                _altIdxMap = patternMap;
                LogPopup($"Patterns-only popup: {_alternatives.Count} entries");
            }
            else
            {
                _alternatives = Array.Empty<MathCursor.Core.Lattice.AmbiguityAlternative>();
                _altIdxMap = Array.Empty<int>();
            }
            _spotStart = newSpotStart;
            _spotEnd = newSpotEnd;
            _resolvedLatex = _topLatex;
            _focusOnFinal = true;
            // Pré-sélection : si l'alt-revert est en index 0 (= une alt est
            // active et splicée en finale), sauter à l'index 1 (= 1ʳᵉ vraie
            // alt non-active). Sinon (cas vierge), index 0 = 1ʳᵉ vraie alt.
            bool firstIsRevert = _altIdxMap.Count > 0
                && _altIdxMap[0] == MathCursor.Core.Resolution.SpanOverride.AltIdxRevert;
            _altIndex = (firstIsRevert && _alternatives.Count > 1) ? 1 : 0;

            _debugFooter.Text = string.IsNullOrEmpty(debugText) ? "" : "NER: \"" + debugText + "\"";

            BuildAltCells();

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

        /// <summary>
        /// Reconstruit la rangée d'alts UI à partir de <c>_alternatives</c>.
        /// Chaque cell est une <c>Border</c> avec un <c>RenderMath</c> du
        /// latex de l'alt, + handlers <c>MouseEnter</c> (preview) et
        /// <c>MouseLeftButtonUp</c> (résolution direct).
        /// </summary>
        // P7d helpers : intégration PatternCompletion[] en tête des alts ambig.

        /// <summary>
        /// Convertit chaque <see cref="MathCursor.Core.Patterns.PatternCompletion"/>
        /// en <see cref="MathCursor.Core.Lattice.AmbiguityAlternative"/> virtuelle
        /// (Latex = <b>HintLatex</b> avec carrés `\square` pour les slots vides,
        /// Mutation préservée), prepend en tête de <paramref name="baseAlts"/>
        /// et retourne la liste combinée. L'index dans <see cref="_patternCompletions"/>
        /// est encodé dans <paramref name="prependedMap"/> via
        /// <c>AltIdxPatternBase - i</c> (P5R+ 2026-05-21).
        ///
        /// <para>Le commit Enter (= ResolveCurrentAltIfFocused → set
        /// _resolvedLatex) utilise le <b>PreviewLatex</b> distinct (sans
        /// carrés) en faisant le lookup inverse via patternIndex.</para>
        /// </summary>
        private IReadOnlyList<MathCursor.Core.Lattice.AmbiguityAlternative> PrependPatternCompletions(
            IReadOnlyList<MathCursor.Core.Lattice.AmbiguityAlternative> baseAlts,
            out IReadOnlyList<int> prependedMap)
        {
            if (_patternCompletions.Count == 0)
            {
                prependedMap = Array.Empty<int>();
                return baseAlts;
            }
            var combined = new System.Collections.Generic.List<MathCursor.Core.Lattice.AmbiguityAlternative>(
                _patternCompletions.Count + baseAlts.Count);
            var mapList = new System.Collections.Generic.List<int>(_patternCompletions.Count);
            for (int i = 0; i < _patternCompletions.Count; i++)
            {
                var pc = _patternCompletions[i];
                // HintLatex pour l'affichage popup (= avec carrés visuels)
                combined.Add(new MathCursor.Core.Lattice.AmbiguityAlternative(
                    pc.HintLatex, pc.Mutation));
                // Encodage de l'index pour retrouver pc.PreviewLatex au commit
                mapList.Add(AltIdxPatternBase - i);
            }
            foreach (var alt in baseAlts) combined.Add(alt);
            prependedMap = mapList;
            return combined;
        }

        /// <summary>
        /// Fusionne <paramref name="prependedMap"/> (entries Pattern en tête)
        /// avec <paramref name="baseMap"/> (entries ambig closed standard) en
        /// préservant l'ordre.
        /// </summary>
        private static IReadOnlyList<int> MergePrependedMap(
            IReadOnlyList<int> prependedMap,
            IReadOnlyList<int> baseMap)
        {
            if (prependedMap.Count == 0) return baseMap;
            var combined = new System.Collections.Generic.List<int>(prependedMap.Count + baseMap.Count);
            combined.AddRange(prependedMap);
            combined.AddRange(baseMap);
            return combined;
        }

        private void BuildAltCells()
        {
            _altsRow.Children.Clear();
            if (_alternatives.Count == 0)
            {
                _altsRowBorder.Visibility = Visibility.Collapsed;
                return;
            }
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
                cell.MouseLeftButtonUp += (_, __) =>
                {
                    int realFromMap = idx < _altIdxMap.Count ? _altIdxMap[idx] : idx;
                    string latexClicked = idx < _alternatives.Count ? _alternatives[idx].Latex : "<oob>";
                    LogPopup($"click display={idx} real={realFromMap} latex=\"{latexClicked}\"");
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
            LogPopup($"resolve_called focusOnFinal={_focusOnFinal} altIdx={_altIndex} altCount={_alternatives.Count} spot=[{_spotStart},{_spotEnd}]");
            if (_focusOnFinal) { LogPopup("  → SKIP (focus on final)"); return false; }
            if (_alternatives.Count == 0) { LogPopup("  → SKIP (no alts)"); return false; }
            if (_altIndex < 0 || _altIndex >= _alternatives.Count) { LogPopup("  → SKIP (altIdx oob)"); return false; }

            // === Mapping index UI → altIdx réel (cf. brief 2026-05-07 étape 7) ===
            // _altIdxMap[uiIndex] = altIdx réel, ou AltIdxRevert (-1) pour l'alt-revert,
            // ou AltIdxPattern (-200) pour une PatternCompletion (P7d).
            int realAltIdx = _altIndex < _altIdxMap.Count
                ? _altIdxMap[_altIndex]
                : _altIndex; // fallback rétro-compat (ne devrait pas arriver)

            // P7d + P5R+ : Pattern sélectionné → set _resolvedLatex avec le
            // PreviewLatex (= sans carrés) et fermer la zone d'ambig. L'affichage
            // popup montrait HintLatex (avec carrés), mais le commit Enter
            // utilise le PreviewLatex pour l'OMath final inséré dans Word.
            if (realAltIdx <= AltIdxPatternBase)
            {
                int patternIndex = AltIdxPatternBase - realAltIdx;
                if (patternIndex < 0 || patternIndex >= _patternCompletions.Count)
                {
                    LogPopup($"  → SKIP (pattern index oob: {patternIndex})");
                    return false;
                }
                var pc = _patternCompletions[patternIndex];
                LogPopup($"Resolved as PATTERN[{patternIndex}] preview=\"{pc.PreviewLatex}\" (hint was \"{pc.HintLatex}\")");
                _resolvedLatex = pc.PreviewLatex;
                // Refresh final container avec le PreviewLatex (commit-clean)
                _finalContainer.Children.Clear();
                _finalContainer.Children.Add(BuildFinalRow(_resolvedLatex));
                // Fermer la zone d'ambig comme un identity pick
                _alternatives = Array.Empty<MathCursor.Core.Lattice.AmbiguityAlternative>();
                _altsRow.Children.Clear();
                _altsRowBorder.Visibility = Visibility.Collapsed;
                _spotStart = _spotEnd = -1;
                _focusOnFinal = true;
                UpdateHighlight();
                return true;
            }

            // Le check _spotStart < 0 ne s'applique pas aux Patterns (déjà
            // sortis au-dessus). Pour les autres entries, on a besoin d'un
            // span valide pour AddPreference/Revert.
            if (_spotStart < 0 || _spotEnd <= _spotStart) { LogPopup("  → SKIP (invalid spot)"); return false; }

            bool isRevert = realAltIdx == MathCursor.Core.Resolution.SpanOverride.AltIdxRevert;

            // === Index revert : l'utilisateur veut le defaultLatex brut ===
            // Delegate au service : RemovePreference(ruleId) puis re-resolve.
            // ApplyPreferences ne trouvera plus de pref pour cette rule →
            // muted == rawSource → topLatex propre sans la mutation. Cf.
            // ADR refacto désambig 2026-05-20.
            if (isRevert)
            {
                LogPopup($"Resolved as REVERT rule=\"{_currentRuleId}\" → fire SourceMutationRequested(AltIdxRevert)");
                SourceMutationRequested?.Invoke(_currentRuleId, MathCursor.Core.Resolution.SpanOverride.AltIdxRevert, null);
                // Ferme la zone d'ambig localement comme un identity pick.
                // Évite le « rien ne se passe » quand l'user clique le default
                // alors qu'aucune pref n'existait (= RemovePreference est no-op
                // → re-resolve donne le même topLatex). Cf. UX 2026-05-21.
                _alternatives = Array.Empty<MathCursor.Core.Lattice.AmbiguityAlternative>();
                _altsRow.Children.Clear();
                _altsRowBorder.Visibility = Visibility.Collapsed;
                _spotStart = _spotEnd = -1;
                _focusOnFinal = true;
                UpdateHighlight();
                return true;
            }

            var selectedAlt = _alternatives[_altIndex];

            // Identity : alt.Latex == topLatex courant → pas de changement,
            // on ferme juste la zone d'ambig (ex: V isolé garde V).
            if (string.Equals(selectedAlt.Latex, _topLatex, StringComparison.Ordinal))
            {
                LogPopup($"Resolved as identity (alt[{realAltIdx}] = current) — no change");
                _alternatives = Array.Empty<MathCursor.Core.Lattice.AmbiguityAlternative>();
                _altsRow.Children.Clear();
                _altsRowBorder.Visibility = Visibility.Collapsed;
                _spotStart = _spotEnd = -1;
                _focusOnFinal = true;
                UpdateHighlight();
                return true;
            }

            // Path unique pour tous les autres picks : SourceMutationRequested.
            // Le service appelle AddPreference(ruleId, altIdx) qui REMPLACE
            // l'ancien pin pour ce ruleId, puis re-resolve. ApplyPreferences
            // part de la source ORIGINALE et applique l'alt.Mutation native
            // → pas de Replace local, pas de nesting (ex \vec{(AB)}).
            //
            // mutation peut être null si l'alt courante (calculée sur la
            // muted source) n'a pas de Mutation propre — le service ignore
            // ce paramètre et utilise ruleId + altIdx pour la pref.
            // Cf. ADR refacto désambig 2026-05-20.
            LogPopup($"Resolved via SourceMutation rule=\"{_currentRuleId}\" altIdx={realAltIdx}"
                + (selectedAlt.Mutation != null ? $" replacement=\"{selectedAlt.Mutation.Replacement}\"" : " (pref-only, no native mutation on muted)"));

            // Le sidecar cross-paragraph est construit côté resolver via
            // BuildSidecar(_preferences) — plus de span pin popup-local.
            SourceMutationRequested?.Invoke(_currentRuleId, realAltIdx, selectedAlt.Mutation);
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
            // Plus de cache local à reset — _resolver._preferences est la
            // source de vérité unique (cf. refacto désambig 2026-05-21 D).
            // Le paramètre resetCaches est gardé pour rétro-compat callers.
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
