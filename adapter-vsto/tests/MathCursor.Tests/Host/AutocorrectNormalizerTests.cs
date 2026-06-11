using MathCursor.Host;
using Xunit;

namespace MathCursor.Tests.Host
{
    public class AutocorrectNormalizerTests
    {
        [Theory]
        // Fold des accents (décision 2026-06-11 : strip avant NER/moteur)
        [InlineData("intégrale 0 1 f(x) x", "integrale 0 1 f(x) x")]
        [InlineData("Intégrale a b x dx", "Integrale a b x dx")]
        [InlineData("On en déduit ça", "On en deduit ca")]
        [InlineData("Évaluer où ?", "Evaluer ou ?")]
        // Smart chars existants toujours foldés
        [InlineData("g(x) – g(x+1)", "g(x) - g(x+1)")]
        public void Normalize_folds_accents_and_smart_chars(string input, string expected)
        {
            Assert.Equal(expected, AutocorrectNormalizer.Normalize(input));
        }

        [Theory]
        // × et ÷ sont des opérateurs du vocabulaire moteur — jamais foldés
        [InlineData("a×b")]
        [InlineData("8÷2")]
        // ASCII pur : fast-path, instance inchangée
        [InlineData("sum k f(k)")]
        public void Normalize_leaves_engine_operators_and_ascii_intact(string input)
        {
            Assert.Equal(input, AutocorrectNormalizer.Normalize(input));
        }

        [Fact]
        public void Normalize_preserves_length_invariant()
        {
            // 1 char in → 1 char out, sinon les offsets NER/Word désynchronisent.
            var s = "Soit l'intégrale ∫ où f est définie — étude n°4 : ça marche";
            Assert.Equal(s.Length, AutocorrectNormalizer.Normalize(s).Length);
        }
    }
}
