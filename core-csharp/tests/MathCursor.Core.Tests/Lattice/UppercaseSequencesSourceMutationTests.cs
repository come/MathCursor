using MathCursor.Core;
using MathCursor.Core.Lattice;
using Xunit;

namespace MathCursor.Core.Tests.Lattice
{
    /// <summary>
    /// Invariants S1 du refacto source-mutation
    /// (ADR <c>2026-05-13-Refactor-source-mutation-pins-sidecar</c>) sur
    /// <see cref="AlternativeGenerator.ScanUppercaseSequences"/> /
    /// <see cref="AmbiguityAlternative.Mutation"/>.
    ///
    /// Scope S1 : <c>RuleTwoUppercase</c> reçoit <see cref="SourceMutation"/>
    /// sur les alts vec et paren ; bracket reste sur fallback splice latex
    /// (intervalle FR empêche une source-mut naturelle) ; <c>RuleThreeUppercase</c>
    /// inchangé (sera traité en S2).
    /// </summary>
    public sealed class UppercaseSequencesSourceMutationTests
    {
        private readonly LatticeEngine _engine = new LatticeEngine();

        [Fact]
        public void S1_TwoUpperPair_VecAlt_HasSourceMutation()
        {
            var r = _engine.ConvertWithAmbiguity("AB");
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleTwoUppercase, r.Spot!.RuleId);

            // Ordre alts cf. MakeUpperSpot : [0]=vec, [1]=paren, [2]=bracket.
            var vec = r.Spot.Alternatives[0];
            Assert.Equal("\\vec{AB}", vec.Latex);
            Assert.NotNull(vec.Mutation);
            Assert.Equal(0, vec.Mutation!.Offset);
            Assert.Equal(2, vec.Mutation.Length);
            Assert.Equal("vec AB", vec.Mutation.Replacement);
        }

        [Fact]
        public void S1_TwoUpperPair_ParenAlt_HasSourceMutation()
        {
            var r = _engine.ConvertWithAmbiguity("AB");
            Assert.NotNull(r.Spot);

            var paren = r.Spot!.Alternatives[1];
            Assert.Equal("\\left(AB\\right)", paren.Latex);
            Assert.NotNull(paren.Mutation);
            Assert.Equal(0, paren.Mutation!.Offset);
            Assert.Equal(2, paren.Mutation.Length);
            Assert.Equal("(AB)", paren.Mutation.Replacement);
        }

        [Fact]
        public void S1_TwoUpperPair_BracketAlt_HasNoSourceMutation()
        {
            var r = _engine.ConvertWithAmbiguity("AB");
            Assert.NotNull(r.Spot);

            var bracket = r.Spot!.Alternatives[2];
            Assert.Equal("\\left[AB\\right]", bracket.Latex);
            Assert.Null(bracket.Mutation);
        }

        [Fact]
        public void S1_ThreeUpperTriplet_AltsHaveNoSourceMutation()
        {
            // S1 ne touche pas RuleThreeUppercase : toutes les alts restent
            // sans Mutation (fallback splice latex préservé pour widehat
            // et triangle).
            var r = _engine.ConvertWithAmbiguity("ABC");
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleThreeUppercase, r.Spot!.RuleId);
            foreach (var alt in r.Spot.Alternatives)
                Assert.Null(alt.Mutation);
        }
    }
}
