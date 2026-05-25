using MathCursor.Engine.Rewriting;
using MathCursor.Engine.Vocabulary;
using Xunit;

namespace MathCursor.Engine.Tests.Rewriting
{
    /// <summary>
    /// Tests POC du <see cref="RewriteEngine"/> avec les <see cref="PilotRules"/>.
    /// Phase A Chantier 4 (2026-05-25). Valide :
    /// - Match simple (1 règle, 1 emit).
    /// - Composition bottom-up (interval-union qui consomme 2 intervals
    ///   déjà reconnus en passe 1).
    /// - Slot manquant → \square (popup guidée).
    /// </summary>
    public class RewriteEnginePilotTests
    {
        private static RewriteEngine BuildEngine()
        {
            var vocab = LocaleVocabulary.LoadEmbedded("fr");
            return new RewriteEngine(vocab, PilotRules.All);
        }

        [Fact]
        public void Frac_explicit_renders()
        {
            var engine = BuildEngine();
            var result = engine.Resolve("frac 1 2");
            Assert.Equal(@"\frac{1}{2}", result.TopLatex);
            Assert.Equal("frac-explicit", result.RuleId);
        }

        [Fact]
        public void Dot_vec_renders()
        {
            var engine = BuildEngine();
            var result = engine.Resolve("a.b");
            Assert.Equal(@"\vec{a}\cdot\vec{b}", result.TopLatex);
        }

        [Fact]
        public void Interval_closed_renders_as_interval_category()
        {
            var engine = BuildEngine();
            var result = engine.Resolve("[0;1]");
            Assert.Equal("[0;1]", result.TopLatex);
            // Vérifier que l'item résultant a la catégorie Interval (= prérequis
            // pour la composition par interval-union).
            Assert.Single(result.Items);
            Assert.Equal(Category.Interval, result.Items[0].Category);
        }

        [Fact]
        public void Interval_union_composes_bottom_up()
        {
            // Le cœur du POC : les 2 intervals sont reconnus en passe 1,
            // puis interval-union compose en passe 2.
            var engine = BuildEngine();
            var result = engine.Resolve("[0;1] union [2;3]");
            Assert.Equal(@"[0;1] \cup [2;3]", result.TopLatex);
        }

        [Fact]
        public void Sum_classic_renders()
        {
            var engine = BuildEngine();
            var result = engine.Resolve("somme k 1 n k");
            Assert.Equal(@"\sum_{k=1}^{n}k", result.TopLatex);
        }

        [Fact]
        public void Lim_classic_renders()
        {
            var engine = BuildEngine();
            var result = engine.Resolve("lim x 0 x");
            Assert.Equal(@"\lim_{x \to 0}x", result.TopLatex);
        }

        [Fact]
        public void Funcdef_renders()
        {
            var engine = BuildEngine();
            var result = engine.Resolve("f : x -> x");
            Assert.Equal(@"f : x \mapsto x", result.TopLatex);
        }

        [Fact]
        public void Empty_source_returns_empty()
        {
            var engine = BuildEngine();
            var result = engine.Resolve("");
            Assert.Equal("", result.TopLatex);
        }

        [Fact]
        public void Plain_text_passes_through_when_no_match()
        {
            var engine = BuildEngine();
            // "xyz" : aucune règle ne matche (vec-letter désactivée pour V1).
            var result = engine.Resolve("xyz");
            Assert.Equal("xyz", result.TopLatex);
        }

        [Fact]
        public void Matrix_row_2x1_with_repeat()
        {
            var engine = BuildEngine();
            var result = engine.Resolve("a,b");
            // matrix-row matche directement « a , b » en tant que Set.
            Assert.Single(result.Items);
            Assert.Equal(Category.Set, result.Items[0].Category);
            Assert.Equal("a & b", result.Items[0].Latex);
        }

        [Fact]
        public void Matrix_2x2_composes_via_matrix_row()
        {
            // Démontre la composition à 2 niveaux : matrix-row reconnaît
            // chaque ligne en passe 1, matrix les compose en passe 2.
            var engine = BuildEngine();
            var result = engine.Resolve("{ a,b ; c,d }");
            Assert.Equal(@"\begin{matrix}a & b \\ c & d\end{matrix}", result.TopLatex);
        }

        [Fact]
        public void Matrix_3x3_composes_via_matrix_row()
        {
            var engine = BuildEngine();
            var result = engine.Resolve("{ a,b,c ; d,e,f ; g,h,i }");
            Assert.Equal(@"\begin{matrix}a & b & c \\ d & e & f \\ g & h & i\end{matrix}", result.TopLatex);
        }

        [Fact]
        public void Frac_slurp_num_2_pairs()
        {
            // Démontre le RepeatBlock (inner composite) : 2 paires a/b + a/b
            // → \frac{a+a}{b+b}. C'est le mécanisme générique de slurp sur
            // N termes.
            var engine = BuildEngine();
            var result = engine.Resolve("1/2 + 3/4");
            Assert.Equal(@"\frac{1+3}{2+4}", result.TopLatex);
        }

        [Fact]
        public void Frac_slurp_num_3_pairs()
        {
            // Slurp sur 3 termes via RepeatBlock min=2 max illimité.
            var engine = BuildEngine();
            var result = engine.Resolve("1/2 + 3/4 + 5/6");
            Assert.Equal(@"\frac{1+3+5}{2+4+6}", result.TopLatex);
        }
    }
}
