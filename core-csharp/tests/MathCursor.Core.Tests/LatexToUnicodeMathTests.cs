using MathCursor.Core;
using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Core.Tests
{
    /// <summary>
    /// Vérifie le convertisseur LaTeX → UnicodeMath : le format consommé par
    /// <c>OMaths.BuildUp()</c> côté Word. Notre LaTeX interne doit être traduit
    /// en UnicodeMath pour que Word construise une équation rendue, pas du texte
    /// brut avec des backslashes.
    /// </summary>
    public sealed class LatexToUnicodeMathTests
    {
        private readonly ITestOutputHelper _log;
        public LatexToUnicodeMathTests(ITestOutputHelper log) { _log = log; }

        [Theory]
        [InlineData("\\frac{1}{2}", "(1)/(2)")]
        [InlineData("\\frac{a+b}{c-d}", "(a+b)/(c-d)")]
        [InlineData("\\sqrt{x}", "√(x)")]
        [InlineData("\\sqrt{x+1}", "√(x+1)")]
        [InlineData("x^{2}", "x^(2)")]
        [InlineData("x_{n}", "x_(n)")]
        [InlineData("\\forall x \\in \\mathbb{R}", "∀ x ∈ ℝ")]
        [InlineData("\\alpha + \\beta", "α + β")]
        [InlineData("\\lim_{x \\to 0}", "lim_(x → 0)")]
        [InlineData("\\lim_{x \\to 0^+} f(x) = \\frac{1}{(x+2)^2}", "lim_(x → 0^+) f(x) = (1)/((x+2)^2)")]
        [InlineData("\\binom{n}{k}", "(n¦k)")]
        [InlineData("\\operatorname{tr}(A)", "tr(A)")]
        [InlineData("\\mathrm{mod}", "mod")]
        [InlineData("\\sin^2(x)", "sin^2(x)")]
        // Accents : émis comme char + combining unicode DÉCOMPOSÉ (ce que Word
        // reconnaît dans son BuildUp — menu Accent du ribbon). On utilise des
        // escape sequences \u pour lever toute ambiguïté avec les caractères
        // précomposés (ẍ précomposé U+1E8D ≠ x + U+0308 décomposé).
        [InlineData("\\vec{AB}", "(AB)⃗")]            // Combining Right Arrow Above
        [InlineData("\\vec{u}", "u⃗")]
        [InlineData("\\hat{x}", "x̂")]                // Combining Circumflex
        [InlineData("\\widehat{ABC}", "(ABC)̂")]      // angle ABC
        [InlineData("\\bar{y}", "y̅")]                // Combining Overline
        [InlineData("\\overline{z}", "z̅")]
        [InlineData("\\dot{x}", "ẋ")]                // Combining Dot Above
        [InlineData("\\ddot{x}", "ẍ")]               // Combining Diaeresis
        [InlineData("\\tilde{a}", "ã")]              // Combining Tilde
        public void Converts_expected(string latex, string expected)
        {
            var got = LatexToUnicodeMath.Convert(latex);
            _log.WriteLine($"LaTeX: \"{latex}\"\n→ \"{got}\"\nexpected: \"{expected}\"");
            Assert.Equal(expected, got);
        }
    }
}
