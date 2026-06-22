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
            + @"|\\mapsto(?![a-zA-Z])"
            + @"|\\mathbin\{/\\!/\}",
            RegexOptions.Compiled);

        /// <summary>
        /// Découpe le LaTeX en segments alternés WpfMath/Unicode. Top-level
        /// only — un match NICHÉ dans des accolades (<c>\overline{\mathbb{Z}}</c>)
        /// est laissé dans son segment WpfMath, où les substitutions dégradées
        /// de <see cref="WpfMathAdapter"/> s'en chargent (extraire un segment
        /// au milieu d'un groupe produirait des accolades orphelines —
        /// attrapé par l'audit popup sur la fixture conjZ, 2026-06-10).
        /// </summary>
        public static List<Segment> Tokenize(string latex)
        {
            var result = new List<Segment>();
            if (string.IsNullOrEmpty(latex)) return result;

            int lastEnd = 0;
            foreach (Match m in UnicodeMacroRegex.Matches(latex))
            {
                if (BraceDepthAt(latex, m.Index) > 0) continue; // niché : top-level only
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

        // Profondeur d'accolades STRUCTURELLES à la position donnée (les
        // accolades littérales \{ \} et les commandes \x sont sautées).
        private static int BraceDepthAt(string s, int upto)
        {
            int d = 0;
            for (int i = 0; i < upto; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length) { i++; continue; }
                if (c == '{') d++;
                else if (c == '}') d--;
            }
            return d;
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
            // Parallèles OBLIQUES (AB // CD) : WpfMath ne connaît ni \mathbin
            // ni \! — U+2AFD est le glyphe penché attendu (le \parallel de
            // WpfMathAdapter, vertical ∥, ne reste que pour les cas nichés).
            if (macroMatch == @"\mathbin{/\!/}") return "⫽";
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

            // \boxed{…} pleine formule : WpfMath ne sait pas dessiner de cadre
            // (cf. WpfMathAdapter qui le déballe en filet pour les cas nichés).
            // Ici on rend le contenu récursivement et on l'entoure d'un Border
            // WPF → aperçu fidèle au m:borderBox inséré dans Word (ADR
            // 2026-06-20 boxed). Pleine formule seulement : un \boxed inline
            // (a+\boxed{x}) retombe sur le déballage WpfMathAdapter.
            string boxedInner = WholeBoxedInner(latex);
            if (boxedInner != null)
                return WrapInBorder(Render(boxedInner, scale), scale);

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

        /// <summary>Si <paramref name="latex"/> est exactement
        /// <c>\boxed{…}</c> (le cadre enveloppe TOUTE la formule), renvoie le
        /// contenu intérieur ; sinon null. Appariement d'accolades (le contenu
        /// peut être imbriqué : <c>\boxed{\frac{1}{x}}</c>).</summary>
        private static string WholeBoxedInner(string latex)
        {
            const string tok = "\\boxed{";
            if (!latex.StartsWith(tok, System.StringComparison.Ordinal)) return null;
            int open = tok.Length - 1; // l'accolade ouvrante
            int depth = 0;
            for (int j = open; j < latex.Length; j++)
            {
                if (latex[j] == '{') depth++;
                else if (latex[j] == '}' && --depth == 0)
                    return j == latex.Length - 1
                        ? latex.Substring(open + 1, j - open - 1)
                        : null; // du contenu suit le } → pas pleine formule
            }
            return null; // déséquilibré
        }

        /// <summary>Entoure un élément rendu d'un cadre fin (aperçu du
        /// m:borderBox). Padding proportionnel au scale pour un visuel
        /// cohérent quelle que soit la taille du popup.</summary>
        private static UIElement WrapInBorder(UIElement inner, double scale)
        {
            var border = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(scale * 0.35, scale * 0.18, scale * 0.35, scale * 0.18),
                Child = inner,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                SnapsToDevicePixels = true,
            };
            return border;
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
