using System.Linq;
using Xunit;
using MathCursor.Core.Lattice.Ast;
using MathCursor.Core.Patterns;
using MathCursor.Core.Patterns.Templates;

namespace MathCursor.Core.Tests.Patterns.Templates
{
    /// <summary>
    /// Tests unitaires <see cref="ForallBelongsTemplate"/> : head V/E (+ ∀/∃),
    /// var CSV (x / x,y / x,y,z), 6 openers (app a, appartient, dans, (-, ∈,
    /// in), états partiels, rejet de boundary, mutation source. Étape P5
    /// (ADR <c>2026-05-21-Feat-forall-belongs-pattern</c>).
    ///
    /// <para>Pour les tests bout-en-bout compositionnel (V x app a R,
    /// V x app a [0,1]U[3,4]), voir <c>ForallBelongsCompositionTests</c>.</para>
    /// </summary>
    public class ForallBelongsTemplateTests
    {
        private static PatternScanContext Ctx(string source, PatternRegistry? registry = null) =>
            new PatternScanContext(
                topAst: null,
                topLatex: source,
                source: source,
                caretOffset: null,
                startPos: 0,
                registry: registry);

        private static ForallBelongsTemplate New() => new ForallBelongsTemplate();

        private static PatternCompletion ExpandAll(string source, PatternRegistry? registry = null)
        {
            var t = New();
            var ctx = Ctx(source, registry);
            var head = t.TryMatchHead(ctx);
            Assert.NotNull(head);
            return t.Expand(head!, ctx).First();
        }

        // ─── TryMatchHead : V/E head detection ────────────────────────

        [Theory]
        [InlineData("V")]
        [InlineData("V x")]
        [InlineData("V x app a R")]
        public void Matches_V_head(string source)
        {
            var m = New().TryMatchHead(Ctx(source));
            Assert.NotNull(m);
            Assert.Equal("forall-belongs", m!.TemplateId);
            Assert.Equal(0, m.SourceStart);
            Assert.Equal(1, m.SourceEnd);
            Assert.Equal("V", ((FilledSlotAtom)m.Slots["polarity"]).Text);
        }

        [Theory]
        [InlineData("E")]
        [InlineData("E x")]
        public void Matches_E_head(string source)
        {
            var m = New().TryMatchHead(Ctx(source));
            Assert.NotNull(m);
            Assert.Equal("E", ((FilledSlotAtom)m!.Slots["polarity"]).Text);
        }

        [Theory]
        [InlineData("∀ x")]
        [InlineData("∃ x")]
        public void Matches_unicode_heads(string source)
        {
            var m = New().TryMatchHead(Ctx(source));
            Assert.NotNull(m);
        }

        [Fact]
        public void Rejects_V_in_word_no_boundary_right()
        {
            // "Vx" : V suivi directement par lettre → rejet (sinon "VAR"
            // matcherait sur V).
            Assert.Null(New().TryMatchHead(Ctx("Vx")));
        }

        [Fact]
        public void Rejects_E_in_word_no_boundary_right()
        {
            Assert.Null(New().TryMatchHead(Ctx("Ex")));
        }

        [Fact]
        public void Rejects_V_after_letter_no_boundary_left()
        {
            // "xV" : V précédé d'une lettre → rejet.
            Assert.Null(New().TryMatchHead(Ctx("xV")));
        }

        [Fact]
        public void Returns_null_on_empty_source()
        {
            Assert.Null(New().TryMatchHead(Ctx("")));
        }

        // ─── Expand : V seul, V x, V x,y ──────────────────────────────

        [Fact]
        public void V_alone_yields_forall_with_var_square_hint()
        {
            var c = ExpandAll("V");
            Assert.Equal(@"\forall", c.PreviewLatex);
            Assert.Equal(@"\forall \square", c.HintLatex);
            Assert.Equal("∀▭", c.Description);
            Assert.True(c.CompletenessScore < 50);
        }

        [Fact]
        public void V_x_yields_forall_x()
        {
            var c = ExpandAll("V x");
            Assert.Equal(@"\forall x", c.PreviewLatex);
            Assert.Equal(@"\forall x", c.HintLatex);
            Assert.Equal("∀x", c.Description);
        }

        [Fact]
        public void V_x_y_yields_forall_x_y()
        {
            var c = ExpandAll("V x,y");
            Assert.Equal(@"\forall x,y", c.PreviewLatex);
        }

        [Fact]
        public void V_three_idents_csv()
        {
            var c = ExpandAll("V x,y,z");
            Assert.Equal(@"\forall x,y,z", c.PreviewLatex);
        }

        [Fact]
        public void V_csv_with_spaces_around_commas()
        {
            // Tolérance espaces : "V x , y" doit matcher comme CSV
            var c = ExpandAll("V x , y");
            Assert.Contains("x", c.PreviewLatex);
            Assert.Contains("y", c.PreviewLatex);
        }

        // ─── Expand : openers sans Registry (domain non parsé) ────────

        [Theory]
        [InlineData("V x app a R", "app a")]
        [InlineData("V x appartient R", "appartient")]
        [InlineData("V x dans R", "dans")]
        [InlineData("V x (- R", "(-")]
        [InlineData("V x ∈ R", "∈")]
        [InlineData("V x in R", "in")]
        public void Each_opener_recognized_without_registry(string source, string expectedOpenerToken)
        {
            // Sans Registry : opener reconnu mais domain non parsé (slot
            // domain reste vide). PreviewLatex montre `\forall x \in` mais
            // sans le R rendu.
            var c = ExpandAll(source);
            Assert.Contains(@"\forall x \in", c.PreviewLatex);
            // Description doit aussi montrer ∈ + placeholder ▭ car domain absent
            Assert.Contains("∈", c.Description);
            Assert.Contains("▭", c.Description);
        }

        [Fact]
        public void V_x_app_a_without_domain_yields_hint_with_square()
        {
            // Sans Registry, le domain n'est pas parsé donc on tombe sur le
            // cas "opener sans sub-completion" → hint montre `\square`.
            var c = ExpandAll("V x app a R");
            Assert.Contains(@"\square", c.HintLatex);
        }

        [Fact]
        public void Rejects_opener_without_word_boundary()
        {
            // "in" suivi de "ner" devrait être rejeté (= word boundary
            // requise pour les mots openers).
            var c = ExpandAll("V x inner");
            // opener "in" suivi de "ner" (lettre) → rejet → 1 completion sans
            // domain ni opener
            Assert.DoesNotContain(@"\in", c.PreviewLatex);
        }

        // ─── Mutation source ──────────────────────────────────────────

        [Fact]
        public void Mutation_replaces_V_with_forall_in_simple_case()
        {
            var c = ExpandAll("V x");
            Assert.NotNull(c.Mutation);
            // Mutation couvre toute la zone du pattern (V à x)
            Assert.Equal(0, c.Mutation!.Offset);
            // "V x" → "forall x"
            Assert.Equal("forall x", c.Mutation.Replacement);
        }

        [Fact]
        public void Mutation_replaces_E_with_exists()
        {
            var c = ExpandAll("E x");
            Assert.Equal("exists x", c.Mutation!.Replacement);
        }

        [Fact]
        public void Mutation_for_V_only_replaces_to_forall()
        {
            var c = ExpandAll("V");
            Assert.Equal("forall", c.Mutation!.Replacement);
            Assert.Equal(0, c.Mutation.Offset);
            Assert.Equal(1, c.Mutation.Length); // juste le V
        }

        // ─── Multi-completion (mécanisme par poids) ───────────────────

        [Fact]
        public void Single_opener_match_yields_single_completion()
        {
            // Avec les 6 aliases actuels qui commencent tous par des chars
            // différents, en pratique 1 alias = 1 completion.
            var t = New();
            var ctx = Ctx("V x app a R");
            var head = t.TryMatchHead(ctx)!;
            var completions = t.Expand(head, ctx);
            Assert.Single(completions);
        }

        // ─── Robustesse ───────────────────────────────────────────────

        [Fact]
        public void TryMatchHead_null_ctx_returns_null()
        {
            Assert.Null(New().TryMatchHead(null!));
        }

        [Fact]
        public void Expand_null_state_returns_empty()
        {
            Assert.Empty(New().Expand(null!, Ctx("V x")));
        }

        [Fact]
        public void Expand_null_ctx_returns_empty()
        {
            var t = New();
            var head = t.TryMatchHead(Ctx("V x"))!;
            Assert.Empty(t.Expand(head, null!));
        }
    }
}
