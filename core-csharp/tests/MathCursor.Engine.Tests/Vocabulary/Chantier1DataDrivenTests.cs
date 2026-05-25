using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Engine.Tests.Vocabulary
{
    /// <summary>
    /// Chantier 1 (2026-05-25) : tests des nouveaux champs YAML data-driven
    /// migrés depuis hardcoded FR de l'adapter VSTO.
    /// </summary>
    public class Chantier1DataDrivenTests
    {
        private readonly ITestOutputHelper _output;

        public Chantier1DataDrivenTests(ITestOutputHelper output) { _output = output; }

        [Fact]
        public void Stopwords_chargees_depuis_yaml()
        {
            var v = Engine.Vocabulary.LocaleVocabulary.LoadEmbedded("fr");
            _output.WriteLine($"stopwords count={v.Stopwords.Count}");
            Assert.Contains("soit", v.Stopwords);
            Assert.Contains("et", v.Stopwords);
            Assert.Contains("avec", v.Stopwords);
            Assert.Contains("Soit", v.Stopwords);  // case-insensitive
        }

        [Fact]
        public void Span_delimiters_chargees_depuis_yaml()
        {
            var v = Engine.Vocabulary.LocaleVocabulary.LoadEmbedded("fr");
            _output.WriteLine($"delimiters count={v.SpanDelimiters.Count}");
            Assert.Contains('.', v.SpanDelimiters);
            Assert.Contains(';', v.SpanDelimiters);
            Assert.Contains('=', v.SpanDelimiters);
            Assert.Contains('\n', v.SpanDelimiters);
            // `,` et `:` exclus (= opérateurs math)
            Assert.DoesNotContain(',', v.SpanDelimiters);
            Assert.DoesNotContain(':', v.SpanDelimiters);
        }

        [Fact]
        public void Math_prefix_keywords_chargees_depuis_yaml()
        {
            var v = Engine.Vocabulary.LocaleVocabulary.LoadEmbedded("fr");
            _output.WriteLine($"prefix keywords count={v.MathPrefixKeywords.Count}");
            Assert.Contains("lim", v.MathPrefixKeywords);
            Assert.Contains("limite", v.MathPrefixKeywords);
            Assert.Contains("racine", v.MathPrefixKeywords);
            Assert.Contains("somme", v.MathPrefixKeywords);
            Assert.Contains("vec", v.MathPrefixKeywords);
            Assert.Contains("LIM", v.MathPrefixKeywords);  // case-insensitive
        }

        [Fact]
        public void Multi_char_ops_derives_de_relations()
        {
            // Le tokenizer doit reconnaître les multi-char ops déclarés dans
            // Relations (= `<=>`, `=>`, `<=`, `≤`, `∪`, etc.) sans hardcoded list.
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("a <=> b");
            _output.WriteLine($"a <=> b → top='{r.TopLatex}'");
            Assert.Contains("\\iff", r.TopLatex);
        }
    }
}
