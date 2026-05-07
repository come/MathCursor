using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MathCursor.Core;
using MathCursor.Core.Resolution;

namespace MathCursor.UI.Debug
{
    /// <summary>
    /// Pane WPF debug qui affiche en temps réel le contexte de résolution
    /// MathCursor : raw source, sidecar (pins + votes), historique paragraphe,
    /// scores agrégés par alternative et trace par signal.
    ///
    /// <para>Mis à jour par <see cref="MathCursor.Host.SuggestionService.ContextResolved"/>
    /// à chaque résolution. Outil de validation visuelle pour le brief
    /// 2026-05-07-global-context-multi-zoom-ranking — permet de voir le
    /// scoring contextuel s'appliquer (ex: cas AB/AD système 2 lignes).</para>
    ///
    /// <para>Pas de tests unitaires : UI debug pure, valeur dans la boucle
    /// d'usage (lancer Word, observer).</para>
    /// </summary>
    public sealed class ContextInspectorPane : UserControl
    {
        private readonly TextBox _content;
        private readonly TextBlock _status;

        public ContextInspectorPane()
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            _status = new TextBlock
            {
                Margin = new Thickness(8, 8, 8, 4),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
                Text = "(en attente d'une résolution...)",
            };
            Grid.SetRow(_status, 0);
            grid.Children.Add(_status);

            // TextBox readonly (vs TextBlock) pour permettre la sélection +
            // copier-coller du contenu (utile pour partager une trace).
            _content = new TextBox
            {
                Margin = new Thickness(8, 4, 8, 8),
                FontFamily = new FontFamily("Cascadia Code, Consolas, Lucida Console, monospace"),
                FontSize = 11,
                IsReadOnly = true,
                IsReadOnlyCaretVisible = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                UndoLimit = 0,
            };
            Grid.SetRow(_content, 1);
            grid.Children.Add(_content);

            Content = grid;
        }

        /// <summary>
        /// Reformatte le pane avec un nouveau contexte/hints. À appeler sur
        /// le thread UI (l'appelant doit Dispatcher.Invoke si besoin).
        /// </summary>
        public void Update(string rawSource, ContextSnapshot snapshot, ScoringHints hints, ResolvedZone resolved)
        {
            _status.Text = $"⟳ {DateTime.Now:HH:mm:ss.fff}  |  source: \"{Truncate(rawSource, 40)}\"";

            var sb = new StringBuilder();

            sb.AppendLine("=== Raw source ===");
            sb.AppendLine(rawSource ?? "<null>");
            sb.AppendLine();

            // Résultat effectif de la résolution. C'est la vérité terrain :
            // le scoring contextuel (Hints ci-dessous) ne sert à rien si
            // aucune AmbiguityMatch ne correspond aux hints émis.
            if (resolved != null)
            {
                sb.AppendLine("=== Top LaTeX (rendu final post-splice) ===");
                sb.AppendLine(resolved.TopLatex ?? "<null>");
                sb.AppendLine($"IsIncomplete: {resolved.IsIncomplete}");
                sb.AppendLine();

                sb.AppendLine($"=== Ambiguïtés détectées sur cette zone : {resolved.AllMatches.Count} ===");
                if (resolved.AllMatches.Count == 0)
                {
                    sb.AppendLine("  (aucune ambig détectée — rule jamais activée sur ce rawSource)");
                }
                else
                {
                    foreach (var m in resolved.AllMatches)
                    {
                        sb.AppendLine($"  • [{m.Start}..{m.End}) rule={m.Spot.RuleId} default=\"{m.Spot.DefaultLatex}\" alts={m.Spot.Alternatives.Count}");
                    }
                }
                if (resolved.Spot != null)
                    sb.AppendLine($"Spot rightmost (popup va s'ouvrir) : rule={resolved.Spot.RuleId}");
                else
                    sb.AppendLine("Spot rightmost : (none — pas de popup d'ambig)");
                sb.AppendLine();
            }

            if (snapshot != null)
            {
                sb.AppendLine($"=== Sidecar (zone OMath en cours) ===");
                sb.AppendLine($"Pins span-level   : {snapshot.Sidecar.SpanPins.Count}");
                foreach (var p in snapshot.Sidecar.SpanPins)
                    sb.AppendLine($"  • [{p.Offset}..{p.Offset + p.Len}) rule={p.Rule} alt={p.AltIdx}");
                sb.AppendLine($"Votes (rules×alt) : {snapshot.Sidecar.ZoneVotes.Count}");
                foreach (var rv in snapshot.Sidecar.ZoneVotes)
                    foreach (var av in rv.Value)
                        sb.AppendLine($"  • {rv.Key}:{av.Key} = {av.Value}");
                sb.AppendLine();

                sb.AppendLine($"=== Historique ¶ courant (L2) ===");
                sb.AppendLine($"Pins ¶            : {snapshot.RecentParagraphPins.Count}");
                foreach (var p in snapshot.RecentParagraphPins)
                    sb.AppendLine($"  • rule={p.Rule} alt={p.AltIdx}");
                sb.AppendLine();
            }

            if (hints != null && hints.AltScores.Count > 0)
            {
                sb.AppendLine("=== Scoring hints (alts triées par score décroissant) ===");
                foreach (var kv in hints.AltScores.OrderByDescending(kv => kv.Value))
                    sb.AppendLine($"  {kv.Value,7:F3}   {kv.Key}");
                sb.AppendLine();

                sb.AppendLine($"=== Trace ({hints.Trace.Count} contributions) ===");
                foreach (var line in hints.Trace)
                    sb.AppendLine($"  {line}");
            }
            else
            {
                sb.AppendLine("=== Scoring hints ===");
                sb.AppendLine("(aucun hint — contexte vide ou aucun signal applicable)");
            }

            _content.Text = sb.ToString();
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }
    }
}
