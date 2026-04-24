using System;
using System.Text;
using Engine = MathCursor.Core.PatternEngine.PatternEngine;
using Xunit;

namespace MathCursor.Core.Tests
{
    /// <summary>
    /// Fuzz léger : 1000 inputs aléatoires. Le moteur doit ne JAMAIS jeter
    /// d'exception. Il peut retourner 0 candidat, c'est acceptable.
    /// </summary>
    public sealed class FuzzTests
    {
        [Fact]
        public void Engine_never_throws_on_random_garbage()
        {
            var engine = Engine.LoadEmbedded("fr");
            var rng = new Random(42); // seed fixe pour reproductibilité
            const int iterations = 1000;

            for (int i = 0; i < iterations; i++)
            {
                string input = RandomInput(rng);
                try
                {
                    var _ = engine.Convert(input);
                }
                catch (Exception ex)
                {
                    Assert.Fail($"Convert threw on input[{i}]=\"{input}\" : {ex.GetType().Name} — {ex.Message}");
                }
            }
        }

        private static string RandomInput(Random rng)
        {
            // Mix de caractères ASCII math + unicode quelques symboles + lettres
            const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789+-*/=()[]{},;.<>^_ |\\π∞αβγ";
            int len = rng.Next(0, 40);
            var sb = new StringBuilder(len);
            for (int i = 0; i < len; i++)
                sb.Append(alphabet[rng.Next(alphabet.Length)]);
            return sb.ToString();
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("\t\n")]
        [InlineData("((((")]
        [InlineData("))))")]
        [InlineData("[[[[")]
        [InlineData("]]]]" )]
        [InlineData("+++++")]
        [InlineData("======")]
        [InlineData("/////")]
        [InlineData("\\\\\\\\")]
        [InlineData("^^^^^")]
        [InlineData("____")]
        public void Engine_handles_edge_cases_gracefully(string input)
        {
            var engine = Engine.LoadEmbedded("fr");
            var result = engine.Convert(input);
            Assert.NotNull(result); // 0 candidats OK, pas null
        }
    }
}
