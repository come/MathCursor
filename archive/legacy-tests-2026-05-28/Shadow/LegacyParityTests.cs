using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Engine.Tests.Shadow
{
    /// <summary>
    /// P23.4 (2026-05-22) : harness de cas réels du legacy pour mesurer la
    /// couverture Engine v2. Ne demande pas la parité exacte (= legacy faisait
    /// différemment) mais valide que les inputs typiques retournent un
    /// rendu sensé et non vide.
    /// </summary>
    public class LegacyParityTests
    {
        private readonly ITestOutputHelper _output;

        public LegacyParityTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public static System.Collections.Generic.IEnumerable<object[]> LegacyInputs()
        {
            // Cas typiques copiés du corpus legacy (= ce que les users tapent).
            // Format : { input, contains_substring_attendu }
            yield return new object[] { "lim x 0 f(x)", @"\lim_{x \to 0}" };
            yield return new object[] { "sum k 1 n k^2", @"\sum_{k=1}^{n}" };
            yield return new object[] { "prod k 1 n k", @"\prod_{k=1}^{n}" };
            yield return new object[] { "int x 0 1 x^2", @"\int_{0}^{1}" };
            yield return new object[] { "derive x x^2+1", @"\frac{d}{dx}" };
            yield return new object[] { "forall x R P(x)", @"\forall x" };
            yield return new object[] { "exists y N y>0", @"\exists y" };
            yield return new object[] { "frac a b", @"\frac{a}{b}" };
            yield return new object[] { "frac 1 2", @"\frac{1}{2}" };
            yield return new object[] { "sqrt x", @"\sqrt{x}" };
            yield return new object[] { "sqrt (1+x^2)", @"\sqrt{1+x^2}" };
            yield return new object[] { "vec u", @"\vec{u}" };
            yield return new object[] { "vec AB", @"\vec{AB}" };
            yield return new object[] { "f'", "f'" };
            yield return new object[] { "f''", "f''" };
            yield return new object[] { "f'(x)", "f'(x)" };
            yield return new object[] { "P(A)", "P(A)" };
            yield return new object[] { "(AB) // (CD)", @"\mathbin{/\!/}" };
            yield return new object[] { "[0,1]", "[0,1]" };
            yield return new object[] { "a+b", "a+b" };
            yield return new object[] { "1/x", @"\frac{1}{x}" };
            yield return new object[] { "x^2", "x^2" };
            yield return new object[] { "x_i", "x_i" };
            yield return new object[] { "R", @"\mathbb{R}" };
            yield return new object[] { "lim x 0 1/x+1", @"\frac{1}{x}+1" };
            yield return new object[] { "sum i 1 n 1/i", @"\frac{1}{i}" };
        }

        [Theory]
        [MemberData(nameof(LegacyInputs))]
        public void Legacy_input_yields_expected_substring(string input, string expectedSubstring)
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve(input);
            _output.WriteLine($"input='{input}' top='{r.TopLatex}'");
            Assert.NotNull(r.TopLatex);
            Assert.NotEmpty(r.TopLatex);
            Assert.Contains(expectedSubstring, r.TopLatex);
        }
    }
}
