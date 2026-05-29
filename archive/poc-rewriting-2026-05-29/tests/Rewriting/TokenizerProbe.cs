using System.Text;
using MathCursor.Engine.Tokenization;
using MathCursor.Engine.Vocabulary;
using Xunit.Abstractions;

namespace MathCursor.Engine.Tests.Rewriting
{
    /// <summary>Probe pour comprendre comment le tokenizer FR traite les
    /// mots filler `quand`, `tend vers`, etc. ChC.1.</summary>
    public class TokenizerProbe
    {
        private readonly ITestOutputHelper _output;
        public TokenizerProbe(ITestOutputHelper output) { _output = output; }

        [Xunit.Fact]
        public void Show_tokens_for_lim_with_fillers()
        {
            var vocab = LocaleVocabulary.LoadEmbedded("fr");
            var tokenizer = new Tokenizer(vocab);
            var tokens = tokenizer.Tokenize("lim quand x tend vers 0 y");
            var sb = new StringBuilder();
            foreach (var t in tokens) sb.AppendLine($"  {t.Kind}: '{t.Text}'");
            _output.WriteLine(sb.ToString());
        }

        [Xunit.Theory]
        [Xunit.InlineData("int t 0 +oo f(t)")]
        [Xunit.InlineData("derive x sin(x)")]
        [Xunit.InlineData("frac n n+1")]
        [Xunit.InlineData("sqrt x^2+y^2")]
        [Xunit.InlineData("G:x->1/x")]
        [Xunit.InlineData("sum k 1 n (1/k)")]
        [Xunit.InlineData("sum i 0 N (a_i)")]
        [Xunit.InlineData("iint x y f(x,y)")]
        [Xunit.InlineData("sum k 0 2n+3 g(k)")]
        public void Show_tokens_for_failures(string input)
        {
            var vocab = LocaleVocabulary.LoadEmbedded("fr");
            var tokenizer = new Tokenizer(vocab);
            var tokens = tokenizer.Tokenize(input);
            var sb = new StringBuilder();
            sb.AppendLine($"INPUT: {input}");
            foreach (var t in tokens) sb.AppendLine($"  {t.Kind}: '{t.Text}'");
            _output.WriteLine(sb.ToString());
        }
    }
}
