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
    /// <see cref="InlineOMathSplicer.SpliceOMathInDocXml"/>,
    /// <see cref="InlineOMathSplicer.ReplaceParagraphsInDocXml"/>. Les
    /// tests d'intégration plus riches (avec pkg:package complet et
    /// scénarios users 2026-05-07) sont dans
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

        // ─── Cas 4 : cross-merge intra-cellule (système { sur 2 ¶s) ───

        [Fact]
        public void ReplaceParagraphs_groups_siblings_in_same_cell()
        {
            // 2 ¶s siblings dans la même cellule, sources brutes des
            // deux lignes d'un système. ReplaceParagraphsInDocXml doit
            // les fusionner en 1 nouveau ¶ (= la cases OMath mergée).
            string body =
                "<w:tbl><w:tr><w:tc>"
                + "<w:p><w:r><w:t xml:space=\"preserve\">{ x = 1</w:t></w:r></w:p>"
                + "<w:p><w:r><w:t xml:space=\"preserve\">{ y = 2</w:t></w:r></w:p>"
                + "</w:tc></w:tr></w:tbl>";
            string newPara =
                "<w:p><m:oMathPara><m:oMathParaPr><m:jc m:val=\"left\"/></m:oMathParaPr>"
                + "<m:oMath><m:r><m:t>CASES_MERGED</m:t></m:r></m:oMath>"
                + "</m:oMathPara></w:p>";

            string result = InlineOMathSplicer.ReplaceParagraphsInDocXml(
                DocPkg(body),
                new[] { "{ x = 1", "{ y = 2" },
                newPara);

            Assert.NotNull(result);
            var parsed = XDocument.Parse(result);
            // La cellule contient 2 ¶s : la cases mergée + un ¶ vide
            // ajouté pour la landing zone du caret (cf. fix caret-trap
            // multi-ligne en cellule, même ADR).
            var tc = parsed.Descendants(W + "tc").Single();
            var parasInCell = tc.Elements(W + "p").ToList();
            Assert.Equal(2, parasInCell.Count);
            // 1er ¶ : la cases mergée.
            Assert.Contains(parasInCell[0].Descendants(M + "t"),
                t => t.Value == "CASES_MERGED");
            // 2e ¶ : vide (landing zone).
            Assert.Empty(parasInCell[1].Elements());
        }

        // ─── Cas display math : caret-trap fix (bug 2026-05-11) ───────

        [Fact]
        public void ReplaceParagraphs_adds_empty_paragraph_after_when_no_next_sibling()
        {
            // Cellule mono-¶ avec une chaîne `=` multi-ligne (2 sources).
            // Après remplacement par le display math mergé, il faut un
            // <w:p> vide après dans la même cellule pour que le caret
            // puisse atterrir dedans (sinon EndKey(wdLine) sort de la
            // cellule).
            string body =
                "<w:tbl><w:tr><w:tc>"
                + "<w:p><w:r><w:t xml:space=\"preserve\">x = 1</w:t></w:r></w:p>"
                + "<w:p><w:r><w:t xml:space=\"preserve\">= 2x + 1</w:t></w:r></w:p>"
                + "</w:tc></w:tr></w:tbl>";
            string newPara =
                "<w:p><m:oMathPara><m:oMathParaPr><m:jc m:val=\"left\"/></m:oMathParaPr>"
                + "<m:oMath><m:r><m:t>EQUATION_CHAIN</m:t></m:r></m:oMath>"
                + "</m:oMathPara></w:p>";

            string result = InlineOMathSplicer.ReplaceParagraphsInDocXml(
                DocPkg(body),
                new[] { "x = 1", "= 2x + 1" },
                newPara);

            Assert.NotNull(result);
            var parsed = XDocument.Parse(result);
            var tc = parsed.Descendants(W + "tc").Single();
            var parasInCell = tc.Elements(W + "p").ToList();
            // 2 ¶s : le display math + un ¶ vide ajouté pour le caret.
            Assert.Equal(2, parasInCell.Count);
            // 1er ¶ : la chaîne mergée.
            Assert.Contains(parasInCell[0].Descendants(M + "t"),
                t => t.Value == "EQUATION_CHAIN");
            // 2e ¶ : vide (pas d'enfants).
            Assert.Empty(parasInCell[1].Elements());
        }

        [Fact]
        public void ReplaceParagraphs_does_not_duplicate_when_next_sibling_already_exists()
        {
            // Hors cellule, avec un ¶ déjà présent après le multi-ligne.
            // Le splicer ne doit pas en ajouter un en plus (sinon
            // saut de ligne visuel parasite).
            string body =
                "<w:p><w:r><w:t xml:space=\"preserve\">x = 1</w:t></w:r></w:p>"
                + "<w:p><w:r><w:t xml:space=\"preserve\">= 2x + 1</w:t></w:r></w:p>"
                + "<w:p><w:r><w:t>contenu suivant</w:t></w:r></w:p>";
            string newPara =
                "<w:p><m:oMathPara><m:oMathParaPr><m:jc m:val=\"left\"/></m:oMathParaPr>"
                + "<m:oMath><m:r><m:t>EQUATION_CHAIN</m:t></m:r></m:oMath>"
                + "</m:oMathPara></w:p>";

            string result = InlineOMathSplicer.ReplaceParagraphsInDocXml(
                DocPkg(body),
                new[] { "x = 1", "= 2x + 1" },
                newPara);

            Assert.NotNull(result);
            var parsed = XDocument.Parse(result);
            var bodyEl = parsed.Descendants(W + "body")
                .First(b => b.Elements(W + "p").Any());
            var paras = bodyEl.Elements(W + "p").ToList();
            // 2 ¶s : le display math + le "contenu suivant" qui était
            // déjà là. PAS de ¶ vide ajouté (le sibling suivant
            // sert déjà de "landing zone" pour le caret).
            Assert.Equal(2, paras.Count);
            Assert.Contains(paras[0].Descendants(M + "t"),
                t => t.Value == "EQUATION_CHAIN");
            Assert.Contains(paras[1].Descendants(W + "t"),
                t => t.Value == "contenu suivant");
        }

        // ─── Cas 5 : refus cross-merge frontière cellule ↔ body ───────

        [Fact]
        public void ReplaceParagraphs_refuses_when_paragraphs_cross_container_boundary()
        {
            // Une cellule avec un ¶, puis hors table un autre ¶. On
            // demande une cross-merge des deux → refuser (sémantiquement
            // absurde, casserait la structure OOXML).
            string body =
                "<w:tbl><w:tr><w:tc>"
                + "<w:p><w:r><w:t xml:space=\"preserve\">{ ligne dans cellule</w:t></w:r></w:p>"
                + "</w:tc></w:tr></w:tbl>"
                + "<w:p><w:r><w:t xml:space=\"preserve\">{ ligne hors cellule</w:t></w:r></w:p>";
            string newPara =
                "<w:p><m:oMath><m:r><m:t>FORBIDDEN</m:t></m:r></m:oMath></w:p>";

            string result = InlineOMathSplicer.ReplaceParagraphsInDocXml(
                DocPkg(body),
                new[] { "{ ligne dans cellule", "{ ligne hors cellule" },
                newPara);

            // Refus net : le sibling avant "{ ligne hors cellule" n'est
            // pas un <w:p> (c'est un <w:tbl>), donc la remontée échoue.
            Assert.Null(result);
        }
    }
}
