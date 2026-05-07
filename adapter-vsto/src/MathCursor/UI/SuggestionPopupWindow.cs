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
        // BaseTopLatex (= TopLatex avant splice contextuel par RulePin /
        // SpanOverride / SidecarSignal). Utilisé pour les recalculs lors
        // des changements d'alt user, pour éviter le double-splice
        // (= partir de "AB" brut au lieu de "\vec{AB}" déjà splicé).
        // Cf. fix bug double-splice 2026-05-07.
        private string _baseTopLatex = "";
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
        // Sont remontés via CurrentSidecar au commit pour que le SuggestionService
        // les persiste et les utilise au cross-merge (cf. ADR 2026-05-06
        // resolution-sidecar-and-layers, Phase 1.5).
        private readonly System.Collections.Generic.List<MathCursor.Core.Resolution.SpanPin> _sessionSpanPins
            = new System.Collections.Generic.List<MathCursor.Core.Resolution.SpanPin>();

        // SpanOverrides v2 (étape 7 brief 2026-05-07) : utilisés UNIQUEMENT
        // pour le cas "revert" (l'utilisateur choisit l'alt-revert dans la
        // popup → SpanOverride{sig, AltIdxRevert} qui dit "pour ce span
        // précis, garde le default brut, pas le RulePin / scoring contextuel").
        private readonly System.Collections.Generic.List<MathCursor.Core.Resolution.SpanOverride> _sessionSpanOverrides
            = new System.Collections.Generic.List<MathCursor.Core.Resolution.SpanOverride>();

        // Mapping index UI (= position dans _alternatives) → altIdx réel
        // de la rule. -1 = alt-revert. Construit à chaque Show() en
        // tenant compte du filtrage de l'alt active (= déjà appliquée
        // par défaut, pas affichée pour ne pas polluer visuellement —
        // demande user 2026-05-07).
        private System.Collections.Generic.IReadOnlyList<int> _altIdxMap
            = System.Array.Empty<int>();

        private readonly Dictionary<string, string> _resolvedSubstitutions
            = new Dictionary<string, string>();

        // Map defaultLatex → ruleId pour les subs accumulées dans
        // _resolvedSubstitutions. Permet de purger par rule au Show()
        // pour éviter les subs stale (= un choix paren sur "BC" reste
        // appliqué même quand l'user a changé d'avis pour vec via une
        // autre session popup). Cf. fix bug 2026-05-07 « la finale (BC)
        // apparaissait à la fois en final ET dans les alts ».
        private readonly Dictionary<string, string> _subsRuleMap
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

        /// <summary>
        /// Sidecar de résolutions accumulé pour la zone courante. Vide tant
        /// que l'utilisateur n'a résolu aucune alt. Cf. ADR 2026-05-06
        /// resolution-sidecar-and-layers — utilisé au commit + cross-merge
        /// pour réappliquer les choix vec/paren/etc. quand on re-pipeline.
        /// </summary>
        public MathCursor.Core.Resolution.ResolutionSidecar CurrentSidecar
        {
            get
            {
                if (_sessionSpanPins.Count == 0 && _sessionSpanOverrides.Count == 0)
                    return MathCursor.Core.Resolution.ResolutionSidecar.Empty;

                // Sidecar v2 (cf. brief 2026-05-07-rule-pin-span-override-refactor) :
                // produit aussi des RulePins déduits des SpanPins de la session.
                // Sémantique : « si l'user a choisi vec sur AB, on muscle vec
                // pour la rule globalement ». Last-write-wins par RuleId :
                // la dernière alt choisie gagne (cohérent avec
                // ZoneResolver.ResolveBestAlt qui consulte RulePins en ordre).
                // Les SpanPins legacy restent peuplés pour rétro-compat
                // (l'overload Resolve(rawSource, sidecar) historique les
                // utilise toujours).
                var rulePinsByRule = new Dictionary<string, int>();
                foreach (var sp in _sessionSpanPins)
                {
                    if (string.IsNullOrEmpty(sp.Rule) || sp.AltIdx < 0) continue;
                    rulePinsByRule[sp.Rule] = sp.AltIdx; // last-write-wins
                }
                var rulePins = new List<MathCursor.Core.Resolution.RulePin>(rulePinsByRule.Count);
                foreach (var kv in rulePinsByRule)
                    rulePins.Add(new MathCursor.Core.Resolution.RulePin(kv.Key, kv.Value));

                return new MathCursor.Core.Resolution.ResolutionSidecar(
                    spanPins: _sessionSpanPins.ToArray(),
                    zoneVotes: null, // ZoneVotes legacy retirés — RulePins prennent le relais
                    rulePins: rulePins,
                    spanOverrides: _sessionSpanOverrides.ToArray());
            }
        }

        /// <summary>
        /// Insère ou met à jour un pin par <c>(Rule, Offset, Len)</c> —
        /// last-write-wins sur <c>AltIdx</c>. Cohérent avec la sémantique
        /// <see cref="MathCursor.Core.Resolution.ResolutionSidecar"/> côté
        /// <c>ZoneResolver.Resolve(source, sidecar)</c> qui prend le dernier
        /// pin matching d'un span. Évite l'accumulation quand <c>Show()</c>
        /// est appelé plusieurs fois sur le même span (NER fluctuant pendant
        /// la frappe) — sinon le sidecar grossit avec des doublons et le
        /// merger les recalibre, polluant les autres OMaths au merge.
        /// Cf. cause racine bug 06-05 (flèches empilées).
        /// </summary>
        /// <returns><c>true</c> si un nouveau pin a été ajouté, <c>false</c>
        /// si un pin existant a été mis à jour (overwrite altIdx).</returns>
        private bool UpsertSpanPin(string ruleId, int offset, int len, int altIdx)
        {
            for (int i = _sessionSpanPins.Count - 1; i >= 0; i--)
            {
                var p = _sessionSpanPins[i];
                if (p.Rule == ruleId && p.Offset == offset && p.Len == len)
                {
                    _sessionSpanPins[i] = new MathCursor.Core.Resolution.SpanPin(
                        ruleId, offset, len, altIdx);
                    return false;
                }
            }
            _sessionSpanPins.Add(new MathCursor.Core.Resolution.SpanPin(
                ruleId, offset, len, altIdx));
            return true;
        }

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

        /// <summary>
        /// Crée ou met à jour un <see cref="MathCursor.Core.Resolution.SpanOverride"/>
        /// pour la signature donnée. Last-write-wins par signature.
        /// (Cf. brief 2026-05-07 étape 7 : alt-revert dans la popup.)
        /// </summary>
        private void UpsertSpanOverride(MathCursor.Core.Resolution.MatchSignature sig, int altIdx)
        {
            if (sig == null) return;
            for (int i = _sessionSpanOverrides.Count - 1; i >= 0; i--)
            {
                if (_sessionSpanOverrides[i].Signature.Equals(sig))
                {
                    _sessionSpanOverrides[i] = new MathCursor.Core.Resolution.SpanOverride(sig, altIdx);
                    return;
                }
            }
            _sessionSpanOverrides.Add(new MathCursor.Core.Resolution.SpanOverride(sig, altIdx));
        }

        /// <summary>
        /// Trouve la <see cref="MathCursor.Core.Resolution.MatchSignature"/>
        /// du match correspondant à <paramref name="spotStart"/>/<paramref name="spotEnd"/>
        /// dans <see cref="_allMatches"/> (liste décorée par ZoneResolver).
        /// Retourne null si aucun match correspondant ou si pas de signature.
        /// </summary>
        private MathCursor.Core.Resolution.MatchSignature FindSignatureAtSpot(
            int spotStart, int spotEnd, string defaultLatex)
        {
            // 1) Match exact par (Start, End)
            foreach (var m in _allMatches)
            {
                if (m.Start == spotStart && m.End == spotEnd && m.Signature != null)
                    return m.Signature;
            }
            // 2) Fallback : match par DefaultLatex à la même position de début
            //    (en cas de décalage léger dû au splice in-popup).
            foreach (var m in _allMatches)
            {
                if (m.Spot?.DefaultLatex == defaultLatex && m.Start == spotStart && m.Signature != null)
                    return m.Signature;
            }
            return null;
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
            int activeAltIdxFromCaller = -1,
            string baseTopLatex = null)
        {
            LogPopup($"Show top=\"{topLatex}\" rule=\"{ruleId}\" alts={(alternatives?.Count ?? 0)} pos=({screenX:F0},{screenY:F0})");

            // Purge les subs stale pour les rules présentes dans allMatches.
            // Évite qu'un ancien choix popup (= sub posée pour une autre zone
            // NER plus tôt) reste appliqué quand la rule réapparaît dans la
            // zone courante avec un altIdx différent. Cf. fix 2026-05-07.
            // Le RulePin du sidecar (passé au ZoneResolver) prend le relais
            // pour le splice contextuel cross-zone.
            if (allMatches != null)
            {
                var rulesPresent = new HashSet<string>();
                foreach (var m in allMatches)
                    if (!string.IsNullOrEmpty(m?.Spot?.RuleId)) rulesPresent.Add(m.Spot.RuleId);

                if (rulesPresent.Count > 0)
                {
                    var keysToRemove = new List<string>();
                    foreach (var kv in _subsRuleMap)
                        if (rulesPresent.Contains(kv.Value)) keysToRemove.Add(kv.Key);
                    foreach (var k in keysToRemove)
                    {
                        _resolvedSubstitutions.Remove(k);
                        _subsRuleMap.Remove(k);
                    }
                }
            }

            // 1) Si l'utilisateur a déjà choisi cette règle dans la session,
            //    on applique sa préférence en silence dans les substitutions
            //    locales + on enregistre un SpanPin pour la propagation au
            //    cross-merge.
            //
            //    NOTE 2026-05-07 : on NE TUE PLUS la zone d'alts (= plus de
            //    `alternatives = Array.Empty`). Avec le filtrage de l'alt
            //    active (étape 7) et le RulePin qui pré-splice le TopLatex,
            //    la popup peut rester ouverte avec les autres options
            //    accessibles (paren, crochet, revert) — l'user voit la
            //    formule finale en bas + peut changer d'avis. Demande user
            //    « ça me fait peur sur la généricité » : l'auto-kill silent
            //    masquait les options et confondait la sémantique.
            if (!string.IsNullOrEmpty(ruleId)
                && alternatives != null && alternatives.Count > 0
                && _rulePreferences.TryGetValue(ruleId, out int preferredIdx)
                && preferredIdx >= 0 && preferredIdx < alternatives.Count
                && spotStart >= 0 && spotEnd > spotStart && spotEnd <= (topLatex?.Length ?? 0))
            {
                string defaultLatex = ResolveDefaultLatex(topLatex!, spotStart, spotEnd, allMatches);
                var preferredAlt = alternatives[preferredIdx];
                _resolvedSubstitutions[defaultLatex] = preferredAlt.Latex;
                _subsRuleMap[defaultLatex] = ruleId;
                UpsertSpanPin(ruleId, spotStart, spotEnd - spotStart, preferredIdx);
                LogPopup($"applied pref (popup stays open) rule=\"{ruleId}\" altIdx={preferredIdx} → \"{preferredAlt.Latex}\"");
            }

            // 2) Applique les résolutions d'ambiguïté précédemment validées
            //    (par string ou par règle ci-dessus).
            string substitutedTop = topLatex ?? "";
            foreach (var kv in _resolvedSubstitutions)
                substitutedTop = substitutedTop.Replace(kv.Key, kv.Value);

            // 3) Recalculer la position du spot APRÈS substitutions.
            // Chercher d'abord dans substitutedTop (= post-subs popup-locales).
            // Si pas trouvé (= splice contextuel ZoneResolver a remplacé
            // defaultLatex par une alt active, ex: "Y^{2}" → "Y_{2}"),
            // fallback sur les bornes originales pour que la popup s'ouvre
            // quand même.
            int newSpotStart = -1, newSpotEnd = -1;
            if (alternatives != null && alternatives.Count > 0
                && spotStart >= 0 && spotEnd > spotStart && spotEnd <= (topLatex?.Length ?? 0))
            {
                string defaultLatex = ResolveDefaultLatex(topLatex!, spotStart, spotEnd, allMatches);
                if (!string.IsNullOrEmpty(defaultLatex))
                {
                    int newIdx = substitutedTop.LastIndexOf(defaultLatex, StringComparison.Ordinal);
                    if (newIdx >= 0)
                    {
                        newSpotStart = newIdx;
                        newSpotEnd = newIdx + defaultLatex.Length;
                    }
                    else
                    {
                        // Fallback : defaultLatex absent de substitutedTop
                        // (= splice contextuel actif). Bornes originales.
                        newSpotStart = spotStart;
                        newSpotEnd = spotEnd;
                    }
                }
            }

            _topLatex = substitutedTop;
            _baseTopLatex = baseTopLatex ?? substitutedTop; // fallback rétro-compat
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

                int activeAltIdx = -1;
                // 1) PRIORITÉ : AppliedAltIdx du match courant — c'est ce
                //    que le ZoneResolver a EFFECTIVEMENT appliqué dans le
                //    TopLatex, donc l'alt qu'il NE FAUT PAS afficher dans
                //    la popup (= invariant user 2026-05-07).
                if (allMatches != null)
                {
                    foreach (var m in allMatches)
                    {
                        if (m?.Spot == null) continue;
                        if (m.Start == spotStart && m.End == spotEnd
                            && m.AppliedAltIdx >= 0)
                        {
                            activeAltIdx = m.AppliedAltIdx;
                            break;
                        }
                    }
                }
                // 2) Fallback : pref in-session via _rulePreferences.
                if (activeAltIdx < 0
                    && !string.IsNullOrEmpty(ruleId)
                    && _rulePreferences.TryGetValue(ruleId, out int active))
                {
                    activeAltIdx = active;
                }
                // 3) Fallback : activeAltIdxFromCaller (= calculé côté caller).
                else if (activeAltIdx < 0 && activeAltIdxFromCaller >= 0)
                {
                    activeAltIdx = activeAltIdxFromCaller;
                }

                bool hasActive = activeAltIdx >= 0 && activeAltIdx < alternatives.Count;

                var built = new System.Collections.Generic.List<MathCursor.Core.Lattice.AmbiguityAlternative>(alternatives.Count + 1);
                var altIdxMap = new System.Collections.Generic.List<int>(alternatives.Count + 1);

                // Alt-revert ajoutée UNIQUEMENT si une alt est active (sinon
                // le default brut est déjà la finale, doublon visuel).
                if (hasActive)
                {
                    built.Add(new MathCursor.Core.Lattice.AmbiguityAlternative(defaultLatex));
                    altIdxMap.Add(MathCursor.Core.Resolution.SpanOverride.AltIdxRevert);
                }

                // Vraies alts, sauf l'alt active filtrée.
                for (int i = 0; i < alternatives.Count; i++)
                {
                    if (i == activeAltIdx) continue;
                    built.Add(alternatives[i]);
                    altIdxMap.Add(i);
                }

                _alternatives = built;
                _altIdxMap = altIdxMap;
            }
            else
            {
                _alternatives = Array.Empty<MathCursor.Core.Lattice.AmbiguityAlternative>();
                _altIdxMap = Array.Empty<int>();
            }
            _spotStart = newSpotStart;
            _spotEnd = newSpotEnd;
            _resolvedLatex = substitutedTop;
            _focusOnFinal = true;
            // Pré-sélection : si l'alt-revert est en index 0 (= une alt est
            // active et splicée en finale), sauter à l'index 1 (= 1ʳᵉ vraie
            // alt non-active). Sinon (cas vierge), index 0 = 1ʳᵉ vraie alt.
            bool firstIsRevert = _altIdxMap.Count > 0
                && _altIdxMap[0] == MathCursor.Core.Resolution.SpanOverride.AltIdxRevert;
            _altIndex = (firstIsRevert && _alternatives.Count > 1) ? 1 : 0;

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

            // === Mapping index UI → altIdx réel (cf. brief 2026-05-07 étape 7) ===
            // _altIdxMap[uiIndex] = altIdx réel, ou AltIdxRevert (-1) pour l'alt-revert.
            int realAltIdx = _altIndex < _altIdxMap.Count
                ? _altIdxMap[_altIndex]
                : _altIndex; // fallback rétro-compat (ne devrait pas arriver)
            bool isRevert = realAltIdx == MathCursor.Core.Resolution.SpanOverride.AltIdxRevert;

            // === Index revert : l'utilisateur veut le defaultLatex brut ===
            if (isRevert)
            {
                string defaultLatex = _alternatives[_altIndex].Latex;
                var sig = FindSignatureAtSpot(_spotStart, _spotEnd, defaultLatex);
                if (sig != null)
                    UpsertSpanOverride(sig, MathCursor.Core.Resolution.SpanOverride.AltIdxRevert);

                // Retire la substitution locale si une cascade précédente
                // l'avait posée — pour que ce span précis affiche bien le
                // default brut localement aussi.
                _resolvedSubstitutions.Remove(defaultLatex);
                _subsRuleMap.Remove(defaultLatex);

                // Partir de _baseTopLatex (cf. fix double-splice).
                string newResolvedRevert = _baseTopLatex;
                foreach (var kv in _resolvedSubstitutions)
                    newResolvedRevert = newResolvedRevert.Replace(kv.Key, kv.Value);
                _resolvedLatex = newResolvedRevert;

                _alternatives = Array.Empty<MathCursor.Core.Lattice.AmbiguityAlternative>();
                _altsRow.Children.Clear();
                _altsRowBorder.Visibility = Visibility.Collapsed;
                _spotStart = _spotEnd = -1;
                _topLatex = _resolvedLatex;

                _finalContainer.Children.Clear();
                _finalContainer.Children.Add(BuildFinalRow(_resolvedLatex));
                _focusOnFinal = true;
                UpdateHighlight();
                LogPopup($"Resolved as REVERT rule=\"{_currentRuleId}\" default=\"{defaultLatex}\"");
                return true;
            }

            var selectedAlt = _alternatives[_altIndex];

            // Branche source-mutation : la résolution n'est plus une sub
            // LaTeX locale, c'est une mutation de la source brute. On délègue
            // à l'hôte (qui détient la source) via l'event ; il appliquera la
            // mutation, relancera le pipeline et appellera Show() à nouveau
            // avec le nouveau résultat. La popup elle-même ne fait rien de
            // plus que propager.
            // realAltIdx déjà calculé via _altIdxMap au-dessus.
            if (selectedAlt.Mutation != null)
            {
                LogPopup($"Resolved via SourceMutation rule=\"{_currentRuleId}\" altIdx={realAltIdx} replacement=\"{selectedAlt.Mutation.Replacement}\"");
                SourceMutationRequested?.Invoke(_currentRuleId, realAltIdx, selectedAlt.Mutation);
                return true;
            }

            // Mutation null = identity (ex: V garde V) → on ferme juste la
            // popup zone d'ambig, source inchangée. L'utilisateur peut
            // continuer à taper, V reste son interprétation.
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

            var alt = selectedAlt.Latex;
            int chosenAltIdx = realAltIdx;
            string ruleId = _currentRuleId;

            // 1) Mémorise la pref par RÈGLE (pour les futures ambiguïtés du
            //    même type qui apparaîtront pendant que l'élève continue à
            //    taper).
            if (!string.IsNullOrEmpty(ruleId))
                _rulePreferences[ruleId] = chosenAltIdx;

            // 2) Cascade IMMÉDIATE : applique le même choix à TOUS les autres
            //    matches du même RuleId déjà présents dans la formule courante
            //    (ex: résoudre BC en vec → AB et AC deviennent aussi vec).
            //    En parallèle, on enregistre un SpanPin par match cascadé pour
            //    que le sidecar (Phase 1.5 ADR 06-05) survive au re-pipeline
            //    (cross-merge multi-ligne notamment).
            foreach (var match in _allMatches)
            {
                if (match.Spot.RuleId == ruleId
                    && chosenAltIdx >= 0 && chosenAltIdx < match.Spot.Alternatives.Count)
                {
                    _resolvedSubstitutions[match.Spot.DefaultLatex] = match.Spot.Alternatives[chosenAltIdx].Latex;
                    _subsRuleMap[match.Spot.DefaultLatex] = ruleId;
                    int matchLen = match.End - match.Start;
                    if (matchLen > 0)
                    {
                        UpsertSpanPin(ruleId, match.Start, matchLen, chosenAltIdx);
                    }
                }
            }

            // 3) Recompose _resolvedLatex en applicant TOUTES les substitutions
            //    accumulées (cascade incluse).
            //    IMPORTANT : on part de _baseTopLatex (= avant splice contextuel)
            //    pour éviter le double-splice. Si on partait de _topLatex
            //    déjà splicé (\vec{AB}), un .Replace("AB", "(AB)") trouverait
            //    "AB" dans "\vec{AB}" et produirait "\vec{(AB)}" — bug user
            //    2026-05-07 « ça fait un truc nul droite vecteur ».
            string newResolved = _baseTopLatex;
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
                _subsRuleMap.Clear();
                _rulePreferences.Clear();
                _sessionSpanPins.Clear();
                _sessionSpanOverrides.Clear();
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
