using System.Collections.Generic;
using Xunit;
using MathCursor.Core.Lattice;
using MathCursor.Core.Patterns;

namespace MathCursor.Core.Tests.Patterns
{
    /// <summary>
    /// Tests unitaires purs sur <see cref="CaretLocator.FindDeepestMatchAtCaret"/>.
    /// Couvre : caret hors zone, listes vides, single match, overlap (deepest
    /// = smallest span), conventions <c>caret == Start</c> et <c>caret == End</c>.
    /// Étape P1 du plan d'organisation Patterns (ADR
    /// <c>2026-05-21-Meta-pattern-templates-vs-ambig-closed</c>).
    /// </summary>
    public class CaretLocatorTests
    {
        private static AmbiguityMatch Match(int start, int end, string ruleId = "r")
        {
            var spot = new AmbiguitySpot(ruleId, "x",
                new[] { new AmbiguityAlternative("alt") });
            return new AmbiguityMatch(spot, start, end);
        }

        [Fact]
        public void Null_matches_returns_null()
        {
            var r = CaretLocator.FindDeepestMatchAtCaret(null, 5);
            Assert.Null(r);
        }

        [Fact]
        public void Empty_matches_returns_null()
        {
            var r = CaretLocator.FindDeepestMatchAtCaret(new List<AmbiguityMatch>(), 5);
            Assert.Null(r);
        }

        [Fact]
        public void Negative_caret_returns_null()
        {
            var matches = new[] { Match(0, 5) };
            var r = CaretLocator.FindDeepestMatchAtCaret(matches, -1);
            Assert.Null(r);
        }

        [Fact]
        public void Caret_before_all_matches_returns_null()
        {
            var matches = new[] { Match(10, 12), Match(20, 22) };
            var r = CaretLocator.FindDeepestMatchAtCaret(matches, 5);
            Assert.Null(r);
        }

        [Fact]
        public void Caret_after_all_matches_returns_null()
        {
            var matches = new[] { Match(0, 2), Match(5, 7) };
            var r = CaretLocator.FindDeepestMatchAtCaret(matches, 100);
            Assert.Null(r);
        }

        [Fact]
        public void Caret_between_two_non_overlapping_matches_returns_null()
        {
            // matches = [0..2) et [5..7), caret == 3 → entre les deux, aucun ne contient.
            var matches = new[] { Match(0, 2), Match(5, 7) };
            var r = CaretLocator.FindDeepestMatchAtCaret(matches, 3);
            Assert.Null(r);
        }

        [Fact]
        public void Caret_inside_single_match_returns_that_match()
        {
            var m = Match(3, 8);
            var r = CaretLocator.FindDeepestMatchAtCaret(new[] { m }, 5);
            Assert.Same(m, r);
        }

        [Fact]
        public void Caret_at_Start_of_match_is_included()
        {
            var m = Match(3, 8);
            var r = CaretLocator.FindDeepestMatchAtCaret(new[] { m }, 3);
            Assert.Same(m, r);
        }

        [Fact]
        public void Caret_at_End_of_match_is_included_by_convention()
        {
            // Convention figée P1 : caret == End → inclus (focus reste sur le
            // match qu'on vient de finir de taper).
            var m = Match(3, 8);
            var r = CaretLocator.FindDeepestMatchAtCaret(new[] { m }, 8);
            Assert.Same(m, r);
        }

        [Fact]
        public void Overlapping_matches_returns_smallest_span()
        {
            // Outer = [0..10), span 10. Inner = [3..6), span 3. Caret 4 dans les deux.
            var outer = Match(0, 10, "outer");
            var inner = Match(3, 6, "inner");
            var r = CaretLocator.FindDeepestMatchAtCaret(new[] { outer, inner }, 4);
            Assert.Same(inner, r);
        }

        [Fact]
        public void Equal_min_span_returns_first_in_enumeration_order()
        {
            // 2 matches au même span, ordre d'énumération = ordre source.
            // Déterminisme : on prend le premier.
            var first = Match(0, 5, "first");
            var second = Match(0, 5, "second");
            var r = CaretLocator.FindDeepestMatchAtCaret(new[] { first, second }, 2);
            Assert.Same(first, r);
        }

        [Fact]
        public void Null_match_entries_are_skipped()
        {
            // Robustesse : si la liste contient un null, on l'ignore.
            var m = Match(0, 5);
            var matches = new AmbiguityMatch?[] { null, m, null };
            var r = CaretLocator.FindDeepestMatchAtCaret(matches!, 2);
            Assert.Same(m, r);
        }

        [Fact]
        public void Caret_at_boundary_between_adjacent_matches_picks_first_by_End_convention()
        {
            // matches adjacents : [0..3) et [3..6). caret == 3 :
            //   - match A : caret == End → inclus
            //   - match B : caret == Start → inclus
            // Les deux contiennent, même span (3). Premier = A (ordre source).
            var a = Match(0, 3, "a");
            var b = Match(3, 6, "b");
            var r = CaretLocator.FindDeepestMatchAtCaret(new[] { a, b }, 3);
            Assert.Same(a, r);
        }
    }
}
