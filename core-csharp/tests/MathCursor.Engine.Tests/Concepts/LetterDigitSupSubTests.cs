using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Engine.Tests.Concepts
{
    /// <summary>
    /// P26 (2026-05-22) : reproduit le comportement legacy "letter-sup-number".
    /// Quand on a une lettre seule suivie d'un chiffre collé (= `x2`, `e3`),
    /// le défaut est l'exposant <c>x^{2}</c> avec l'indice <c>x_{2}</c>
    /// comme alternative. Cf. LetterSupNumberPopupTests legacy.
    /// </summary>
    public class LetterDigitSupSubTests
    {
        private readonly ITestOutputHelper _output;

        public LetterDigitSupSubTests(ITestOutputHelper output)
        {
            _output = output;
        }

        // ─── Cas simples : lettre + chiffre ───────────────────────────

        [Fact]
        public void X2_default_exposant_alt_indice()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("x2");
            _output.WriteLine($"top='{r.TopLatex}' collisions={r.Collisions.Count}");
            Assert.Equal("x^{2}", r.TopLatex);
            Assert.Single(r.Collisions);
            Assert.Equal("x_{2}", r.Collisions[0].Latex);
        }

        [Fact]
        public void E3_yields_e_exp_3_and_e_sub_3()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("e3");
            Assert.Equal("e^{3}", r.TopLatex);
            Assert.Single(r.Collisions);
            Assert.Equal("e_{3}", r.Collisions[0].Latex);
        }

        [Fact]
        public void Y12_multi_digit_number_works()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("y12");
            Assert.Equal("y^{12}", r.TopLatex);
            Assert.Single(r.Collisions);
            Assert.Equal("y_{12}", r.Collisions[0].Latex);
        }

        // ─── Pas de sup/sub si lettre déjà longue (= mot, pas une variable) ─

        [Fact]
        public void Cos2_does_not_trigger_letter_digit_sup_sub()
        {
            // `cos2` : "cos" est tokenisé en function (= text "\cos"). Pas
            // une simple lettre. Pas de sup/sub auto.
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("cos2");
            _output.WriteLine($"top='{r.TopLatex}'");
            // Doit rester un produit (ou similaire), pas un sup/sub auto.
            Assert.DoesNotContain("^{2}", r.TopLatex);
            Assert.DoesNotContain("_{2}", r.TopLatex);
        }

        // ─── 2 lettres adjacentes = géométrie, pas sup/sub ────────────

        [Fact]
        public void AB_two_uppercase_no_sup_sub()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("AB");
            Assert.Equal("AB", r.TopLatex);
            // Pas de collision sup/sub (= AB = géométrie, pas A^B).
            // Mais P27 ajoute une collision vec, donc on filtre.
            Assert.DoesNotContain(r.Collisions, c => c.Latex.Contains("^{") || c.Latex.Contains("_{"));
        }

        // ─── Exposant explicite via ^ continue de marcher ─────────────

        [Fact]
        public void Explicit_caret_continues_to_work()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("x^2");
            Assert.Equal("x^2", r.TopLatex);
        }

        [Fact]
        public void Explicit_underscore_continues_to_work()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("x_2");
            Assert.Equal("x_2", r.TopLatex);
        }

        // ─── Composition avec fraction / autres ───────────────────────

        [Fact]
        public void X2_inside_addition_still_sup_sub()
        {
            // `x2+1` : x2 reste un sup/sub (= x^{2}+1 par défaut).
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("x2+1");
            _output.WriteLine($"top='{r.TopLatex}' collisions={r.Collisions.Count}");
            Assert.Contains("x^{2}", r.TopLatex);
        }

        // ─── x2 puis y2 = 2 sup/sub indépendants ──────────────────────

        [Fact]
        public void X2_plus_y2_two_independent_supsubs()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("x2+y2");
            _output.WriteLine($"top='{r.TopLatex}' collisions={r.Collisions.Count}");
            Assert.Contains("x^{2}", r.TopLatex);
            Assert.Contains("y^{2}", r.TopLatex);
        }
    }
}
