using System.Linq;
using Xunit;
using MathCursor.Core;
using MathCursor.Core.Lattice;
using MathCursor.Core.Patterns;
using MathCursor.Core.Patterns.Templates;

namespace MathCursor.Core.Tests.Patterns
{
    /// <summary>
    /// Tests P5R+ (2026-05-21) : trailing whitespace handling pour les
    /// patterns à slot optionnel + IsIncomplete pour garder la popup
    /// ouverte pendant la saisie. Cf. ADR
    /// <c>2026-05-21-Feat-pattern-trailing-hints-and-isincomplete</c>.
    /// </summary>
    public class PatternTrailingHintsTests
    {
        private static ZoneResolver MakeResolverWithPatterns()
        {
            var (pipeline, registry) = DefaultPatternRegistry.BuildBoth();
            return new ZoneResolver(new LatticeEngine(), pipeline, registry);
        }

        private static PatternScanContext Ctx(string source, PatternRegistry registry) =>
            new PatternScanContext(
                topAst: null,
                topLatex: source,
                source: source,
                caretOffset: null,
                startPos: 0,
                registry: registry);

        // ─── Trailing space → carré hint domain pour forall ────────────

        [Fact]
        public void V_x_without_trailing_no_domain_hint()
        {
            // "V x" sans espace après → pas de carré domain dans HintLatex
            var registry = DefaultPatternRegistry.Build();
            var t = new ForallBelongsTemplate();
            var ctx = Ctx("V x", registry);
            var head = t.TryMatchHead(ctx)!;
            var c = t.Expand(head, ctx)[0];
            Assert.Equal(@"\forall x", c.PreviewLatex);
            Assert.Equal(@"\forall x", c.HintLatex);
            Assert.DoesNotContain(@"\square", c.HintLatex);
        }

        [Fact]
        public void V_x_with_trailing_yields_square_domain_hint()
        {
            // "V x " avec espace après → HintLatex montre `\in \square`
            var registry = DefaultPatternRegistry.Build();
            var t = new ForallBelongsTemplate();
            var ctx = Ctx("V x ", registry);
            var head = t.TryMatchHead(ctx)!;
            var c = t.Expand(head, ctx)[0];
            Assert.Equal(@"\forall x", c.PreviewLatex); // commit-clean (= sans carré)
            Assert.Equal(@"\forall x \in \square", c.HintLatex); // affichage avec carré
        }

        [Fact]
        public void V_with_trailing_yields_full_template_hint()
        {
            // "V " trailing space, pas de var encore → HintLatex montre
            // ∀ + carré var + carré domain (= forme template guidante)
            var registry = DefaultPatternRegistry.Build();
            var t = new ForallBelongsTemplate();
            var ctx = Ctx("V ", registry);
            var head = t.TryMatchHead(ctx)!;
            var c = t.Expand(head, ctx)[0];
            Assert.Equal(@"\forall", c.PreviewLatex);
            Assert.Equal(@"\forall \square \in \square", c.HintLatex);
        }

        [Fact]
        public void V_x_R_complete_no_trailing_handling_needed()
        {
            // Pattern complet : ni hint domain supplémentaire, ni carré.
            var registry = DefaultPatternRegistry.Build();
            var t = new ForallBelongsTemplate();
            var ctx = Ctx("V x R", registry);
            var head = t.TryMatchHead(ctx)!;
            var c = t.Expand(head, ctx)[0];
            Assert.Equal(@"\forall x \in \mathbb{R}", c.PreviewLatex);
            Assert.Equal(@"\forall x \in \mathbb{R}", c.HintLatex);
            Assert.DoesNotContain(@"\square", c.HintLatex);
        }

        [Fact]
        public void V_x_R_with_trailing_does_not_add_extra_hint()
        {
            // "V x R " trailing space mais domain déjà rempli → pas de carré ajouté
            var registry = DefaultPatternRegistry.Build();
            var t = new ForallBelongsTemplate();
            var ctx = Ctx("V x R ", registry);
            var head = t.TryMatchHead(ctx)!;
            var c = t.Expand(head, ctx)[0];
            Assert.DoesNotContain(@"\square", c.HintLatex);
        }

        // ─── IsIncomplete pour pattern partiel ────────────────────────

        [Fact]
        public void V_alone_marks_resolved_as_incomplete()
        {
            // "V" seul → pattern actif avec score < 100 → IsIncomplete = true
            var r = MakeResolverWithPatterns().Resolve("V");
            Assert.True(r.IsIncomplete);
        }

        [Fact]
        public void V_x_with_trailing_marks_incomplete()
        {
            // "V x " trailing space → score < 100 (pas de domain) → IsIncomplete
            var r = MakeResolverWithPatterns().Resolve("V x ");
            Assert.True(r.IsIncomplete);
        }

        [Fact]
        public void V_x_R_complete_marks_not_incomplete()
        {
            // Pattern complet (score 100) → IsIncomplete = false
            // (sauf si autre signal incomplete, ex. opérateur trailing)
            var r = MakeResolverWithPatterns().Resolve("V x R");
            Assert.False(r.IsIncomplete);
        }

        [Fact]
        public void V_x_interval_union_complete_marks_not_incomplete()
        {
            var r = MakeResolverWithPatterns().Resolve("V x [0,1]U[3,4]");
            Assert.False(r.IsIncomplete);
        }

        [Fact]
        public void Source_without_pattern_not_marked_incomplete_by_pattern_check()
        {
            // "abc" pas de pattern → check pattern partiel = no-op,
            // IsIncomplete dépend uniquement du check legacy (\square, op final).
            var r = MakeResolverWithPatterns().Resolve("abc");
            Assert.False(r.IsIncomplete);
        }
    }
}
