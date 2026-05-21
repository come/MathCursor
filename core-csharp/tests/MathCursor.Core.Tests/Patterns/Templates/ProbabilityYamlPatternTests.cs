using System.Linq;
using Xunit;
using MathCursor.Core.Patterns;
using MathCursor.Core.Patterns.Yaml;

namespace MathCursor.Core.Tests.Patterns.Templates
{
    /// <summary>
    /// Tests du pattern <c>probability</c> (P9e, 2026-05-21) — premier pattern
    /// créé UNIQUEMENT via YAML, sans .cs dédié. Validation du DSL YAML
    /// (P9e) qui permet d'ajouter un nouveau pattern entièrement via
    /// <c>data/patterns/probability.yaml</c>.
    /// </summary>
    public class ProbabilityYamlPatternTests
    {
        private static PatternScanContext Ctx(string source) =>
            new PatternScanContext(
                topAst: null,
                topLatex: source,
                source: source,
                caretOffset: null,
                startPos: 0,
                registry: null);

        private static YamlArgListPatternTemplate New()
            => new YamlArgListPatternTemplate(PatternSpecLoader.LoadEmbedded("probability.yaml"));

        private static PatternCompletion ExpandAll(string source)
        {
            var t = New();
            var ctx = Ctx(source);
            var head = t.TryMatchHead(ctx);
            Assert.NotNull(head);
            return t.Expand(head!, ctx).First();
        }

        // ─── Heads ────────────────────────────────────────────────────

        [Fact]
        public void Matches_P_head()
        {
            var m = New().TryMatchHead(Ctx("P A"));
            Assert.NotNull(m);
            Assert.Equal("probability", m!.TemplateId);
            Assert.Equal(0, m.SourceStart);
            Assert.Equal(1, m.SourceEnd);
        }

        [Fact]
        public void Matches_Prob_head()
        {
            var m = New().TryMatchHead(Ctx("Prob X"));
            Assert.NotNull(m);
            Assert.Equal(4, m!.SourceEnd);
        }

        [Fact]
        public void Rejects_P_in_word_no_boundary()
        {
            // "Paul" : P suivi de "aul" lettres → rejet
            Assert.Null(New().TryMatchHead(Ctx("Paul")));
        }

        // ─── Expand : 1 slot multi-token ──────────────────────────────

        [Fact]
        public void P_alone_yields_template_hint()
        {
            var c = ExpandAll("P");
            Assert.Equal("P()", c.PreviewLatex);
            Assert.Equal(@"P(\square)", c.HintLatex);
            Assert.Equal("P(▭)", c.Description);
            Assert.Equal(50, c.CompletenessScore);
        }

        [Fact]
        public void P_A_complete()
        {
            var c = ExpandAll("P A");
            Assert.Equal("P(A)", c.PreviewLatex);
            Assert.Equal("P(A)", c.HintLatex);
            Assert.Equal(100, c.CompletenessScore);
        }

        [Fact]
        public void P_multi_token_event()
        {
            // "P A ∩ B" : event multi-token = "A ∩ B"
            var c = ExpandAll("P A ∩ B");
            Assert.Equal("P(A ∩ B)", c.PreviewLatex);
        }

        [Fact]
        public void Prob_X_yields_PX()
        {
            // Head "Prob" mais latex/mutation = "P" (= alias)
            var c = ExpandAll("Prob X");
            Assert.Equal("P(X)", c.PreviewLatex);
        }

        // ─── Mutation source ──────────────────────────────────────────

        [Fact]
        public void Mutation_replaces_P_with_P_keyword()
        {
            // Mutation simple : "P A" → "P A" (head est P déjà canonique)
            var c = ExpandAll("P A");
            Assert.Equal("P A", c.Mutation!.Replacement);
        }

        [Fact]
        public void Mutation_replaces_Prob_with_P()
        {
            // "Prob X" → "P X" (Prob aliasé vers P canonique)
            var c = ExpandAll("Prob X");
            Assert.Equal("P X", c.Mutation!.Replacement);
        }

        // ─── Robustesse ───────────────────────────────────────────────

        [Fact]
        public void TryMatchHead_null_ctx_returns_null()
        {
            Assert.Null(New().TryMatchHead(null!));
        }

        // ─── Validation que le YAML est bien chargé ───────────────────

        [Fact]
        public void Loaded_spec_has_correct_template_id()
        {
            var spec = PatternSpecLoader.LoadEmbedded("probability.yaml");
            Assert.Equal("probability", spec.TemplateId);
            Assert.Equal(2, spec.Heads.Count);
            Assert.Single(spec.Slots);
            Assert.True(spec.Slots[0].MultiToken);
        }
    }
}
