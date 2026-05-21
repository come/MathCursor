using System.Collections.Generic;
using Xunit;
using MathCursor.Core;
using MathCursor.Core.Lattice;
using MathCursor.Core.Resolution;

namespace MathCursor.Core.Tests.Resolution
{
    /// <summary>
    /// Tests de <see cref="PopupAltFilter"/> + scenarios end-to-end
    /// ZoneResolver → Filter pour valider que la popup filtre bien l'alt
    /// active de la liste affichée. Cf. bugs rapportés 2026-05-21 :
    /// <list type="bullet">
    /// <item>tight-chain-extension : default (alt[0]) toujours visible</item>
    /// <item>two-uppercase AB+AC=AD : click [AB] ne fonctionne pas</item>
    /// </list>
    /// </summary>
    public class PopupAltFilterTests
    {
        // ─── Filter pur (sans engine) ────────────────────────────────

        [Fact]
        public void Filter_empty_alts_returns_empty()
        {
            var r = PopupAltFilter.Filter(0, 0, null, null, "");
            Assert.Empty(r.Built);
            Assert.Empty(r.AltIdxMap);
            Assert.Equal(-1, r.ActiveAltIdx);
        }

        [Fact]
        public void Filter_no_active_alt_shows_all_alts_no_revert()
        {
            var alts = new[]
            {
                new AmbiguityAlternative("\\vec{AB}"),
                new AmbiguityAlternative("\\left(AB\\right)"),
                new AmbiguityAlternative("\\left[AB\\right]"),
            };
            var matches = new[]
            {
                new AmbiguityMatch(new AmbiguitySpot("two-uppercase", "AB", alts), 0, 2),
            };
            var r = PopupAltFilter.Filter(0, 2, alts, matches, "AB");

            Assert.Equal(3, r.Built.Count);
            Assert.Equal(new[] { 0, 1, 2 }, r.AltIdxMap);
            Assert.Equal(-1, r.ActiveAltIdx);
        }

        [Fact]
        public void Filter_with_AppliedAltIdx_excludes_active_and_prepends_revert()
        {
            var alts = new[]
            {
                new AmbiguityAlternative("\\vec{AB}"),
                new AmbiguityAlternative("\\left(AB\\right)"),
                new AmbiguityAlternative("\\left[AB\\right]"),
            };
            var spot = new AmbiguitySpot("two-uppercase", "AB", alts);
            // Match avec AppliedAltIdx=0 (= vec) → vec doit être exclu, revert ajouté.
            var matches = new[] { new AmbiguityMatch(spot, 0, 2).WithAppliedAlt(0) };
            var r = PopupAltFilter.Filter(0, 2, alts, matches, "AB");

            Assert.Equal(3, r.Built.Count); // revert + paren + bracket
            Assert.Equal(SpanOverride.AltIdxRevert, r.AltIdxMap[0]);
            Assert.Equal("AB", r.Built[0].Latex); // revert = defaultLatex
            Assert.Equal(1, r.AltIdxMap[1]); // paren
            Assert.Equal(2, r.AltIdxMap[2]); // bracket
            Assert.Equal(0, r.ActiveAltIdx);
        }

        [Fact]
        public void Filter_AppliedAltIdx_only_for_match_matching_spot_bounds()
        {
            // Match avec bornes différentes du spot → NE doit PAS être considéré
            // comme active (= invariant : seul le match au même span que le
            // Spot affiché compte).
            var alts = new[] { new AmbiguityAlternative("alt0"), new AmbiguityAlternative("alt1") };
            var spot = new AmbiguitySpot("R", "D", alts);
            var matches = new[]
            {
                new AmbiguityMatch(spot, 10, 20).WithAppliedAlt(0), // span différent
            };
            var r = PopupAltFilter.Filter(0, 2, alts, matches, "D");
            Assert.Equal(-1, r.ActiveAltIdx);
            Assert.Equal(2, r.Built.Count); // no revert, no filter
        }

        [Fact]
        public void Filter_no_match_no_active()
        {
            var alts = new[] { new AmbiguityAlternative("a0"), new AmbiguityAlternative("a1") };
            // Pas de match (allMatches == null) → activeAltIdx reste -1 →
            // toutes les alts s'affichent, pas de revert.
            var r = PopupAltFilter.Filter(0, 2, alts, null, "D");
            Assert.Equal(-1, r.ActiveAltIdx);
            Assert.Equal(2, r.Built.Count);
            Assert.Equal(0, r.AltIdxMap[0]);
            Assert.Equal(1, r.AltIdxMap[1]);
        }

        // ─── End-to-end ZoneResolver → Filter ────────────────────────

        [Fact]
        public void E2E_Lim_filters_default_alt_via_DefaultLatex_match()
        {
            // Sans pref user mais alt[0].Latex == Spot.DefaultLatex →
            // Resolver annote AppliedAltIdx=0 (fallback default rendering)
            // → filter exclut alt[0] + ajoute revert pour éviter doublon
            // visuel popup-alts ↔ final. Cf. UX 2026-05-21.
            var engine = new LatticeEngine();
            var resolver = new ZoneResolver(engine);
            var r = resolver.Resolve("Lim x 0 f(x)= 1/x+1 *4");

            var filtered = PopupAltFilter.Filter(
                r.SpotStart ?? -1, r.SpotEnd ?? -1,
                r.Spot.Alternatives, r.AllMatches, r.Spot.DefaultLatex);

            Assert.Equal(0, filtered.ActiveAltIdx);
            Assert.Equal(2, filtered.Built.Count); // revert + alt[1]
            Assert.Equal(SpanOverride.AltIdxRevert, filtered.AltIdxMap[0]);
            Assert.Equal(1, filtered.AltIdxMap[1]);
        }

        [Fact]
        public void E2E_Lim_with_pref_filters_active_alt()
        {
            // Avec pref alt[1], filter exclut alt[1] + ajoute revert.
            var engine = new LatticeEngine();
            var resolver = new ZoneResolver(engine);
            resolver.AddPreference("tight-chain-extension", 1);
            var r = resolver.Resolve("Lim x 0 f(x)= 1/x+1 *4");

            var filtered = PopupAltFilter.Filter(
                r.SpotStart ?? -1, r.SpotEnd ?? -1,
                r.Spot.Alternatives, r.AllMatches, r.Spot.DefaultLatex);

            Assert.Equal(1, filtered.ActiveAltIdx);
            Assert.Equal(2, filtered.Built.Count); // revert + alt[0]
            Assert.Equal(SpanOverride.AltIdxRevert, filtered.AltIdxMap[0]);
            Assert.Equal(0, filtered.AltIdxMap[1]);
        }

        [Fact]
        public void E2E_TwoUppercase_default_no_filter_when_no_alt_matches_default()
        {
            // Pour two-uppercase, DefaultLatex = "AB" mais aucune alt ne
            // l'a comme Latex (les alts ont vec/paren/bracket décorés).
            // Donc pas de filter, 3 alts affichés.
            var engine = new LatticeEngine();
            var resolver = new ZoneResolver(engine);
            var r = resolver.Resolve("AB+AC=AD");

            var spotStart = r.SpotStart ?? -1;
            var spotEnd = r.SpotEnd ?? -1;
            var defaultLatex = r.Spot.DefaultLatex;
            var filtered = PopupAltFilter.Filter(
                spotStart, spotEnd, r.Spot.Alternatives, r.AllMatches, defaultLatex);

            Assert.Equal(-1, filtered.ActiveAltIdx);
            Assert.Equal(3, filtered.Built.Count); // les 3 alts
        }

        [Fact]
        public void E2E_TwoUppercase_after_pick_vec_only_paren_bracket_remain()
        {
            // Bug user : sur AB+AC=AD, après pick vec, click [AB] (bracket) doit fonctionner.
            var engine = new LatticeEngine();
            var resolver = new ZoneResolver(engine);
            resolver.AddPreference("two-uppercase", 0); // vec
            var r = resolver.Resolve("AB+AC=AD");

            var defaultLatex = r.Spot.DefaultLatex;
            var filtered = PopupAltFilter.Filter(
                r.SpotStart ?? -1, r.SpotEnd ?? -1,
                r.Spot.Alternatives, r.AllMatches, defaultLatex);

            // Active = vec (0), donc filtered = [revert, paren(1), bracket(2)].
            Assert.Equal(0, filtered.ActiveAltIdx);
            Assert.Equal(3, filtered.Built.Count);
            Assert.Equal(SpanOverride.AltIdxRevert, filtered.AltIdxMap[0]);
            Assert.Equal(1, filtered.AltIdxMap[1]); // paren
            Assert.Equal(2, filtered.AltIdxMap[2]); // bracket
            // Si user click display=2, realAltIdx = altIdxMap[2] = 2 (bracket). CORRECT.
        }

        [Fact]
        public void E2E_TwoUppercase_pref_change_keeps_consistent_mapping()
        {
            // Scenario user : pick vec, puis pick paren, puis pick bracket.
            var engine = new LatticeEngine();
            var resolver = new ZoneResolver(engine);

            resolver.AddPreference("two-uppercase", 0); // vec
            var r1 = resolver.Resolve("AB+AC=AD");
            var f1 = PopupAltFilter.Filter(r1.SpotStart ?? -1, r1.SpotEnd ?? -1, r1.Spot.Alternatives, r1.AllMatches, r1.Spot.DefaultLatex);
            Assert.Equal(0, f1.ActiveAltIdx);
            // map = [revert, 1, 2]
            Assert.Equal(SpanOverride.AltIdxRevert, f1.AltIdxMap[0]);
            Assert.Equal(1, f1.AltIdxMap[1]);
            Assert.Equal(2, f1.AltIdxMap[2]);

            // User click display=2 (= bracket) → real=2 → AddPreference(rule, 2)
            resolver.AddPreference("two-uppercase", 2);
            var r2 = resolver.Resolve("AB+AC=AD");
            var f2 = PopupAltFilter.Filter(r2.SpotStart ?? -1, r2.SpotEnd ?? -1, r2.Spot.Alternatives, r2.AllMatches, r2.Spot.DefaultLatex);
            Assert.Equal(2, f2.ActiveAltIdx);
            // map = [revert, 0, 1] (= bracket exclu)
            Assert.Equal(SpanOverride.AltIdxRevert, f2.AltIdxMap[0]);
            Assert.Equal(0, f2.AltIdxMap[1]); // vec
            Assert.Equal(1, f2.AltIdxMap[2]); // paren
        }
    }
}
