using System.Linq;
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
            // Phase 5a : peut renvoyer plusieurs suggestions si le top-K
            // diverge proche du top-1. On vérifie que le top-1 est correct.
            var s = _engine.Convert("sum k=1 n+1 cos2x");
            Assert.NotEmpty(s);
            Assert.Equal("\\sum_{k=1}^{n+1} \\cos 2x", s[0].Latex);
        }

        [Fact]
        public void Frac_with_holes_renders_glyphs()
        {
            var s = _engine.Convert("frac a");
            Assert.NotEmpty(s);
            Assert.Equal("\\frac{a}{\\square }", s[0].Latex);
            Assert.False(s[0].IsPartial);
        }

        [Fact]
        public void LoadEmbedded_returns_working_instance()
        {
            var engine = LatticeEngine.LoadEmbedded("fr");
            var s = engine.Convert("pi");
            Assert.NotEmpty(s);
            Assert.Equal("\\pi", s[0].Latex);
        }

        // ----- Phase 5a : multi-suggestions -----

        [Fact]
        public void Top_one_unambiguous_returns_single_suggestion()
        {
            // "x" : un seul chemin possible dans le lattice, pas d'alternative
            var s = _engine.Convert("x");
            Assert.Single(s);
            Assert.Equal("x", s[0].Latex);
        }

        [Fact]
        public void Suggestions_are_deduplicated_by_latex()
        {
            // Deux paths du top-K peuvent rendre le même LaTeX (ex: "ab" en
            // ident vs "a*b" en implicit mult → même rendu "ab"). La façade
            // dédupe par signature LaTeX, pas par path.
            var s = _engine.Convert("ab");
            var distinctLatex = s.Select(x => x.Latex).Distinct().Count();
            Assert.Equal(s.Count, distinctLatex);
        }

        [Fact]
        public void Top_one_is_first_in_list()
        {
            // Convention : index 0 = top-1 (cost le plus bas). Permet à
            // l'adapter VSTO de sélectionner par défaut s[0] sans logique.
            var s = _engine.Convert("sum k 1 n k");
            Assert.NotEmpty(s);
            // Le top-1 est le chemin de plus faible coût Dijkstra → score
            // le plus élevé. Aucune autre suggestion ne peut avoir un score
            // strictement supérieur.
            for (int i = 1; i < s.Count; i++)
                Assert.True(s[i].Score <= s[0].Score,
                    $"s[{i}].Score ({s[i].Score}) > s[0].Score ({s[0].Score}) — top-1 doit être en tête");
        }

        [Fact]
        public void Suggestions_capped_at_max()
        {
            // Au plus 4 suggestions (top-1 + 3 alternatives), même si le
            // top-K génère plus de chemins valides.
            var s = _engine.Convert("abcde");
            Assert.True(s.Count <= 4, $"Trop de suggestions : {s.Count}");
        }
    }
}
