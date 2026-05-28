using Xunit;

namespace MathCursor.Engine.Tests.Concepts
{
    /// <summary>
    /// Tests P19 : géométrie reconnue structurellement par le parser.
    /// (AB) = droite, [AB] = segment, // = parallèle, ⊥ = perpendiculaire.
    /// Pas de règle YAML — relations dans vocab.Relations + juxtaposition
    /// implicite des majuscules dans (...) ou [...].
    /// </summary>
    public class GeometrieTests
    {
        [Fact]
        public void Parallele_droites()
        {
            // P28 : `//` rendu penché \mathbin{/\!/} (= notation manuscrite FR).
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("(AB) // (AC)");
            Assert.Equal(@"(AB) \mathbin{/\!/} (AC)", r.TopLatex);
        }

        [Fact]
        public void Perpendiculaire_droites()
        {
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("(AB) ⊥ (CD)");
            Assert.Equal(@"(AB) \perp (CD)", r.TopLatex);
        }

        [Fact]
        public void Segment_brackets()
        {
            // [AB] = segment AB en notation FR (= juste l'objet [AB]).
            var engine = MathEngine.BuildDefault("fr");
            var r = engine.Resolve("[AB]");
            Assert.Equal(@"[AB]", r.TopLatex);
        }
    }
}
