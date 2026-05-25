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
    }
}
