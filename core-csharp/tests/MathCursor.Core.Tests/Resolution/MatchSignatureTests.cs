using System;
using MathCursor.Core.Resolution;
using Xunit;

namespace MathCursor.Core.Tests.Resolution
{
    public class MatchSignatureTests
    {
        // ─── Constructeur / validation ─────────────────────────────────

        [Fact]
        public void Ctor_validFields_setsAll()
        {
            var s = new MatchSignature("two-uppercase", "AB", 3, 0);
            Assert.Equal("two-uppercase", s.RuleId);
            Assert.Equal("AB", s.DefaultLatex);
            Assert.Equal(3, s.RawSourcePos);
            Assert.Equal(0, s.OccurrenceIdx);
        }

        [Fact]
        public void Ctor_nullRuleId_throws()
            => Assert.Throws<ArgumentNullException>(() => new MatchSignature(null!, "AB", 0, 0));

        [Fact]
        public void Ctor_nullDefaultLatex_throws()
            => Assert.Throws<ArgumentNullException>(() => new MatchSignature("rule", null!, 0, 0));

        [Theory]
        [InlineData(-1)]
        [InlineData(-100)]
        public void Ctor_negativeRawSourcePos_throws(int pos)
            => Assert.Throws<ArgumentOutOfRangeException>(() => new MatchSignature("rule", "AB", pos, 0));

        [Theory]
        [InlineData(-1)]
        [InlineData(-100)]
        public void Ctor_negativeOccurrenceIdx_throws(int occ)
            => Assert.Throws<ArgumentOutOfRangeException>(() => new MatchSignature("rule", "AB", 0, occ));

        // ─── Equals / GetHashCode ──────────────────────────────────────

        [Fact]
        public void Equals_sameFields_true()
        {
            var a = new MatchSignature("two-uppercase", "AB", 3, 0);
            var b = new MatchSignature("two-uppercase", "AB", 3, 0);
            Assert.True(a.Equals(b));
            Assert.True(a == b);
            Assert.False(a != b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void Equals_differentRule_false()
        {
            var a = new MatchSignature("two-uppercase", "AB", 3, 0);
            var b = new MatchSignature("three-uppercase", "AB", 3, 0);
            Assert.False(a.Equals(b));
            Assert.False(a == b);
        }

        [Fact]
        public void Equals_differentDefault_false()
        {
            var a = new MatchSignature("two-uppercase", "AB", 3, 0);
            var b = new MatchSignature("two-uppercase", "CD", 3, 0);
            Assert.False(a.Equals(b));
        }

        [Fact]
        public void Equals_differentPos_false()
        {
            var a = new MatchSignature("two-uppercase", "AB", 3, 0);
            var b = new MatchSignature("two-uppercase", "AB", 7, 0);
            Assert.False(a.Equals(b));
        }

        [Fact]
        public void Equals_differentOccurrenceIdx_false()
        {
            // Cas AB+CD=AB : 1ʳᵉ et 2ᵉ occurrences distinctes même si
            // RuleId et DefaultLatex sont identiques.
            var a = new MatchSignature("two-uppercase", "AB", 0, 0);
            var b = new MatchSignature("two-uppercase", "AB", 6, 1);
            Assert.False(a.Equals(b));
        }

        [Fact]
        public void Equals_null_false()
        {
            var a = new MatchSignature("two-uppercase", "AB", 3, 0);
            Assert.False(a.Equals(null));
            Assert.False(a == null);
            Assert.True(a != null);
        }

        [Fact]
        public void Equals_object_dispatches()
        {
            var a = new MatchSignature("two-uppercase", "AB", 3, 0);
            object b = new MatchSignature("two-uppercase", "AB", 3, 0);
            Assert.True(a.Equals(b));
            Assert.False(a.Equals("not a signature"));
            Assert.False(a.Equals(null));
        }

        // ─── ToString (utile pour debug / Inspector) ───────────────────

        [Fact]
        public void ToString_includesAllFields()
        {
            var s = new MatchSignature("two-uppercase", "AB", 3, 1);
            var t = s.ToString();
            Assert.Contains("two-uppercase", t);
            Assert.Contains("AB", t);
            Assert.Contains("3", t);
            Assert.Contains("1", t);
        }
    }

    public class RulePinTests
    {
        [Fact]
        public void Ctor_validFields_setsAll()
        {
            var p = new RulePin("two-uppercase", 0);
            Assert.Equal("two-uppercase", p.RuleId);
            Assert.Equal(0, p.AltIdx);
        }

        [Fact]
        public void Ctor_nullRule_throws()
            => Assert.Throws<ArgumentNullException>(() => new RulePin(null!, 0));

        [Fact]
        public void Ctor_negativeAltIdx_throws()
            => Assert.Throws<ArgumentOutOfRangeException>(() => new RulePin("rule", -1));

        [Fact]
        public void Equals_sameFields_true()
        {
            var a = new RulePin("two-uppercase", 0);
            var b = new RulePin("two-uppercase", 0);
            Assert.True(a.Equals(b));
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void Equals_differentAlt_false()
        {
            var a = new RulePin("two-uppercase", 0);
            var b = new RulePin("two-uppercase", 1);
            Assert.False(a.Equals(b));
        }

        [Fact]
        public void Equals_differentRule_false()
        {
            var a = new RulePin("two-uppercase", 0);
            var b = new RulePin("canonical-set", 0);
            Assert.False(a.Equals(b));
        }
    }

    public class SpanOverrideTests
    {
        private static MatchSignature SigAB => new MatchSignature("two-uppercase", "AB", 3, 0);

        [Fact]
        public void Ctor_validFields_setsAll()
        {
            var o = new SpanOverride(SigAB, 1);
            Assert.Equal(SigAB, o.Signature);
            Assert.Equal(1, o.AltIdx);
            Assert.False(o.IsRevert);
        }

        [Fact]
        public void Ctor_revertAltIdx_isRevertTrue()
        {
            var o = new SpanOverride(SigAB, SpanOverride.AltIdxRevert);
            Assert.True(o.IsRevert);
            Assert.Equal(-1, o.AltIdx);
        }

        [Fact]
        public void Ctor_nullSignature_throws()
            => Assert.Throws<ArgumentNullException>(() => new SpanOverride(null!, 0));

        [Fact]
        public void Ctor_altIdxBelowRevert_throws()
            => Assert.Throws<ArgumentOutOfRangeException>(() => new SpanOverride(SigAB, -2));

        [Fact]
        public void AltIdxRevert_constantIsMinusOne()
            => Assert.Equal(-1, SpanOverride.AltIdxRevert);

        [Fact]
        public void Equals_sameSignatureAndAlt_true()
        {
            var a = new SpanOverride(SigAB, 1);
            var b = new SpanOverride(SigAB, 1);
            Assert.True(a.Equals(b));
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void Equals_differentSignature_false()
        {
            var a = new SpanOverride(new MatchSignature("two-uppercase", "AB", 0, 0), 1);
            var b = new SpanOverride(new MatchSignature("two-uppercase", "AB", 6, 1), 1);
            Assert.False(a.Equals(b));
        }

        [Fact]
        public void Equals_differentAlt_false()
        {
            var a = new SpanOverride(SigAB, 0);
            var b = new SpanOverride(SigAB, 1);
            Assert.False(a.Equals(b));
        }

        [Fact]
        public void ToString_revert_distinguishable()
        {
            var revert = new SpanOverride(SigAB, SpanOverride.AltIdxRevert);
            var alt = new SpanOverride(SigAB, 1);
            Assert.Contains("revert", revert.ToString());
            Assert.Contains("alt 1", alt.ToString());
        }
    }
}
