using System.Linq;
using Xunit;
using MathCursor.Engine.Rules;
using MathCursor.Engine.Vocabulary;

namespace MathCursor.Engine.Tests.Collision
{
    /// <summary>
    /// Tests collision §2.4 + ranker gaté §2.3. Invariant testable :
    /// le ranker NE tourne JAMAIS sur une entrée à candidat unique
    /// (= cœur déterministe).
    /// </summary>
    public class CollisionTests
    {
        // ─── Match unique → pas de collision ──────────────────────────

        [Fact]
        public void Single_match_yields_no_collisions()
        {
            var engine = MathCursor.Engine.MathEngine.BuildDefault("fr");
            var r = engine.Resolve("lim x 0 f(x)");
            Assert.Empty(r.Collisions);
            Assert.Equal("limite-x-to-bound", r.RuleId);
        }

        [Fact]
        public void Plain_arithmetic_no_anchor_no_collision()
        {
            var engine = MathCursor.Engine.MathEngine.BuildDefault("fr");
            var r = engine.Resolve("1+2");
            Assert.Empty(r.Collisions);
            Assert.Equal(string.Empty, r.RuleId); // fallback flat
        }

        // ─── Collision artificielle : 2 règles → 2 candidats ──────────

        [Fact]
        public void Two_rules_match_same_input_yield_collision()
        {
            var vocab = LocaleVocabulary.LoadEmbedded("fr");
            // 2 règles concurrentes sur "lim x 0 f" : on simule un cas où
            // 2 shapes différentes matchent le même input. La pipeline doit
            // émettre 2 candidats triés par couverture du span.
            var rules = new[]
            {
                new RuleSpec {
                    Id = "lim-A",
                    Anchor = "lim",
                    Shape = "lim $var $bound $body",
                    Emit  = "\\lim_{$var \\to $bound} $body" },
                new RuleSpec {
                    Id = "lim-B",
                    Anchor = "lim",
                    Shape = "lim $var $bound",
                    Emit  = "\\lim_{$var \\to $bound}" },
            };
            var engine = new MathCursor.Engine.MathEngine(vocab, rules);

            var r = engine.Resolve("lim x 0 f");
            // Les 2 règles matchent. Collisions ≥ 2 → ranker gaté actif.
            Assert.True(r.Collisions.Count >= 2);
            // Tri : span le plus large d'abord (= lim-A consomme f en body).
            Assert.Equal("lim-A", r.Collisions[0].RuleId);
        }

        [Fact]
        public void Ranker_does_not_run_when_single_candidate()
        {
            // Invariant brief §2.3 : single candidate → Collisions vide
            // (= pas d'output liste, pas de ranking implicite).
            var engine = MathCursor.Engine.MathEngine.BuildDefault("fr");
            var r = engine.Resolve("sum k 1 n (1/k)");
            Assert.NotEqual(string.Empty, r.TopLatex);
            Assert.Empty(r.Collisions);
        }

        // ─── Ordre des candidats : couverture du span ─────────────────

        [Fact]
        public void Collisions_sorted_by_span_coverage_desc()
        {
            var vocab = LocaleVocabulary.LoadEmbedded("fr");
            var rules = new[]
            {
                new RuleSpec { Id = "short", Anchor = "lim", Shape = "lim $var", Emit = "\\lim $var" },
                new RuleSpec { Id = "long",  Anchor = "lim", Shape = "lim $var $bound", Emit = "\\lim_{$var \\to $bound}" },
            };
            var engine = new MathCursor.Engine.MathEngine(vocab, rules);
            var r = engine.Resolve("lim x 0");
            Assert.Equal(2, r.Collisions.Count);
            Assert.Equal("long", r.Collisions[0].RuleId);  // span 3 tokens
            Assert.Equal("short", r.Collisions[1].RuleId); // span 2 tokens
        }
    }
}
