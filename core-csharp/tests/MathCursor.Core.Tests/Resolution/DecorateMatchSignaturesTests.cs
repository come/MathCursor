using MathCursor.Core;
using MathCursor.Core.Lattice;
using MathCursor.Core.Resolution;
using Xunit;

namespace MathCursor.Core.Tests.Resolution
{
    /// <summary>
    /// Tests de la décoration MatchSignature post-scan, exposée via le
    /// chemin <see cref="ZoneResolver.Resolve(string)"/>. Vérifie que les
    /// AmbiguityMatch retournés ont une Signature cohérente : OccurrenceIdx
    /// croissant pour les répétitions du même (rule, default).
    /// </summary>
    public class DecorateMatchSignaturesTests
    {
        private static ZoneResolver MakeResolver()
            => new ZoneResolver(LatticeEngine.LoadEmbedded("fr"));

        [Fact]
        public void Single_match_has_signature_with_occ_zero()
        {
            // "AB" → un seul match two-uppercase.
            var resolved = MakeResolver().Resolve("AB");
            Assert.Single(resolved.AllMatches);
            var m = resolved.AllMatches[0];
            Assert.NotNull(m.Signature);
            Assert.Equal("two-uppercase", m.Signature!.RuleId);
            Assert.Equal("AB", m.Signature.DefaultLatex);
            Assert.Equal(0, m.Signature.OccurrenceIdx);
        }

        [Fact]
        public void Two_distinct_pairs_have_independent_occurrence_indices()
        {
            // "AB+CD" : 2 matches two-uppercase, paires différentes →
            // OccurrenceIdx = 0 chacun (compteurs indépendants par DefaultLatex).
            var resolved = MakeResolver().Resolve("AB+CD");
            Assert.Equal(2, resolved.AllMatches.Count);
            foreach (var m in resolved.AllMatches)
            {
                Assert.NotNull(m.Signature);
                Assert.Equal(0, m.Signature!.OccurrenceIdx);
            }
        }

        [Fact]
        public void Repeated_same_pair_increments_occurrence_idx()
        {
            // "AB+CD=AB" : 3 matches dont 2 "AB". L'occurrenceIdx
            // discrimine la 1ʳᵉ AB (0) de la 2ᵉ AB (1).
            var resolved = MakeResolver().Resolve("AB+CD=AB");
            Assert.Equal(3, resolved.AllMatches.Count);

            int abCount = 0, cdCount = 0;
            foreach (var m in resolved.AllMatches)
            {
                Assert.NotNull(m.Signature);
                if (m.Signature!.DefaultLatex == "AB")
                {
                    Assert.Equal(abCount, m.Signature.OccurrenceIdx);
                    abCount++;
                }
                else if (m.Signature.DefaultLatex == "CD")
                {
                    Assert.Equal(cdCount, m.Signature.OccurrenceIdx);
                    cdCount++;
                }
            }
            Assert.Equal(2, abCount); // 2 occurrences de AB → idx 0 puis 1
            Assert.Equal(1, cdCount); // 1 occurrence de CD → idx 0
        }

        [Fact]
        public void Signature_RawSourcePos_matches_match_Start()
        {
            // V1 : RawSourcePos = match.Start (= position dans topLatex).
            var resolved = MakeResolver().Resolve("AB+CD=AB");
            foreach (var m in resolved.AllMatches)
            {
                Assert.NotNull(m.Signature);
                Assert.Equal(m.Start, m.Signature!.RawSourcePos);
            }
        }

        [Fact]
        public void Signature_distinguishes_different_rules()
        {
            // "ABC" → trois lettres = rule three-uppercase (pas two-uppercase).
            var resolved = MakeResolver().Resolve("ABC");
            Assert.Single(resolved.AllMatches);
            var m = resolved.AllMatches[0];
            Assert.NotNull(m.Signature);
            Assert.Equal("three-uppercase", m.Signature!.RuleId);
        }

        [Fact]
        public void Empty_source_no_matches()
        {
            var resolved = MakeResolver().Resolve("");
            Assert.Empty(resolved.AllMatches);
        }

        [Fact]
        public void Plain_arithmetic_no_two_uppercase_match()
        {
            // "1+2" : pas de paire de majuscules → 0 matches two-uppercase.
            var resolved = MakeResolver().Resolve("1+2");
            foreach (var m in resolved.AllMatches)
            {
                Assert.NotEqual("two-uppercase", m.Spot.RuleId);
            }
        }
    }
}
