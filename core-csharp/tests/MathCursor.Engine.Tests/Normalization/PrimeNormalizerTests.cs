using Xunit;

namespace MathCursor.Engine.Tests.Normalization
{
    /// <summary>
    /// Tests unitaires <see cref="Engine.Normalization.PrimeNormalizer"/>.
    /// Chantier 2 — extraction du Tokenizer (2026-05-25).
    /// </summary>
    public class PrimeNormalizerTests
    {
        [Theory]
        [InlineData('\'', 1)]
        [InlineData('"', 2)]
        [InlineData('’', 1)]   // U+2019
        [InlineData('″', 2)]   // U+2033
        [InlineData('‴', 3)]   // U+2034
        [InlineData('⁗', 4)]   // U+2057
        public void PrimeCount_returns_expected(char c, int expected)
        {
            Assert.Equal(expected, Engine.Normalization.PrimeNormalizer.PrimeCount(c));
        }

        [Theory]
        [InlineData('\'', true)]
        [InlineData('"', true)]
        [InlineData('’', true)]
        [InlineData('″', true)]
        [InlineData('⁗', true)]
        [InlineData('a', false)]
        [InlineData('1', false)]
        [InlineData(' ', false)]
        public void IsPrimeChar_classifies(char c, bool expected)
        {
            Assert.Equal(expected, Engine.Normalization.PrimeNormalizer.IsPrimeChar(c));
        }

        [Theory]
        [InlineData("f'", "f'")]                  // ASCII inchangé
        [InlineData("f''", "f''")]                // double ASCII inchangé
        [InlineData("f\"", "f''")]                // double quote → 2 primes
        [InlineData("f’", "f'")]                  // U+2019 → 1 prime
        [InlineData("f”", "f''")]                 // U+201D → 2 primes
        [InlineData("f″", "f''")]                 // math double prime
        [InlineData("f‴", "f'''")]                // triple prime
        [InlineData("f⁗", "f''''")]               // quadruple prime
        [InlineData("abc", "abc")]                // pas de prime
        [InlineData("", "")]                      // empty
        public void Normalize_canonicalizes_primes(string input, string expected)
        {
            Assert.Equal(expected, Engine.Normalization.PrimeNormalizer.Normalize(input));
        }

        [Fact]
        public void Normalize_handles_null()
        {
            Assert.Equal(string.Empty, Engine.Normalization.PrimeNormalizer.Normalize(null));
        }
    }
}
