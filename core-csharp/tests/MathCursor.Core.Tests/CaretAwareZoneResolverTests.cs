using Xunit;
using MathCursor.Core;
using MathCursor.Core.Lattice;

namespace MathCursor.Core.Tests
{
    /// <summary>
    /// Tests d'intégration <see cref="ZoneResolver"/> + caret-aware.
    /// Vérifie que le paramètre <c>caretOffset</c> ajusté repositionne le
    /// <c>Spot</c> sur le match-le-plus-profond contenant le caret, et que
    /// <c>caretOffset == null</c> préserve le comportement legacy
    /// (rightmost). Étape P1 du plan Patterns (ADR
    /// <c>2026-05-21-Meta-pattern-templates-vs-ambig-closed</c>).
    /// </summary>
    public class CaretAwareZoneResolverTests
    {
        private static ZoneResolver NewResolver() => new ZoneResolver(new LatticeEngine());

        // ─── AB+AC=AD : 3 matches two-uppercase, caret choisit le focus ──

        [Fact]
        public void Caret_null_preserves_legacy_rightmost_on_ABACAD()
        {
            var r = NewResolver().Resolve("AB+AC=AD");
            Assert.True(r.AllMatches.Count >= 2);
            // rightmost = AD (le plus à droite). Spot doit le pointer.
            var rightmost = r.AllMatches[r.AllMatches.Count - 1];
            Assert.Equal(rightmost.Start, r.SpotStart);
            Assert.Equal(rightmost.End, r.SpotEnd);
        }

        [Fact]
        public void Caret_inside_first_match_focuses_on_it()
        {
            // "AB+AC=AD" — caret à la position 1 (= au milieu de AB)
            var r = NewResolver().Resolve("AB+AC=AD", caretOffset: 1);
            Assert.NotNull(r.Spot);
            // Spot doit être le match qui contient 1 (= AB, span [0..2])
            Assert.Equal(0, r.SpotStart);
            Assert.Equal(2, r.SpotEnd);
        }

        [Fact]
        public void Caret_inside_middle_match_focuses_on_it()
        {
            // "AB+AC=AD" — caret au milieu de AC (position 4)
            // Positions topLatex : A=0 B=1 +=2 A=3 C=4 ==5 A=6 D=7
            var r = NewResolver().Resolve("AB+AC=AD", caretOffset: 4);
            Assert.NotNull(r.Spot);
            // Spot doit être le match qui contient 4 (= AC, span [3..5])
            Assert.Equal(3, r.SpotStart);
            Assert.Equal(5, r.SpotEnd);
        }

        [Fact]
        public void Caret_inside_last_match_matches_legacy_rightmost()
        {
            // "AB+AC=AD" — caret au milieu de AD. Coïncide avec rightmost
            // legacy → comportement identique au cas caret==null.
            var r = NewResolver().Resolve("AB+AC=AD", caretOffset: 7);
            Assert.NotNull(r.Spot);
            Assert.Equal(6, r.SpotStart);
            Assert.Equal(8, r.SpotEnd);
        }

        [Fact]
        public void Caret_outside_any_match_falls_back_to_legacy_rightmost()
        {
            // "AB+AC=AD" — caret 2 = pile sur le '+'. Cas limite : caret==End
            // de AB (2) → AB inclus par convention. Donc Spot = AB.
            // Test du fallback : caret hors zone (100) → legacy.
            var r = NewResolver().Resolve("AB+AC=AD", caretOffset: 100);
            Assert.NotNull(r.Spot);
            // Aucun match ne contient 100 → fallback rightmost = AD
            var rightmost = r.AllMatches[r.AllMatches.Count - 1];
            Assert.Equal(rightmost.Start, r.SpotStart);
            Assert.Equal(rightmost.End, r.SpotEnd);
        }

        [Fact]
        public void Caret_at_End_of_match_is_included_by_convention()
        {
            // "AB+AC=AD" — caret == 2 = End de AB. Convention CaretLocator :
            // caret == End inclus. Donc Spot = AB (pas AC qui commence à 3).
            var r = NewResolver().Resolve("AB+AC=AD", caretOffset: 2);
            Assert.NotNull(r.Spot);
            Assert.Equal(0, r.SpotStart);
            Assert.Equal(2, r.SpotEnd);
        }

        [Fact]
        public void Caret_negative_falls_back_to_legacy_rightmost()
        {
            var r = NewResolver().Resolve("AB+AC=AD", caretOffset: -1);
            Assert.NotNull(r.Spot);
            var rightmost = r.AllMatches[r.AllMatches.Count - 1];
            Assert.Equal(rightmost.Start, r.SpotStart);
            Assert.Equal(rightmost.End, r.SpotEnd);
        }

        // ─── Lim tight-chain : 1 seul match, caret partout préserve ───

        [Fact]
        public void Caret_inside_unique_tightchain_match_returns_it()
        {
            // "Lim x 0 f(x)= 1/x+1 *4" : 1 match (tight-chain-extension).
            var resolver = NewResolver();
            var r = resolver.Resolve("Lim x 0 f(x)= 1/x+1 *4");
            Assert.Single(r.AllMatches);
            var only = r.AllMatches[0];

            // Caret au milieu de ce match → même match.
            var rWithCaret = resolver.Resolve("Lim x 0 f(x)= 1/x+1 *4",
                caretOffset: (only.Start + only.End) / 2);
            Assert.Equal(only.Start, rWithCaret.SpotStart);
            Assert.Equal(only.End, rWithCaret.SpotEnd);
        }

        [Fact]
        public void Caret_null_on_tightchain_preserves_legacy()
        {
            // caret null = comportement avant P1, intact.
            var r = NewResolver().Resolve("Lim x 0 f(x)= 1/x+1 *4");
            Assert.NotNull(r.Spot);
            Assert.Equal("tight-chain-extension", r.Spot!.RuleId);
        }

        // ─── Overload Resolve(rawSource, sidecar, caretOffset) ────────────

        [Fact]
        public void Overload_sidecar_passes_caret_through()
        {
            var resolver = NewResolver();
            var empty = MathCursor.Core.Resolution.ResolutionSidecar.Empty;
            // Sidecar vide → délègue à Resolve(rawSource, caretOffset).
            var r = resolver.Resolve("AB+AC=AD", empty, caretOffset: 4);
            Assert.NotNull(r.Spot);
            Assert.Equal(3, r.SpotStart);
            Assert.Equal(5, r.SpotEnd);
        }

        // ─── Overload Resolve(rawSource, globalCtx, sidecar, caretOffset) ─

        [Fact]
        public void Overload_globalctx_short_circuit_applies_caret()
        {
            // globalCtx vide + sidecar null → court-circuit (baseResolved retourné).
            // Le caret doit quand même être appliqué.
            var resolver = NewResolver();
            var r = resolver.Resolve("AB+AC=AD", globalCtx: null, sidecar: null, caretOffset: 1);
            Assert.NotNull(r.Spot);
            Assert.Equal(0, r.SpotStart);
            Assert.Equal(2, r.SpotEnd);
        }
    }
}
