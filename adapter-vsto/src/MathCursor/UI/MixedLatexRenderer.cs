using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfMath.Controls;

namespace MathCursor.UI
{
    /// <summary>
    /// Rendu LaTeX → UIElement pour les popups WPF, mixed-rendering :
    /// les segments LaTeX que WpfMath sait rendre passent par
    /// <see cref="FormulaControl"/> ; les ~9 macros que WpfMath rend en
    /// glyphe placeholder "." (cf. brief 2026-05-06-wpfmath-fallback-renderer)
    /// sont remplacées par leur équivalent Unicode et rendues dans un
    /// <see cref="TextBlock"/> avec Cambria Math.
    ///
    /// Macros substituées Unicode : <c>\mathbb{R/N/Z/Q/C/P}</c>, <c>\mapsto</c>,
    /// <c>\iint</c>, <c>\iiint</c>. Les autres macros (cases, pmatrix, widehat,
    /// overline, oint, limsup/liminf, etc.) restent gérées par <see cref="WpfMathAdapter"/>
    /// qui les transforme en LaTeX que WpfMath sait rendre nativement.
    ///
    /// Limite V1 connue : tokenization au top-level uniquement. Une macro
    /// problématique nichée dans un sous-groupe (<c>\frac{\mathbb{R}}{2}</c>)
    /// ne sera pas substituée — elle retombe sur la subst dégradée historique
    /// de <see cref="WpfMathAdapter"/> (ou son défaut si retirée). Acceptable
    /// pour V1 vu le corpus d'usage réel.
    /// </summary>
    public static class MixedLatexRenderer
    {
        public enum SegmentType { WpfMath, Unicode }

        public struct Segment
        {
            public SegmentType Type;
            public string Content;
        }

        // Alternation unique : leftmost-first match → \iiint AVANT \iint pour
        // éviter que `\iiint` ne soit tokenizé comme `\iint` + `iint`. Les
        // lookaheads `(?![a-zA-Z])` empêchent de matcher des prefixes (ex:
        // `\mapstop` ne doit pas matcher `\mapsto`).
        // `(?![a-zA-Z_^])` sur \iint/\iiint : une intégrale multiple AVEC
        // bornes (\iint_{0}^{1}) ne peut pas être extraite — le TextBlock ∬
        // ne porte pas de scripts, et le segment WpfMath restant commencerait
        // par `_` (« script needs a base »). Ces cas restent dans le segment
        // WpfMath, dégradés en \int\int par WpfMathAdapter (audit 2026-06-10).
        private static readonly Regex UnicodeMacroRegex = new Regex(
            @"\\mathbb\{[RNZQCP]\}"
            + @"|\\iiint(?![a-zA-Z_^])"
            + @"|\\iint(?![a-zA-Z_^])"
            + @"|\\mapsto(?![a-zA-Z])",
            RegexOptions.Compiled);

        /// <summary>
        /// Découpe le LaTeX en segments alternés WpfMath/Unicode. Top-level
        /// only — n'inspecte pas l'intérieur des accolades.
        /// </summary>
        public static List<Segment> Tokenize(string latex)
        {
            var result = new List<Segment>();
            if (string.IsNullOrEmpty(latex)) return result;

            int lastEnd = 0;
            foreach (Match m in UnicodeMacroRegex.Matches(latex))
            {
                if (m.Index > lastEnd)
                {
                    result.Add(new Segment
                    {
                        Type = SegmentType.WpfMath,
                        Content = latex.Substring(lastEnd, m.Index - lastEnd),
                    });
                }
                result.Add(new Segment
                {
                    Type = SegmentType.Unicode,
                    Content = ResolveUnicode(m.Value),
                });
                lastEnd = m.Index + m.Length;
            }
            if (lastEnd < latex.Length)
            {
                result.Add(new Segment
                {
                    Type = SegmentType.WpfMath,
                    Content = latex.Substring(lastEnd),
                });
            }
            return result;
        }

        private static string ResolveUnicode(string macroMatch)
        {
            // \mathbb{X} : extraire la lettre en position 8.
            if (macroMatch.Length == 10 && macroMatch.StartsWith(@"\mathbb{"))
            {
                switch (macroMatch[8])
                {
                    case 'R': return "ℝ";
                    case 'N': return "ℕ";
                    case 'Z': return "ℤ";
                    case 'Q': return "ℚ";
                    case 'C': return "ℂ";
                    case 'P': return "ℙ";
                }
            }
            if (macroMatch == @"\iiint")  return "∭";
            if (macroMatch == @"\iint")   return "∬";
            if (macroMatch == @"\mapsto") return "↦";
            return macroMatch;
        }

        /// <summary>
        /// Rendu de la formule : un seul <see cref="FormulaControl"/> si pas
        /// de macro Unicode (fast path), sinon un <see cref="StackPanel"/>
        /// horizontal qui mixe FormulaControl et TextBlock.
        /// </summary>
        public static UIElement Render(string latex, double scale = 18)
        {
            if (string.IsNullOrEmpty(latex))
                return new TextBlock { Text = "", FontSize = 14 };

            var segments = Tokenize(latex);

            // Fast path : aucune macro Unicode → FormulaControl direct
            // (évite l'overhead StackPanel pour 95 % des cas).
            if (segments.Count == 1 && segments[0].Type == SegmentType.WpfMath)
                return MakeFormulaControl(segments[0].Content, scale);

            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };
            foreach (var seg in segments)
            {
                UIElement child;
                if (seg.Type == SegmentType.Unicode)
                {
                    child = MakeUnicodeTextBlock(seg.Content, scale);
                }
                else
                {
                    if (string.IsNullOrEmpty(seg.Content)) continue;
                    child = MakeFormulaControl(seg.Content, scale);
                }
                panel.Children.Add(child);
            }
            return panel;
        }

        private static UIElement MakeFormulaControl(string latex, double scale)
        {
            // WpfMathAdapter applique encore les substitutions cases/pmatrix/
            // bmatrix/vmatrix/widehat/overline/setminus/etc. testées
            // empiriquement (cf. ADR 2026-04-24-Feat-popup-revert-wpfmath).
            var adapted = WpfMathAdapter.Adapt(latex);
            if (string.IsNullOrEmpty(adapted))
                return new TextBlock { Text = "" };

            try
            {
                return new FormulaControl
                {
                    Formula = adapted,
                    Scale = scale,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            }
            catch
            {
                // Filet de sécurité : si WpfMath lève sur du LaTeX malformé,
                // on dégrade en TextBlock avec le texte brut.
                return new TextBlock
                {
                    Text = adapted,
                    FontFamily = new FontFamily("Cambria Math, Cambria, Segoe UI Symbol, Segoe UI"),
                    FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                };
            }
        }

        private static UIElement MakeUnicodeTextBlock(string text, double scale)
        {
            // Ratio FontSize / scale calibré empiriquement pour matcher la
            // hauteur des glyphes math voisins (FormulaControl Scale=18 rend
            // des caractères ~24-28 px de hauteur). FontSize = scale * 1.4
            // donne un ℝ ~ 25 px de haut, visuellement proche.
            var tb = new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Cambria Math, Cambria, Segoe UI Symbol, Segoe UI"),
                FontSize = scale * 1.4,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(20, 20, 20)),
            };
            // TextFormattingMode.Display : rendu pixel-aligné (vs Ideal qui
            // bave aux tailles UI ~24-30 px). Fait une vraie différence en
            // popup. TextRenderingMode.ClearType : sub-pixel AA pour un
            // contour plus net sur écran LCD.
            TextOptions.SetTextFormattingMode(tb, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(tb, TextRenderingMode.ClearType);
            return tb;
        }
    }
}
