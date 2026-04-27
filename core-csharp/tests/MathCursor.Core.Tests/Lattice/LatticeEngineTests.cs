using MathCursor.Core;
using Xunit;

namespace MathCursor.Core.Tests.Lattice
{
    public sealed class LatticeEngineTests
    {
        private readonly LatticeEngine _engine = new LatticeEngine();

        [Fact]
        public void Empty_input_returns_empty_list()
        {
            Assert.Empty(_engine.Convert(""));
            Assert.Empty(_engine.Convert("   "));
            Assert.Empty(_engine.Convert(null!));
        }

        [Fact]
        public void Simple_atom_returns_one_suggestion()
        {
            var s = _engine.Convert("x");
            Assert.Single(s);
            Assert.Equal("x", s[0].Latex);
            Assert.Equal("lattice", s[0].PatternId);
            Assert.False(s[0].IsPartial);
        }

        [Fact]
        public void Trims_input_whitespace()
        {
            // L'adapter envoie parfois des spans avec espaces aux bords ;
            // la façade doit nettoyer pour cohérence.
            var s = _engine.Convert("  x  ");
            Assert.Single(s);
            Assert.Equal("x", s[0].Latex);
        }

        [Fact]
        public void Score_decreases_with_cost()
        {
            // "x" : coût 5 (ident 1 lettre) → score ~95
            // "abc" : coût 27 (ident 3 lettres = 18+3*3) → score ~73
            var simple = _engine.Convert("x");
            var heavy = _engine.Convert("abc");
            Assert.True(simple[0].Score > heavy[0].Score,
                $"Simple score ({simple[0].Score}) doit être > heavy score ({heavy[0].Score})");
        }

        [Fact]
        public void Score_is_clamped_to_zero_min()
        {
            // Un input long dont la lecture coûte > 100 — le score doit rester ≥ 0
            var s = _engine.Convert("abcdefghijklmnop");
            Assert.True(s[0].Score >= 0);
        }

        [Fact]
        public void Sum_full_pipeline_renders_correctly()
        {
            var s = _engine.Convert("sum k=1 n+1 cos2x");
            Assert.Single(s);
            Assert.Equal("\\sum_{k=1}^{n+1} \\cos 2x", s[0].Latex);
        }

        [Fact]
        public void Frac_with_holes_renders_glyphs()
        {
            // L'élève tape "frac a", on lui rend la formule incomplète avec \square
            var s = _engine.Convert("frac a");
            Assert.Single(s);
            Assert.Equal("\\frac{a}{\\square }", s[0].Latex);
            // La présence d'un Hole ne lève PAS IsPartial : c'est une formule
            // valide (juste incomplète au sens utilisateur), Word l'insérera.
            Assert.False(s[0].IsPartial);
        }

        [Fact]
        public void LoadEmbedded_returns_working_instance()
        {
            var engine = LatticeEngine.LoadEmbedded("fr");
            var s = engine.Convert("pi");
            Assert.Single(s);
            Assert.Equal("\\pi", s[0].Latex);
        }
    }
}
