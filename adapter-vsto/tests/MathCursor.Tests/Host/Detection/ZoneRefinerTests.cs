using System.Collections.Generic;
using MathCursor.Detection;
using MathCursor.Host.Detection;
using Xunit;

namespace MathCursor.Tests.Host.Detection
{
    /// <summary>
    /// Tests pour <see cref="ZoneRefiner"/> — helpers purs de raffinage
    /// d'une zone NER avant l'engine. Bounded context P2.14 du refactor archi.
    /// </summary>
    public sealed class ZoneRefinerTests
    {
        // Phase 2 beta-clean : la table vit dans ZoneRefiner (le YAML
        // appartenait à l'ancien moteur supprimé).
        private static readonly ISet<string> _keywords = ZoneRefiner.DefaultMathPrefixKeywords;

        private static DetectedZone Zone(int s, int e, string text)
            => new DetectedZone(s, e, text, 0.95f);

        // ── TryExtendForwardWhitespace ────────────────────────────────

        [Fact]
        public void TryExtendForwardWhitespace_caret_at_zone_end_returns_zone_unchanged()
        {
            var z = Zone(0, 5, "hello");
            var result = ZoneRefiner.TryExtendForwardWhitespace("hello", z, caret: 5);
            Assert.Same(z, result);
        }

        [Fact]
        public void TryExtendForwardWhitespace_whitespace_between_extends()
        {
            // "hello   " : caret en 8, zone se termine en 5 → ws between → extend
            var z = Zone(0, 5, "hello");
            var result = ZoneRefiner.TryExtendForwardWhitespace("hello   ", z, caret: 8);
            Assert.NotSame(z, result);
            Assert.Equal(8, result.End);
            Assert.Equal("hello   ", result.Text);
        }

        [Fact]
        public void TryExtendForwardWhitespace_non_whitespace_blocks_extension()
        {
            // "hello abc" : caret en 9, mais 'a' à pos 6 n'est pas ws → pas étendu
            var z = Zone(0, 5, "hello");
            var result = ZoneRefiner.TryExtendForwardWhitespace("hello abc", z, caret: 9);
            Assert.Same(z, result);
        }

        [Fact]
        public void TryExtendForwardWhitespace_gap_too_large_returns_unchanged()
        {
            // 6 ws entre → > 5 chars seuil
            var z = Zone(0, 5, "hello");
            var result = ZoneRefiner.TryExtendForwardWhitespace("hello      ", z, caret: 11);
            Assert.Same(z, result);
        }

        // ── ExtendBackwardWithKeyword ─────────────────────────────────

        [Fact]
        public void ExtendBackwardWithKeyword_with_lim_keyword_extends()
        {
            // "lim x" : zone = "x" → absorb "lim"
            var z = Zone(4, 5, "x");
            var result = ZoneRefiner.ExtendBackwardWithKeyword("lim x", z, _keywords);
            Assert.Equal(0, result.Start);
            Assert.Equal("lim x", result.Text);
        }

        [Fact]
        public void ExtendBackwardWithKeyword_with_unknown_word_returns_unchanged()
        {
            var z = Zone(4, 5, "x");
            var result = ZoneRefiner.ExtendBackwardWithKeyword("bla x", z, _keywords);
            Assert.Same(z, result);
        }

        [Fact]
        public void ExtendBackwardWithKeyword_case_insensitive()
        {
            var z = Zone(4, 5, "x");
            var result = ZoneRefiner.ExtendBackwardWithKeyword("LIM x", z, _keywords);
            Assert.Equal(0, result.Start);
        }

        [Fact]
        public void ExtendBackwardWithKeyword_at_paragraph_start_no_keyword()
        {
            var z = Zone(0, 5, "hello");
            var result = ZoneRefiner.ExtendBackwardWithKeyword("hello", z, _keywords);
            Assert.Same(z, result);
        }

        // ── FilterOutOMathOverlap ─────────────────────────────────────

        [Fact]
        public void FilterOutOMathOverlap_no_overlap_keeps_all()
        {
            var zones = new[] { Zone(0, 5, "abc"), Zone(10, 15, "xyz") };
            var regions = new List<(int, int)> { (20, 25) };
            var result = ZoneRefiner.FilterOutOMathOverlap(zones, regions);
            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void FilterOutOMathOverlap_full_overlap_drops()
        {
            var zones = new[] { Zone(0, 5, "abc"), Zone(10, 15, "xyz") };
            var regions = new List<(int, int)> { (10, 15) };
            var result = ZoneRefiner.FilterOutOMathOverlap(zones, regions);
            Assert.Single(result);
            Assert.Equal(0, result[0].Start);
        }

        [Fact]
        public void FilterOutOMathOverlap_partial_overlap_drops()
        {
            var zones = new[] { Zone(0, 8, "abcdefg") };
            var regions = new List<(int, int)> { (5, 10) };
            var result = ZoneRefiner.FilterOutOMathOverlap(zones, regions);
            Assert.Empty(result);
        }

        [Fact]
        public void FilterOutOMathOverlap_empty_inputs_handled()
        {
            Assert.Empty(ZoneRefiner.FilterOutOMathOverlap(new DetectedZone[0], new List<(int, int)>()));
            // null in → empty out (defensive, pas null pour éviter NRE chez le caller).
            Assert.Empty(ZoneRefiner.FilterOutOMathOverlap(null, new List<(int, int)>()));
        }

        // ── PickNearestZone ───────────────────────────────────────────

        [Fact]
        public void PickNearestZone_caret_inside_distance_zero()
        {
            var zones = new[] { Zone(0, 5, "abc"), Zone(10, 15, "xyz") };
            var best = ZoneRefiner.PickNearestZone(zones, caret: 12, out int d);
            Assert.Equal(0, d);
            Assert.Equal(10, best.Start);
        }

        [Fact]
        public void PickNearestZone_caret_before_picks_closest()
        {
            var zones = new[] { Zone(10, 15, "abc"), Zone(20, 25, "xyz") };
            var best = ZoneRefiner.PickNearestZone(zones, caret: 8, out int d);
            Assert.Equal(2, d);
            Assert.Equal(10, best.Start);
        }

        // ── MergeWhitespaceAdjacent (fragmentation NER, 2026-06-12) ────

        [Fact]
        public void Merge_joins_zones_separated_by_single_space()
        {
            // « (a b c d ; | e (sum x 0 1 » : le NER coupe la formule en 2 —
            // la fusion redonne la zone entière, parsable par le moteur.
            var text = "(a b c d ; e (sum x 0 1";
            var zones = new[] { Zone(0, 10, "(a b c d ;"), Zone(11, 23, "e (sum x 0 1") };
            var merged = ZoneRefiner.MergeWhitespaceAdjacent(zones, text, zones[1]);
            Assert.Equal(0, merged.Start);
            Assert.Equal(23, merged.End);
            Assert.Equal("(a b c d ; e (sum x 0 1".Substring(0, 23), merged.Text);
        }

        [Fact]
        public void Merge_does_not_join_across_prose()
        {
            // « x=1 et y=2 » : le gap contient des lettres → pas de fusion.
            var text = "x=1 et y=2";
            var zones = new[] { Zone(0, 3, "x=1"), Zone(7, 10, "y=2") };
            var merged = ZoneRefiner.MergeWhitespaceAdjacent(zones, text, zones[1]);
            Assert.Same(zones[1], merged);
        }

        [Fact]
        public void Merge_chains_multiple_adjacent_zones()
        {
            var text = "aa bb cc";
            var zones = new[] { Zone(0, 2, "aa"), Zone(3, 5, "bb"), Zone(6, 8, "cc") };
            var merged = ZoneRefiner.MergeWhitespaceAdjacent(zones, text, zones[1]);
            Assert.Equal(0, merged.Start);
            Assert.Equal(8, merged.End);
        }

        [Fact]
        public void Merge_respects_max_gap()
        {
            var text = "aa      bb"; // 6 espaces > maxGap 3
            var zones = new[] { Zone(0, 2, "aa"), Zone(8, 10, "bb") };
            var merged = ZoneRefiner.MergeWhitespaceAdjacent(zones, text, zones[0]);
            Assert.Same(zones[0], merged);
        }

        [Fact]
        public void PickNearestZone_caret_after_picks_closest()
        {
            var zones = new[] { Zone(0, 5, "abc"), Zone(20, 25, "xyz") };
            var best = ZoneRefiner.PickNearestZone(zones, caret: 27, out int d);
            Assert.Equal(2, d);
            Assert.Equal(20, best.Start);
        }

        // ── MathPrefixKeywords data-driven ─────────────────────────────
        // Migration Chantier 1 — 2026-05-25 : la liste est maintenant dans
        // data-v2/locale/fr.yml `math_prefix_keywords:`.

        [Fact]
        public void MathPrefixKeywords_contains_common_terms()
        {
            Assert.Contains("lim", _keywords);
            Assert.Contains("racine", _keywords);
            Assert.Contains("somme", _keywords);
            Assert.Contains("vec", _keywords);
        }
    }
}
