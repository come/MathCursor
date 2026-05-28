using Xunit;

namespace MathCursor.Engine.Tests.Concepts
{
    /// <summary>
    /// Tests P20 : intervalles reconnus structurellement par le parser
    /// (= via ListCombinator + délimiteurs). Pas de règle YAML.
    /// </summary>
    public class IntervallesTests
    {
        [Fact]
        public void Closed_interval()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("[0,1]");
            Assert.Equal("[0,1]", r.TopLatex);
        }

        [Fact]
        public void Open_interval()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("(0,1)");
            Assert.Equal("(0,1)", r.TopLatex);
        }

        [Fact]
        public void Half_open_brackets()
        {
            // Note : `[0,1)` syntaxe US. En FR on tape `[0,1[`. Notre POC
            // gère `[0,1)` car le parser accepte tout délim ouvrant/fermant.
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("[0,1)");
            Assert.Equal("[0,1)", r.TopLatex);
        }

        [Fact]
        public void Interval_with_letters()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("[a,b]");
            Assert.Equal("[a,b]", r.TopLatex);
        }
    }
}
