using MathCursor.Core;
using MathCursor.Engine;
using MathCursor.Engine.Adapter;
using Xunit;

namespace MathCursor.Engine.Adapter.Tests
{
    /// <summary>
    /// Harnais e2e <b>headless</b> (= sans Word) de la chaîne réellement
    /// utilisée par l'add-in VSTO :
    /// <code>
    /// texte → EngineZoneSource.TryResolve → ResolvedZone.TopLatex → LatexToUnicodeMath.Convert → UnicodeMath
    /// </code>
    /// On asserte la sortie <b>UnicodeMath</b> (= ce que Word reçoit avant
    /// <c>OMaths.BuildUp()</c>), pas seulement le LaTeX intermédiaire. Tout
    /// écart entre le vocabulaire LaTeX émis par le moteur V2 et la couverture
    /// de <see cref="LatexToUnicodeMath"/> devient un échec ROUGE ici, et non
    /// un OMath vide/cassé observé tardivement dans Word.
    ///
    /// <para>Cf. ADR 2026-05-29-Test-engine-adapter-e2e-headless.</para>
    /// </summary>
    public class EngineToUnicodeMathE2eTests
    {
        private static readonly EngineZoneSource _source =
            new EngineZoneSource(MathEngine.BuildDefault("fr"));

        [Theory]
        // ─── Intervalles : `,` ou `;` en séparateur → `;`, bornes ouvertes/fermées ──
        [InlineData("[0,1]", "[0;1]")]
        [InlineData("[0 ; 1]", "[0;1]")]
        [InlineData("]0 ; 1[", "]0;1[")]
        [InlineData("[0 ; 1[", "[0;1[")]
        // ─── Ensembles + union / intersection / différence ──────────────
        [InlineData("{0 ; 1}", "{0;1}")]
        [InlineData("R \\ {0}", "ℝ ∖ {0}")]
        [InlineData("R union [0 ; 1]", "ℝ ∪ [0;1]")]
        [InlineData("[0 ; 1] U [2 ; 3]", "[0;1] ∪ [2;3]")]
        // ─── Matrices (■ = marqueur matrice UnicodeMath, & colonnes, @ lignes) ──
        [InlineData("(1 2 ; 3 4)", "(■(1&2@3&4))")]
        [InlineData("[1 2 ; 3 4]", "[■(1&2@3&4)]")]
        [InlineData("(1 ; 2 ; 3)", "(■(1@2@3))")]
        // ─── Fractions (single-char nu vs multi-char parenthésé) ────────
        [InlineData("frac 1 2", "1/2")]
        [InlineData("frac (x+1) (x-1)", "(x+1)/(x-1)")]
        [InlineData("frac n n+1", "n/(n+1)")]
        // ─── Racines (carrée vs n-ième via √(n&x)) ──────────────────────
        [InlineData("sqrt 2", "√(2)")]
        [InlineData("sqrt 3 8", "√(3&8)")]
        [InlineData("sqrt (x+1)", "√(x+1)")]
        // ─── Sommes (composition récursive du corps) ────────────────────
        [InlineData("sum k 1 n k", "∑_(k=1)^n k")]
        [InlineData("sum k=1 n (1/k)", "∑_(k=1)^n 1/k")]
        // ─── Intégrales : ∫ + dx deviné. NB : triple espace autour du `\,`
        //     (artefact cosmétique pré-existant de LatexToUnicodeMath, tassé
        //     par Word au rendu) — verrouillé tel quel. ───────────────────
        [InlineData("int x 0 1 x^2", "∫_0^1 x^2   dx")]
        [InlineData("int t 0 +oo f(t)", "∫_0^(+∞) f(t)   dt")]
        // ─── Limites, vecteurs, décimales ───────────────────────────────
        [InlineData("lim x->0 f(x)", "lim_(x → 0) f(x)")]
        [InlineData("vec AB", "(AB)⃗")]
        [InlineData("3,14", "3,14")]
        [InlineData("5+0,5", "5+0,5")]
        public void Text_resolves_to_expected_unicodemath(string input, string expected)
        {
            var zone = _source.TryResolve(input, out _);
            Assert.NotNull(zone);
            var unicode = LatexToUnicodeMath.Convert(zone!.TopLatex);
            Assert.Equal(expected, unicode);
        }
    }
}
