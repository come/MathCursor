using System;
using System.Windows;
using System.Windows.Media;
using WpfMath.Controls;
using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Tests.UI
{
    /// <summary>
    /// Probe-tests Étape 1 (cf. brief 2026-05-06-wpfmath-fallback-renderer.md).
    /// On instancie un <c>FormulaControl</c> avec différentes combinaisons
    /// (LaTeX × FontFamily) et on force layout. Aucune exception levée =
    /// WpfMath a accepté le syntaxe — ne dit RIEN sur la qualité du glyphe
    /// rendu (boîte vide silencieuse possible). Pour le visuel, voir Étape 2
    /// (render-to-PNG).
    /// </summary>
    public class WpfMathSmokeTests
    {
        private readonly ITestOutputHelper _output;
        public WpfMathSmokeTests(ITestOutputHelper output) { _output = output; }

        // Cas du brief : ensemble + opérateurs + témoin supportée. Le 1er
        // de chaque paire est ce qu'émet le core aujourd'hui ; le 2e est
        // la substitution Unicode envisagée pour Branche A.
        public static TheoryData<string, string> Cases = new TheoryData<string, string>
        {
            { "\\frac{1}{2}",        "Cambria Math" },   // témoin supportée
            { "\\mathbb{R}",         "Cambria Math" },   // baseline cassée
            { "\\text{ℝ}",           "Cambria Math" },   // Branche A candidat
            { "ℝ",                   "Cambria Math" },   // unicode brut
            { "\\setminus",          "Cambria Math" },
            { "\\text{∖}",           "Cambria Math" },
            { "\\mapsto",            "Cambria Math" },
            { "\\text{↦}",           "Cambria Math" },
            { "\\widehat{ABC}",      "Cambria Math" },   // multi-char widehat
            { "\\overline{x+y}",     "Cambria Math" },
            { "\\iint",              "Cambria Math" },
            { "\\limsup",            "Cambria Math" },
        };

        [Theory]
        [MemberData(nameof(Cases))]
        public void FormulaControl_Instantiates_WithoutException(string latex, string fontFamily)
        {
            var ex = StaRunner.Run<Exception>(() =>
            {
                try
                {
                    var fc = new FormulaControl
                    {
                        Formula = latex,
                        FontFamily = new FontFamily(fontFamily),
                        Scale = 18,
                    };
                    // Force le layout — WpfMath parse la formule lazy au
                    // premier Measure, donc sans ça les erreurs de parse
                    // ne remontent pas.
                    fc.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    fc.Arrange(new Rect(fc.DesiredSize));
                    return null;
                }
                catch (Exception e) { return e; }
            });

            if (ex != null)
            {
                _output.WriteLine($"LaTeX='{latex}' Font='{fontFamily}' →");
                _output.WriteLine($"  {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                    _output.WriteLine($"  inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            }
            Assert.Null(ex);
        }
    }
}
