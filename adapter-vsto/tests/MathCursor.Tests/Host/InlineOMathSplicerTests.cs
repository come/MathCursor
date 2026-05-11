using MathCursor.Host;
using Xunit;

namespace MathCursor.Tests.Host
{
    /// <summary>
    /// Tests pour <see cref="InlineOMathSplicer"/> (cf. ADR
    /// <c>2026-05-07-Fix-insert-via-paragraph-xml-splice</c>).
    ///
    /// L'API publique : <see cref="InlineOMathSplicer.ExtractOMathElement"/>
    /// + <see cref="InlineOMathSplicer.SpliceOMathInDocXml"/>. Les tests
    /// d'intégration plus riches (avec pkg:package complet et plusieurs
    /// scénarios users) sont dans <see cref="InsertTransplantIntegrationTests"/>.
    /// </summary>
    public sealed class InlineOMathSplicerTests
    {
        // ─── ExtractOMathElement ──────────────────────────────────────

        [Fact]
        public void Extract_inline_oMath_returns_element_only()
        {
            string captured =
                "<w:p><w:pPr/><m:oMath><m:r><m:t>y</m:t></m:r></m:oMath></w:p>";

            string extracted = InlineOMathSplicer.ExtractOMathElement(captured);

            Assert.NotNull(extracted);
            Assert.Contains("<m:oMath", extracted);
            Assert.Contains("</m:oMath>", extracted);
            Assert.DoesNotContain("<w:p", extracted);
        }

        [Fact]
        public void Extract_oMathPara_returns_full_para_element()
        {
            string captured =
                "<w:p><m:oMathPara><m:oMathParaPr><m:jc m:val=\"left\"/></m:oMathParaPr>"
                + "<m:oMath><m:r><m:t>x</m:t></m:r></m:oMath>"
                + "</m:oMathPara></w:p>";

            string extracted = InlineOMathSplicer.ExtractOMathElement(captured);

            Assert.NotNull(extracted);
            // Préfère le wrapper oMathPara (display + jc) quand présent.
            Assert.Contains("<m:oMathPara", extracted);
        }

        [Fact]
        public void Extract_returns_null_when_no_oMath()
        {
            string captured = "<w:p><w:r><w:t>plain text</w:t></w:r></w:p>";
            Assert.Null(InlineOMathSplicer.ExtractOMathElement(captured));
        }

        [Fact]
        public void Extract_returns_null_for_empty_or_invalid_input()
        {
            Assert.Null(InlineOMathSplicer.ExtractOMathElement(null));
            Assert.Null(InlineOMathSplicer.ExtractOMathElement(""));
            Assert.Null(InlineOMathSplicer.ExtractOMathElement("not xml"));
        }
    }
}
