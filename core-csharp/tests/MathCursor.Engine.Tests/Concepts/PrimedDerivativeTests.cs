using Xunit;

namespace MathCursor.Engine.Tests.Concepts
{
    /// <summary>
    /// Tests P21 : dérivées primées Lagrange `f'`, `f''`, `f"`. Pas de YAML —
    /// absorbées par le tokenizer comme suffixe d'un Word d'1 lettre.
    /// Variants Unicode normalisés vers ASCII `'`.
    /// </summary>
    public class PrimedDerivativeTests
    {
        [Fact]
        public void Single_prime()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("f'");
            Assert.Equal("f'", r.TopLatex);
        }

        [Fact]
        public void Double_prime()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("f''");
            Assert.Equal("f''", r.TopLatex);
        }

        [Fact]
        public void Guillemet_double_equals_two_primes()
        {
            // f" (= guillemet ASCII U+0022) → f'' (= 2 primes LaTeX)
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("f\"");
            Assert.Equal("f''", r.TopLatex);
        }

        [Fact]
        public void Word_autocorrect_typographic_apostrophe()
        {
            // Word convertit ' en ’ (U+2019). Doit être normalisé en '.
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("f’");
            Assert.Equal("f'", r.TopLatex);
        }

        [Fact]
        public void Primed_function_call()
        {
            // f'(x) → f'(x) (= application primed à un argument)
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("f'(x)");
            Assert.Equal("f'(x)", r.TopLatex);
        }
    }
}
