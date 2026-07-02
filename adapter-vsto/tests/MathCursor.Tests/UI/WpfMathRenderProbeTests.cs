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
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfMath.Controls;
using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Tests.UI
{
    /// <summary>
    /// Probe-tests Étape 2 (cf. brief 2026-05-06-wpfmath-fallback-renderer.md).
    /// Render chaque cas dans un PNG sur disque pour inspection visuelle.
    /// Permet de décider Branche A (Cambria Math + substitution Unicode suffit)
    /// vs Branche B (archi 3 classes nécessaire) sans ouvrir Word.
    ///
    /// Les PNG sont écrits dans &lt;repo-root&gt;/.render-probes/. Chaque test
    /// log son chemin absolu via ITestOutputHelper.
    /// </summary>
    public class WpfMathRenderProbeTests
    {
        private readonly ITestOutputHelper _output;
        public WpfMathRenderProbeTests(ITestOutputHelper output) { _output = output; }

        // (label, latex, fontFamily). Le label sert au nom du fichier PNG.
        public static TheoryData<string, string, string> Cases = new TheoryData<string, string, string>
        {
            // Témoin macro déjà supportée — sanity check du pipeline de rendu.
            { "00-baseline-frac",         "\\frac{1}{2}",       "Cambria Math" },

            // Ensembles : la question centrale du brief — ℝ avec double barre ?
            { "10-mathbb-R-raw",          "\\mathbb{R}",        "Cambria Math" },
            { "11-mathbb-R-text-unicode", "\\text{ℝ}",          "Cambria Math" },
            { "12-unicode-R-bare",        "ℝ",                  "Cambria Math" },
            { "13-mathbb-N-raw",          "\\mathbb{N}",        "Cambria Math" },
            { "14-mathbb-N-text-unicode", "\\text{ℕ}",          "Cambria Math" },
            { "15-mathbb-Z-raw",          "\\mathbb{Z}",        "Cambria Math" },
            { "16-mathbb-Z-text-unicode", "\\text{ℤ}",          "Cambria Math" },

            // Cas mixte : intersection ensembles (cas réel émis par le core).
            { "20-mixed-R-cap-N-raw",     "\\mathbb{R} \\cap \\mathbb{N}",         "Cambria Math" },
            { "21-mixed-R-cap-N-text",    "\\text{ℝ} \\cap \\text{ℕ}",             "Cambria Math" },

            // Opérateurs.
            { "30-setminus-raw",          "A \\setminus B",     "Cambria Math" },
            { "31-setminus-text-unicode", "A \\text{∖} B",      "Cambria Math" },
            { "32-mapsto-raw",            "f : x \\mapsto x^2", "Cambria Math" },
            { "33-mapsto-text-unicode",   "f : x \\text{↦} x^2","Cambria Math" },

            // Décorations multi-char.
            { "40-widehat-multi",         "\\widehat{ABC}",     "Cambria Math" },
            { "41-overline-multi",        "\\overline{x+y}",    "Cambria Math" },

            // Symboles intégrale double / contour.
            { "50-iint",                  "\\iint",             "Cambria Math" },
            { "51-oint",                  "\\oint",             "Cambria Math" },

            // Limites.
            { "60-limsup",                "\\limsup_{n\\to\\infty} a_n",   "Cambria Math" },
            { "61-liminf",                "\\liminf_{n\\to\\infty} a_n",   "Cambria Math" },

            // Norme : \| parse mais rend une barre SIMPLE — \Vert (la subst
            // WpfMathAdapter) doit rendre la double (retour user 2026-06-10).
            { "70-norm-backslash-pipe",   "\\left\\|AB\\right\\|",         "Cambria Math" },
            { "71-norm-vert",             "\\left\\Vert AB\\right\\Vert ", "Cambria Math" },
        };

        // Cas TextBlock pur (bypass WpfMath) pour vérifier si Cambria Math
        // sait afficher les caractères Unicode math hors WpfMath.
        public static TheoryData<string, string, string> TextBlockCases = new TheoryData<string, string, string>
        {
            { "tb-00-cambria-R",          "ℝ",                  "Cambria Math" },
            { "tb-01-cambria-set",        "ℝ ∩ ℕ ∖ ℤ",          "Cambria Math" },
            { "tb-02-cambria-arrows",     "f : x ↦ x²",         "Cambria Math" },
            { "tb-03-segoe-R",            "ℝ",                  "Segoe UI Symbol" },
            { "tb-04-default-R",          "ℝ",                  "Segoe UI" },
        };

        [Theory]
        [MemberData(nameof(TextBlockCases))]
        public void Render_TextBlock_ToPng(string label, string text, string fontFamily)
        {
            var outDir = GetOutputDir();
            Directory.CreateDirectory(outDir);
            var path = Path.Combine(outDir, label + ".png");

            int renderWidth = 0, renderHeight = 0;
            StaRunner.Run(() =>
            {
                var tb = new TextBlock
                {
                    Text = text,
                    FontFamily = new FontFamily(fontFamily),
                    FontSize = 36,
                    Foreground = Brushes.Black,
                };
                var border = new Border
                {
                    Background = Brushes.White,
                    Padding = new Thickness(24),
                    Child = tb,
                };
                border.Measure(new Size(600, 300));
                border.Arrange(new Rect(border.DesiredSize));
                border.UpdateLayout();

                var width  = (int)Math.Max(1, Math.Ceiling(border.ActualWidth));
                var height = (int)Math.Max(1, Math.Ceiling(border.ActualHeight));
                renderWidth = width;
                renderHeight = height;

                var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(border);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                    encoder.Save(fs);
            });

            Assert.True(File.Exists(path), "PNG non écrit : " + path);
            var bytes = new FileInfo(path).Length;
            Assert.True(bytes > 0, "PNG vide : " + path);
            _output.WriteLine(string.Format(
                "{0,-30} {1,4}x{2,-4} {3,6} bytes  {4}",
                label, renderWidth, renderHeight, bytes, path));
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Render_ToPng(string label, string latex, string fontFamily)
        {
            var outDir = GetOutputDir();
            Directory.CreateDirectory(outDir);
            var path = Path.Combine(outDir, label + ".png");

            int renderWidth = 0, renderHeight = 0;
            StaRunner.Run(() =>
            {
                var fc = new FormulaControl
                {
                    Formula = latex,
                    FontFamily = new FontFamily(fontFamily),
                    Scale = 24,
                    Foreground = Brushes.Black,
                };

                // Forcer le template et le layout. WpfMath parse au premier
                // Measure ; sans Border + UpdateLayout le rendu est souvent
                // vide car le visual n'a pas eu son template appliqué.
                var border = new Border
                {
                    Background = Brushes.White,
                    Padding = new Thickness(24),
                    Child = fc,
                };

                // Mesurer / arranger comme si la fenêtre faisait 600x300.
                border.Measure(new Size(600, 300));
                border.Arrange(new Rect(border.DesiredSize));
                border.UpdateLayout();

                var width  = (int)Math.Max(1, Math.Ceiling(border.ActualWidth));
                var height = (int)Math.Max(1, Math.Ceiling(border.ActualHeight));
                renderWidth = width;
                renderHeight = height;

                var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(border);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                    encoder.Save(fs);
            });

            Assert.True(File.Exists(path), "PNG non écrit : " + path);
            var bytes = new FileInfo(path).Length;
            Assert.True(bytes > 0, "PNG vide : " + path);
            _output.WriteLine(string.Format(
                "{0,-30} {1,4}x{2,-4} {3,6} bytes  {4}",
                label, renderWidth, renderHeight, bytes, path));
        }

        /// <summary>
        /// &lt;repo-root&gt;/.render-probes/. Résolu depuis le BaseDirectory de
        /// l'assembly de test (5 niveaux de remontée :
        /// <c>adapter-vsto/tests/MathCursor.Tests/bin/Debug/net48</c>).
        /// </summary>
        private static string GetOutputDir()
        {
            var bin = AppDomain.CurrentDomain.BaseDirectory;
            var repoRoot = Path.GetFullPath(Path.Combine(bin, "..", "..", "..", "..", "..", ".."));
            return Path.Combine(repoRoot, ".render-probes");
        }
    }
}
