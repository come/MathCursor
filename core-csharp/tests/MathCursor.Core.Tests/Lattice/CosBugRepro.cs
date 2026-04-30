using MathCursor.Core;
using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Core.Tests.Lattice
{
    /// <summary>
    /// Tests de régression pour le bug user 2026-04-30 sur les fonctions
    /// trigo : `Cos2(x)+sin2(x)/4` rendu en `cos(2(x)+sin(2(x)/4))` au lieu de
    /// `\cos(x)^2 + \sin(x)^2/4`.
    ///
    /// Cf. ADR 2026-04-30-Fix-trig-func-power-tight-arg.
    ///
    /// Le `Func` ne doit consommer comme argument que ce qui suit son
    /// éventuel exposant tight (`Number Group`), pas la suite ouverte par
    /// `+`/`/` qui est tagged tight par le Lexer entre `)` et le caractère
    /// suivant.
    /// </summary>
    public sealed class CosBugRepro
    {
        private static readonly LatticeEngine _engine = new LatticeEngine();
        private readonly ITestOutputHelper _log;
        public CosBugRepro(ITestOutputHelper log) { _log = log; }

        // ---- Cas isolés (déjà OK avant fix, gardent leur comportement) ----

        [Fact]
        public void Cos2_paren_x_alone_yields_cos_x_squared()
        {
            var r = _engine.ConvertWithAmbiguity("Cos2(x)");
            _log.WriteLine($"\"Cos2(x)\" → \"{r.TopLatex}\"");
            Assert.Equal("\\cos\\left(x\\right)^{2}", r.TopLatex);
        }

        [Fact]
        public void Cos_paren_x_alone_yields_simple_call()
        {
            var r = _engine.ConvertWithAmbiguity("cos(x)");
            Assert.Equal("\\cos\\left(x\\right)", r.TopLatex);
        }

        // ---- Bug du jour (rouges avant fix) ----

        [Fact]
        public void Cos2_paren_x_plus_one_does_not_absorb_suffix()
        {
            // Bug : `Cos2(x)+1` rendait `\cos\left(2\left(x\right)+1\right)`.
            // Attendu : la fonction s'arrête après `(x)`, le `+1` reste dehors.
            var r = _engine.ConvertWithAmbiguity("Cos2(x)+1");
            _log.WriteLine($"\"Cos2(x)+1\" → \"{r.TopLatex}\"");
            Assert.Equal("\\cos\\left(x\\right)^{2}+1", r.TopLatex);
        }

        [Fact]
        public void Sin2_paren_x_div_4_does_not_absorb_division()
        {
            // Bug : `sin2(x)/4` rendait `\sin\left(\frac{2\left(x\right)}{4}\right)`.
            // Attendu : `sin` retourne `\sin(x)^2`, le `/4` reste dehors et
            // le LatexRenderer émet une fraction (cf. division → \frac).
            var r = _engine.ConvertWithAmbiguity("sin2(x)/4");
            _log.WriteLine($"\"sin2(x)/4\" → \"{r.TopLatex}\"");
            Assert.Equal("\\frac{\\sin\\left(x\\right)^{2}}{4}", r.TopLatex);
        }

        [Fact]
        public void Trig_identity_divided_full_form()
        {
            // Combinaison du bug : `Cos2(x)+sin2(x)/4`. Attendu : identité trig
            // `\cos(x)^2 + \sin(x)^2 / 4` (la division ne capte que le sin²).
            // Précédence : `+` est plus loose que `/`, donc `+` au top.
            var r = _engine.ConvertWithAmbiguity("Cos2(x)+sin2(x)/4");
            _log.WriteLine($"\"Cos2(x)+sin2(x)/4\" → \"{r.TopLatex}\"");
            Assert.Equal("\\cos\\left(x\\right)^{2}+\\frac{\\sin\\left(x\\right)^{2}}{4}", r.TopLatex);
        }

        [Fact]
        public void Cos2_paren_x_times_y_keeps_implicit_mult_after()
        {
            // `cos2(x)*y` → la mult `*y` est tight après `)` mais doit rester
            // au top, pas dans l'arg de cos.
            var r = _engine.ConvertWithAmbiguity("cos2(x)*y");
            _log.WriteLine($"\"cos2(x)*y\" → \"{r.TopLatex}\"");
            // \cos(x)^2 \cdot y (mult explicite rendue \cdot)
            Assert.Contains("\\cos\\left(x\\right)^{2}", r.TopLatex);
            Assert.DoesNotContain("\\cos\\left(2", r.TopLatex);  // pas d'absorption
        }

        // ---- Cas non-tight : pas de power, fallback flow normal ----

        [Fact]
        public void Cos_space_2_paren_x_also_yields_power_via_legacy_remap()
        {
            // `cos 2(x)` (avec espace) : ParseTightChain absorbe `2(x)` en
            // Bin(*, 2, (x)) (ParseTightChain regarde l'adjacence tight DANS
            // sa boucle interne, pas avec le token précédent — l'espace entre
            // cos et 2 ne bloque rien). Le remap historique
            // `Sup(Func, Number)` fire → `\cos(x)^2`. Comportement présent
            // avant le fix, conservé après.
            var r = _engine.ConvertWithAmbiguity("cos 2(x)");
            _log.WriteLine($"\"cos 2(x)\" → \"{r.TopLatex}\"");
            Assert.Equal("\\cos\\left(x\\right)^{2}", r.TopLatex);
        }

        [Fact]
        public void Cos2x_no_parens_keeps_legacy_behavior()
        {
            // `cos2x` : le `2` est tight mais suivi d'un Ident (pas d'un
            // Group). Le pattern strict ne fire pas → flow normal → arg = 2x.
            // Comportement pré-existant, pas modifié.
            var r = _engine.ConvertWithAmbiguity("cos2x");
            _log.WriteLine($"\"cos2x\" → \"{r.TopLatex}\"");
            // L'important : pas de régression sur ce flow.
            Assert.Contains("\\cos", r.TopLatex);
        }
    }
}
