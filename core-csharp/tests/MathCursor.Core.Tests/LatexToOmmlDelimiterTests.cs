using System.Linq;
using System.Xml.Linq;
using Xunit;
using MathCursor.Core;

namespace MathCursor.Core.Tests
{
    /// <summary>
    /// Couvre la gestion des délimiteurs <c>\left … \right</c> par
    /// <see cref="LatexToOmml"/> : émission d'un <c>&lt;m:d&gt;</c> auto-sizé
    /// (begChr/endChr) au lieu de laisser fuiter « left »/« right » en texte
    /// brut (bug observé en Word : « f(x) » rendu « fleft(xright) »).
    /// </summary>
    public class LatexToOmmlDelimiterTests
    {
        private static readonly XNamespace M = LatexToOmml.M;

        private static string AllText(XElement omath)
            => string.Concat(omath.Descendants(M + "t").Select(t => t.Value));

        private static XElement OnlyDelim(XElement omath)
            => Assert.Single(omath.Descendants(M + "d"));

        [Fact]
        public void Paren_delim_emits_m_d_not_literal_text()
        {
            var x = LatexToOmml.Convert(@"f\left(x\right)");
            var d = OnlyDelim(x);
            Assert.Equal("(", d.Element(M + "dPr")?.Element(M + "begChr")?.Attribute(M + "val")?.Value);
            Assert.Equal(")", d.Element(M + "dPr")?.Element(M + "endChr")?.Attribute(M + "val")?.Value);

            string text = AllText(x);
            Assert.DoesNotContain("left", text);
            Assert.DoesNotContain("right", text);
            Assert.Contains("f", text);
            Assert.Contains("x", text);
        }

        [Fact]
        public void Bracket_delim()
        {
            var d = OnlyDelim(LatexToOmml.Convert(@"\left[a;b\right]"));
            Assert.Equal("[", d.Element(M + "dPr")?.Element(M + "begChr")?.Attribute(M + "val")?.Value);
            Assert.Equal("]", d.Element(M + "dPr")?.Element(M + "endChr")?.Attribute(M + "val")?.Value);
        }

        [Fact]
        public void Brace_delim_via_backslash()
        {
            var d = OnlyDelim(LatexToOmml.Convert(@"\left\{x\right\}"));
            Assert.Equal("{", d.Element(M + "dPr")?.Element(M + "begChr")?.Attribute(M + "val")?.Value);
            Assert.Equal("}", d.Element(M + "dPr")?.Element(M + "endChr")?.Attribute(M + "val")?.Value);
        }

        [Fact]
        public void Abs_delim_pipe()
        {
            var d = OnlyDelim(LatexToOmml.Convert(@"\left|x\right|"));
            Assert.Equal("|", d.Element(M + "dPr")?.Element(M + "begChr")?.Attribute(M + "val")?.Value);
            Assert.Equal("|", d.Element(M + "dPr")?.Element(M + "endChr")?.Attribute(M + "val")?.Value);
        }

        [Fact]
        public void Dot_delim_is_invisible()
        {
            // \left. … \right) : délim ouvrant invisible (val="").
            var d = OnlyDelim(LatexToOmml.Convert(@"\left.x\right)"));
            Assert.Equal("", d.Element(M + "dPr")?.Element(M + "begChr")?.Attribute(M + "val")?.Value);
            Assert.Equal(")", d.Element(M + "dPr")?.Element(M + "endChr")?.Attribute(M + "val")?.Value);
        }

        [Fact]
        public void Fraction_inside_delim_is_nested()
        {
            // \left(\frac{1}{x}\right) : la fraction vit DANS le <m:d> (auto-size).
            var x = LatexToOmml.Convert(@"\left(\frac{1}{x}\right)");
            var d = OnlyDelim(x);
            Assert.Single(d.Descendants(M + "f"));
        }

        [Fact]
        public void Nested_delims_match_by_depth()
        {
            // \left(\left[x\right]\right) : 2 <m:d> imbriqués, le ] matche le
            // [ interne et le ) le ( externe.
            var x = LatexToOmml.Convert(@"\left(\left[x\right]\right)");
            Assert.Equal(2, x.Descendants(M + "d").Count());
            var outer = x.Elements(M + "d").Single();
            Assert.Equal("(", outer.Element(M + "dPr")?.Element(M + "begChr")?.Attribute(M + "val")?.Value);
            var inner = Assert.Single(outer.Descendants(M + "d"));
            Assert.Equal("[", inner.Element(M + "dPr")?.Element(M + "begChr")?.Attribute(M + "val")?.Value);
        }

        [Fact]
        public void Lim_fraction_still_correct_after_delim_change()
        {
            // Garde-fou : le fix \left ne casse pas le cas lim/fraction.
            var x = LatexToOmml.Convert(@"\lim_{x\to0}\frac{1}{x+1}");
            Assert.Single(x.Descendants(M + "func"));
            Assert.Single(x.Descendants(M + "limLow"));
            Assert.Single(x.Descendants(M + "f"));
        }
    }
}
