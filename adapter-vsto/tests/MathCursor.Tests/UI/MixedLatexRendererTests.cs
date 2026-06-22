using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MathCursor.UI;
using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Tests.UI
{
    public class MixedLatexRendererTests
    {
        private readonly ITestOutputHelper _output;
        public MixedLatexRendererTests(ITestOutputHelper output) { _output = output; }

        // ===== Tokenization (rapide, pas de WPF) =====

        [Fact]
        public void Tokenize_Empty_NoSegments()
        {
            Assert.Empty(MixedLatexRenderer.Tokenize(""));
            Assert.Empty(MixedLatexRenderer.Tokenize(null));
        }

        [Fact]
        public void Tokenize_PlainLatex_SingleWpfMathSegment()
        {
            var segs = MixedLatexRenderer.Tokenize(@"\frac{1}{2}");
            Assert.Single(segs);
            Assert.Equal(MixedLatexRenderer.SegmentType.WpfMath, segs[0].Type);
            Assert.Equal(@"\frac{1}{2}", segs[0].Content);
        }

        [Theory]
        [InlineData(@"\mathbb{R}", "ℝ")]
        [InlineData(@"\mathbb{N}", "ℕ")]
        [InlineData(@"\mathbb{Z}", "ℤ")]
        [InlineData(@"\mathbb{Q}", "ℚ")]
        [InlineData(@"\mathbb{C}", "ℂ")]
        [InlineData(@"\mathbb{P}", "ℙ")]
        [InlineData(@"\mapsto", "↦")]
        [InlineData(@"\iint", "∬")]
        [InlineData(@"\iiint", "∭")]
        [InlineData(@"\mathbin{/\!/}", "⫽")]
        public void Tokenize_SingleUnicodeMacro_OneUnicodeSegment(string latex, string expected)
        {
            var segs = MixedLatexRenderer.Tokenize(latex);
            Assert.Single(segs);
            Assert.Equal(MixedLatexRenderer.SegmentType.Unicode, segs[0].Type);
            Assert.Equal(expected, segs[0].Content);
        }

        [Fact]
        public void Tokenize_Paralleles_ObliqueUnicode()
        {
            // « AB // CD » : le ⫽ (U+2AFD) penché, PAS le ∥ vertical de
            // l'ancienne substitution WpfMathAdapter (retour user 2026-06-10).
            var segs = MixedLatexRenderer.Tokenize(@"AB \mathbin{/\!/} CD");
            Assert.Equal(3, segs.Count);
            Assert.Equal(MixedLatexRenderer.SegmentType.Unicode, segs[1].Type);
            Assert.Equal("⫽", segs[1].Content);
        }

        [Fact]
        public void Tokenize_MacroNichee_ResteDansLeSegmentWpfMath()
        {
            // \mathbb{Z} NICHÉ dans \overline{…} : l'extraire couperait le
            // groupe en accolades orphelines — il reste dans le segment
            // WpfMath (subst dégradée |Z de WpfMathAdapter). Audit conjZ.
            var segs = MixedLatexRenderer.Tokenize(@"\overline{\mathbb{Z} }");
            Assert.Single(segs);
            Assert.Equal(MixedLatexRenderer.SegmentType.WpfMath, segs[0].Type);
        }

        [Fact]
        public void Tokenize_RcapN_ThreeSegments()
        {
            var segs = MixedLatexRenderer.Tokenize(@"\mathbb{R} \cap \mathbb{N}");
            Assert.Equal(3, segs.Count);
            Assert.Equal(MixedLatexRenderer.SegmentType.Unicode, segs[0].Type);
            Assert.Equal("ℝ", segs[0].Content);
            Assert.Equal(MixedLatexRenderer.SegmentType.WpfMath, segs[1].Type);
            Assert.Equal(@" \cap ", segs[1].Content);
            Assert.Equal(MixedLatexRenderer.SegmentType.Unicode, segs[2].Type);
            Assert.Equal("ℕ", segs[2].Content);
        }

        [Fact]
        public void Tokenize_IiintNotMatchedAsIint()
        {
            // Vérifie que `\iiint` n'est pas tokenizé comme `\iint` + `iint`.
            var segs = MixedLatexRenderer.Tokenize(@"\iiint");
            Assert.Single(segs);
            Assert.Equal("∭", segs[0].Content);
        }

        [Fact]
        public void Tokenize_MapstoLookahead_DoesNotEatPrefix()
        {
            // `\mapstoX` ne doit PAS matcher `\mapsto` (qui consommerait le \).
            // Idéalement WpfMath rendra `\mapstoX` (commande inconnue) — peu
            // importe ici, on vérifie juste que le tokenizer le laisse passer.
            var segs = MixedLatexRenderer.Tokenize(@"\mapstoX");
            Assert.Single(segs);
            Assert.Equal(MixedLatexRenderer.SegmentType.WpfMath, segs[0].Type);
        }

        [Fact]
        public void Tokenize_FunctionMapsto_TwoWpfMathOneUnicode()
        {
            var segs = MixedLatexRenderer.Tokenize(@"f : x \mapsto x^2");
            Assert.Equal(3, segs.Count);
            Assert.Equal(MixedLatexRenderer.SegmentType.WpfMath, segs[0].Type);
            Assert.Equal("f : x ", segs[0].Content);
            Assert.Equal(MixedLatexRenderer.SegmentType.Unicode, segs[1].Type);
            Assert.Equal("↦", segs[1].Content);
            Assert.Equal(MixedLatexRenderer.SegmentType.WpfMath, segs[2].Type);
            Assert.Equal(" x^2", segs[2].Content);
        }

        // ===== Rendu dimensionnel (STA, render PNG dans .render-probes/mixed/) =====
        //
        // Discriminant : un rendu placeholder WpfMath donne 50×50/207 bytes
        // pile poil (cf. brief 2026-05-06, signal "."). On asserte que chaque
        // cas produit une image au-dessus de ces seuils. Si un test devient
        // rouge → soit régression placeholder, soit ajustement légitime du
        // rendu (réviser les seuils).

        // Seuils universels juste au-dessus du placeholder WpfMath (50×50,
        // 207 bytes) avec une petite marge. Discriminant suffisant pour
        // détecter une régression "macro Unicode pas rendue" sans être
        // fragile aux ajustements de mise en page WpfMath.
        private const int MinWidth = 60;
        private const int MinBytes = 300;

        public static TheoryData<string, string> RenderCases = new TheoryData<string, string>
        {
            // (label, latex)
            { "00-frac-baseline",  @"\frac{1}{2}" },
            // Macros substituées en TextBlock Unicode :
            { "10-mathbbR",        @"\mathbb{R}" },
            { "11-mathbbN",        @"\mathbb{N}" },
            { "12-mathbbZ",        @"\mathbb{Z}" },
            { "20-RcapN",          @"\mathbb{R} \cap \mathbb{N}" },
            { "21-RcapNcupZ",      @"\mathbb{R} \cap \mathbb{N} \cup \mathbb{Z}" },
            { "30-mapsto",         @"f : x \mapsto x^2" },
            { "40-iint",           @"\iint" },
            { "41-iiint",          @"\iiint" },
            // Macros WpfMath-safe : on s'assure que le pipeline mixed ne les
            // a pas cassées (fast-path FormulaControl direct).
            { "50-setminus",       @"A \setminus B" },
            { "51-widehat-multi",  @"\widehat{ABC}" },
            { "52-overline",       @"\overline{x+y}" },
            { "53-oint",           @"\oint" },
            // \boxed : cadre WPF (Border) autour du contenu rendu — l'aperçu
            // doit montrer le cadre que Word recevra en m:borderBox.
            { "60-boxed",          @"\boxed{x=2}" },
            { "61-boxed-frac",     @"\boxed{\frac{1}{x}}" },
        };

        [Theory]
        [MemberData(nameof(RenderCases))]
        public void Render_NotPlaceholder(string label, string latex)
        {
            var outDir = GetOutputDir();
            Directory.CreateDirectory(outDir);
            var path = Path.Combine(outDir, label + ".png");

            int width = 0, height = 0;
            StaRunner.Run(() =>
            {
                var element = MixedLatexRenderer.Render(latex, 24);
                var border = new Border
                {
                    Background = Brushes.White,
                    Padding = new Thickness(24),
                    Child = element,
                };
                border.Measure(new Size(800, 300));
                border.Arrange(new Rect(border.DesiredSize));
                border.UpdateLayout();

                width  = (int)Math.Max(1, Math.Ceiling(border.ActualWidth));
                height = (int)Math.Max(1, Math.Ceiling(border.ActualHeight));

                var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(border);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                    encoder.Save(fs);
            });

            var bytes = new FileInfo(path).Length;
            _output.WriteLine(string.Format(
                "{0,-22} {1,4}x{2,-4} {3,6} bytes  {4}",
                label, width, height, bytes, path));

            Assert.True(width >= MinWidth,
                $"Width {width} < {MinWidth} (placeholder ?) — {path}");
            Assert.True(bytes >= MinBytes,
                $"Bytes {bytes} < {MinBytes} (placeholder ?) — {path}");
        }

        private static string GetOutputDir()
        {
            var bin = AppDomain.CurrentDomain.BaseDirectory;
            // adapter-vsto/tests/MathCursor.Tests/bin/Debug/net48 → repo root (6 niveaux)
            var repoRoot = Path.GetFullPath(Path.Combine(bin, "..", "..", "..", "..", "..", ".."));
            return Path.Combine(repoRoot, ".render-probes", "mixed");
        }
    }
}
