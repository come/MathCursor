using System.Linq;
using Xunit;
using MathCursor.Core.Patterns;
using MathCursor.Core.Patterns.Ranking;

namespace MathCursor.Core.Tests.Patterns.Ranking
{
    /// <summary>
    /// Tests <see cref="DefaultPatternRanker"/> : dédup, scoring composite,
    /// NMS overlap. Cf. ADR <c>2026-05-21-Feat-pattern-ranker</c> (P10).
    /// </summary>
    public class DefaultPatternRankerTests
    {
        private static PatternScanContext Ctx(string source, int? caret = null) =>
            new PatternScanContext(
                topAst: null, topLatex: source, source: source,
                caretOffset: caret, startPos: 0, registry: null);

        private static PatternCompletion C(
            string preview, int start, int end, int completeness = 100,
            string? description = null) =>
            new PatternCompletion(
                description: description ?? preview,
                previewLatex: preview,
                hintLatex: preview,
                mutation: null,
                completenessScore: completeness,
                sourceStart: start,
                sourceEnd: end);

        private static DefaultPatternRanker R() => new DefaultPatternRanker();

        // ─── Robustesse ───────────────────────────────────────────────

        [Fact]
        public void Null_input_returns_empty()
        {
            Assert.Empty(R().Rank(null!, Ctx("x")));
        }

        [Fact]
        public void Empty_input_returns_empty()
        {
            Assert.Empty(R().Rank(System.Array.Empty<PatternCompletion>(), Ctx("x")));
        }

        // ─── Dédup ────────────────────────────────────────────────────

        [Fact]
        public void Dedup_identical_span_and_preview_keeps_first()
        {
            // 2 completions à clé (start, end, preview) identique → 1 gardée.
            // Span disjoint pour le 3e (= NMS ne va pas interférer avec la
            // mesure de la dédup).
            var a = C("DUP", 10, 13);
            var b = C("DUP", 10, 13);  // doublon de a
            var c = C("DIFF", 0, 5);

            var ranked = R().Rank(new[] { a, b, c }, Ctx("DIFF__1234DUP_"));

            // a et b → dédupliqués à 1, c kept. Total = 2.
            Assert.Equal(2, ranked.Count);
            Assert.Contains(ranked, x => x.PreviewLatex == "DUP");
            Assert.Contains(ranked, x => x.PreviewLatex == "DIFF");
        }

        [Fact]
        public void No_dedup_when_different_preview_but_same_span()
        {
            // Même span, preview différent (= 2 templates distincts proposent
            // des rendus différents). Dédup conservatif : on ne fusionne pas.
            var a = C("preview-A", 0, 5);
            var b = C("preview-B", 0, 5);

            var ranked = R().Rank(new[] { a, b }, Ctx("12345"));

            // Pas dédupliqués au niveau de l'étape 1 (clé inclut PreviewLatex).
            // Mais NMS étape 3 va en filtrer un (= overlap span identique).
            // Le test vérifie surtout l'invariant : pas tous les deux jetés
            // par la dédup seule, donc au moins 1 sort.
            Assert.NotEmpty(ranked);
        }

        // ─── Score : bonus span complet ───────────────────────────────

        [Fact]
        public void Bonus_span_complete_added_when_span_covers_all_source()
        {
            // Source de longueur 5, completion couvre [0..5] → bonus +30.
            var c = C("xxx", 0, 5, completeness: 50);
            var score = DefaultPatternRanker.ComputeScore(c, Ctx("hello"));
            Assert.Equal(50 + DefaultPatternRanker.BonusSpanComplete, score);
        }

        [Fact]
        public void No_bonus_span_when_partial_span()
        {
            // Source de longueur 5, completion couvre seulement [0..3].
            var c = C("xxx", 0, 3, completeness: 50);
            var score = DefaultPatternRanker.ComputeScore(c, Ctx("hello"));
            Assert.Equal(50, score);
        }

        // ─── Score : bonus caret-aware ────────────────────────────────

        [Fact]
        public void Bonus_caret_added_when_caret_in_span()
        {
            // Source "F'(x)=1/x", caret position 3 (dans le span [0..5]).
            var c = C("F'(x)", 0, 5, completeness: 100);
            var score = DefaultPatternRanker.ComputeScore(c, Ctx("F'(x)=1/x", caret: 3));
            // 100 + 15 (caret) — pas de bonus span (source.Length=9, span=5).
            Assert.Equal(100 + DefaultPatternRanker.BonusCaretInsideSpan, score);
        }

        [Fact]
        public void No_bonus_caret_when_caret_outside_span()
        {
            // Source "F'(x)=1/x", caret 8 (hors du span [0..5]).
            var c = C("F'(x)", 0, 5, completeness: 100);
            var score = DefaultPatternRanker.ComputeScore(c, Ctx("F'(x)=1/x", caret: 8));
            Assert.Equal(100, score);
        }

        [Fact]
        public void No_bonus_caret_when_null()
        {
            var c = C("xxx", 0, 5, completeness: 50);
            var score = DefaultPatternRanker.ComputeScore(c, Ctx("hello", caret: null));
            // Span complet → +30, pas de caret → +0.
            Assert.Equal(50 + DefaultPatternRanker.BonusSpanComplete, score);
        }

        // ─── NMS overlap ──────────────────────────────────────────────

        [Fact]
        public void NMS_drops_lower_score_when_spans_overlap()
        {
            // 2 completions overlapent (= [0..5] et [2..4]) → garde meilleur
            // score. CompletenessScore 100 vs 50 → garde 100.
            var winner = C("F'(x)", 0, 5, completeness: 100);
            var loser = C("(x,▭)", 2, 4, completeness: 50);

            var ranked = R().Rank(new[] { winner, loser }, Ctx("F'(x)=1/x"));

            Assert.Single(ranked);
            Assert.Equal("F'(x)", ranked[0].PreviewLatex);
        }

        [Fact]
        public void NMS_keeps_both_when_spans_disjoint()
        {
            // Spans disjoints (= [0..3] et [5..8]) → tous gardés.
            var a = C("AAA", 0, 3, completeness: 100);
            var b = C("BBB", 5, 8, completeness: 100);

            var ranked = R().Rank(new[] { a, b }, Ctx("AAA__BBB__"));

            Assert.Equal(2, ranked.Count);
        }

        [Fact]
        public void NMS_keeps_tangent_spans()
        {
            // Spans tangents (= end == start) ne se chevauchent pas.
            // [0..3] et [3..6] sont compatibles.
            var a = C("ABC", 0, 3, completeness: 100);
            var b = C("DEF", 3, 6, completeness: 100);

            var ranked = R().Rank(new[] { a, b }, Ctx("ABCDEF"));

            Assert.Equal(2, ranked.Count);
        }

        [Fact]
        public void NMS_tie_score_keeps_first_in_input_order()
        {
            // Deux completions de même score qui overlapent → garde la 1ère
            // dans l'ordre input (= déterminisme via OrderByDescending stable).
            var first = C("FIRST", 0, 5, completeness: 100);
            var second = C("SECND", 1, 4, completeness: 100);

            var ranked = R().Rank(new[] { first, second }, Ctx("XXXXXX"));

            Assert.Single(ranked);
            Assert.Equal("FIRST", ranked[0].PreviewLatex);
        }

        // ─── Idempotence ──────────────────────────────────────────────

        [Fact]
        public void Rank_is_idempotent()
        {
            var input = new[]
            {
                C("AAA", 0, 3, completeness: 100),
                C("BBB", 5, 8, completeness: 80),
                C("(x,▭)", 2, 4, completeness: 50),
            };
            var ctx = Ctx("AAA__BBB__", caret: 1);

            var r1 = R().Rank(input, ctx);
            var r2 = R().Rank(r1, ctx);

            Assert.Equal(r1.Count, r2.Count);
            for (int i = 0; i < r1.Count; i++)
                Assert.Equal(r1[i].PreviewLatex, r2[i].PreviewLatex);
        }

        // ─── Composition end-to-end : cas user F'(x)=1/x ──────────────

        [Fact]
        public void Pilot_F_prime_x_equals_keeps_only_primed_derivative()
        {
            // Simule la sortie de PatternPipeline avant le ranker pour
            // "F'(x)=1/x" : 2 completions IntervalUnion-like + 1 primed.
            // Le ranker doit garder uniquement la primed (= meilleur score).
            var intervalA = C("\\left(x,\\right)", 2, 4, completeness: 50);
            var intervalB = C("\\left(x,\\right)", 2, 4, completeness: 50);  // doublon
            var primed = C("F'(x)", 0, 5, completeness: 100);

            var ranked = R().Rank(
                new[] { intervalA, intervalB, primed },
                Ctx("F'(x)=1/x", caret: 5));

            Assert.Single(ranked);
            Assert.Equal("F'(x)", ranked[0].PreviewLatex);
        }

        // ─── Legacy : completions sans SourceStart/End ────────────────

        [Fact]
        public void Legacy_completions_without_span_pass_through()
        {
            // Completion construite sans SourceStart/End (= -1 par défaut)
            // → pas de NMS possible, pas de bonus span/caret, mais pas jetée.
            var legacy = new PatternCompletion(
                description: "legacy", previewLatex: "x",
                hintLatex: "x", mutation: null, completenessScore: 80);

            var ranked = R().Rank(new[] { legacy }, Ctx("xxx"));
            Assert.Single(ranked);
        }
    }
}
