using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Engine.Tests.Concepts
{
    /// <summary>
    /// P29 : valider que TOUTES les ancres YAML (norm, lim, sum, frac, sqrt,
    /// vec, ...) fonctionnent **partout** dans une expression composée, pas
    /// seulement au top-level. C'est la promesse du brief v5 §6 (génériques).
    /// </summary>
    public class AnchorInExpressionTests
    {
        private readonly ITestOutputHelper _output;

        public AnchorInExpressionTests(ITestOutputHelper output) { _output = output; }

        [Fact]
        public void Norm_inside_equation()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("A=norm u");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.Equal(@"A = \|u\|", r.TopLatex);
        }

        [Fact]
        public void Frac_inside_addition()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("1+frac a b");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.Equal(@"1+\frac{a}{b}", r.TopLatex);
        }

        [Fact]
        public void Sqrt_inside_equation()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("x=sqrt 2");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.Equal(@"x = \sqrt{2}", r.TopLatex);
        }

        [Fact]
        public void Vec_inside_addition()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("vec u+vec v");
            _output.WriteLine($"top='{r.TopLatex}'");
            Assert.Equal(@"\vec{u}+\vec{v}", r.TopLatex);
        }
    }
}
