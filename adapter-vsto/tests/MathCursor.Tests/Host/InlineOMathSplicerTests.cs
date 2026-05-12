using System.Linq;
using System.Xml.Linq;
using MathCursor.Host;
using Xunit;

namespace MathCursor.Tests.Host
{
    /// <summary>
    /// Tests pour <see cref="InlineOMathSplicer"/> (cf. ADR
    /// <c>2026-05-07-Fix-insert-via-paragraph-xml-splice</c> +
    /// <c>2026-05-11-Fix-omath-splice-content-based-navigation</c>).
    ///
    /// API publique : <see cref="InlineOMathSplicer.ExtractOMathElement"/>,
    /// <see cref="InlineOMathSplicer.SpliceOMathInDocXml"/>. Les tests
    /// d'intégration plus riches (avec pkg:package complet et scénarios
    /// users 2026-05-07) sont dans
    /// <see cref="InsertTransplantIntegrationTests"/>.
    /// </summary>
    public sealed class InlineOMathSplicerTests
    {
        private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private static readonly XNamespace M = "http://schemas.openxmlformats.org/officeDocument/2006/math";

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

        // ─── Splice / Replace : navigation content-based ──────────────
        // Cf. ADR 2026-05-11. Le splicer doit identifier le <w:p> cible
        // par contenu (queue match mathSource), peu importe sa profondeur
        // dans l'arbre. Marche dans <w:body> direct, <w:tc> de tableau,
        // SDT, etc., uniformément.

        /// <summary>Wrapper minimal <w:document> avec les namespaces requis.</summary>
        private static string DocPkg(string bodyInner)
            => "<w:document"
            + " xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\""
            + " xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\">"
            + "<w:body>" + bodyInner + "</w:body>"
            + "</w:document>";

        private static string FakeOMath(string content)
            => "<m:oMath><m:r><m:t>" + content + "</m:t></m:r></m:oMath>";

        // ─── Cas 1 : régression — single <w:p> dans <w:body> direct ───

        [Fact]
        public void Splice_in_body_direct_paragraph_works()
        {
            // Régression : avant ADR 2026-05-11, ce cas marchait via
            // body.Elements("w:p") flat. Doit continuer à marcher avec
            // l'API content-based.
            string body = "<w:p><w:r><w:t xml:space=\"preserve\">Soit f</w:t></w:r></w:p>";
            string newOMath = FakeOMath("f_INSERTED");

            string result = InlineOMathSplicer.SpliceOMathInDocXml(
                DocPkg(body), "f", newOMath);

            Assert.NotNull(result);
            var parsed = XDocument.Parse(result);
            Assert.Contains(parsed.Descendants(M + "t"),
                t => t.Value == "f_INSERTED");
            // Le préfixe "Soit " est préservé dans le <w:p>.
            Assert.Contains(parsed.Descendants(W + "t"),
                t => t.Value.StartsWith("Soit"));
        }

        // ─── Cas 2 : single <w:p> DANS une cellule de tableau ─────────

        [Fact]
        public void Splice_in_table_cell_paragraph_works()
        {
            // Bug user 2026-05-11 : "si j'écris une formule dans un
            // tableau.. rien ne marche". Cause racine = body.Elements()
            // ne descendait pas dans <w:tc>. Avec content-based, le
            // splicer trouve le <w:p> par queue match peu importe la
            // profondeur.
            string body =
                "<w:tbl>"
                + "<w:tr>"
                + "<w:tc>"
                + "<w:p><w:r><w:t xml:space=\"preserve\">Soit f</w:t></w:r></w:p>"
                + "</w:tc>"
                + "</w:tr>"
                + "</w:tbl>";
            string newOMath = FakeOMath("f_IN_CELL");

            string result = InlineOMathSplicer.SpliceOMathInDocXml(
                DocPkg(body), "f", newOMath);

            Assert.NotNull(result);
            var parsed = XDocument.Parse(result);
            // Le nouvel OMath doit être DANS la cellule, pas au niveau
            // body.
            var tc = parsed.Descendants(W + "tc").FirstOrDefault();
            Assert.NotNull(tc);
            Assert.Contains(tc.Descendants(M + "t"),
                t => t.Value == "f_IN_CELL");
            // La structure tbl/tr/tc est préservée.
            Assert.Single(parsed.Descendants(W + "tbl"));
            Assert.Single(parsed.Descendants(W + "tr"));
            Assert.Single(parsed.Descendants(W + "tc"));
        }

        // ─── Cas 3 : cellule avec 2 <w:p> consécutifs, cible = le 2e ──

        [Fact]
        public void Splice_in_cell_with_multiple_paragraphs_targets_the_matching_one()
        {
            // Une cellule peut contenir plusieurs ¶s (multi-ligne dans
            // la cellule). Le splicer doit cibler uniquement celui dont
            // la queue match mathSource, pas le premier qu'il croise.
            string body =
                "<w:tbl><w:tr><w:tc>"
                + "<w:p><w:r><w:t>ligne sans math</w:t></w:r></w:p>"
                + "<w:p><w:r><w:t xml:space=\"preserve\">resoudre eq</w:t></w:r></w:p>"
                + "</w:tc></w:tr></w:tbl>";

            string result = InlineOMathSplicer.SpliceOMathInDocXml(
                DocPkg(body), "eq", FakeOMath("eq_MATCHED"));

            Assert.NotNull(result);
            var parsed = XDocument.Parse(result);
            var paras = parsed.Descendants(W + "p").ToList();
            Assert.Equal(2, paras.Count);
            // ¶[0] intact (pas de m:oMath).
            Assert.Empty(paras[0].Descendants(M + "t"));
            Assert.Contains(paras[0].Descendants(W + "t"),
                t => t.Value == "ligne sans math");
            // ¶[1] contient la nouvelle OMath.
            Assert.Contains(paras[1].Descendants(M + "t"),
                t => t.Value == "eq_MATCHED");
        }

    }
}
