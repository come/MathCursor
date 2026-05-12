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

        // ─── Splice avec absorbedHandles (refactor C.1 atomic) ────────
        // Le splicer enrichi prend en charge l'absorption inline d'OMaths
        // voisines : retire le bookmark + l'OMath + glue whitespace entre
        // OMath absorbée et texte typé, en plus de remplacer les runs
        // typés par la nouvelle OMath. Cf. ADR 2026-05-12-Refactor-pure-
        // merger-atomic-insert.

        private static string BookmarkedOMath(string handle, string content, int bookmarkId = 1)
            => $"<w:bookmarkStart w:id=\"{bookmarkId}\" w:name=\"mcEq_{handle}\"/>"
             + FakeOMath(content)
             + $"<w:bookmarkEnd w:id=\"{bookmarkId}\"/>";

        [Fact]
        public void Splice_with_left_absorb_removes_bookmark_oMath_and_glue()
        {
            // Scénario merge_left : ¶ = [absorbed OMath][espace][typé].
            // absorbedHandles = handle de l'OMath absorbée → splice retire
            // bookmarks + oMath + l'espace de glue, et remplace par la
            // nouvelle OMath fusionnée.
            string body = "<w:p>"
                + BookmarkedOMath("eq_LEFT", "OLD_OMATH")
                + "<w:r><w:t xml:space=\"preserve\"> </w:t></w:r>"
                + "<w:r><w:t xml:space=\"preserve\">typed</w:t></w:r>"
                + "</w:p>";

            string result = InlineOMathSplicer.SpliceOMathInDocXml(
                DocPkg(body), "typed", FakeOMath("MERGED"),
                new[] { "eq_LEFT" });

            Assert.NotNull(result);
            var parsed = XDocument.Parse(result);
            var para = parsed.Descendants(W + "p").Single();
            // L'OMath absorbée doit avoir disparu, remplacée par MERGED.
            Assert.DoesNotContain(para.Descendants(M + "t"),
                t => t.Value == "OLD_OMATH");
            Assert.Contains(para.Descendants(M + "t"),
                t => t.Value == "MERGED");
            // Le bookmark mcEq_eq_LEFT doit être parti.
            Assert.DoesNotContain(para.Descendants(W + "bookmarkStart"),
                bm => (string)bm.Attribute(W + "name") == "mcEq_eq_LEFT");
            // Plus de run "typed" non plus.
            Assert.DoesNotContain(para.Descendants(W + "t"),
                t => t.Value == "typed");
        }

        [Fact]
        public void Splice_with_right_absorb_removes_trailing_bookmark_and_oMath()
        {
            // Scénario merge_right : ¶ = [typé][espace][absorbed OMath].
            // Le tail-match doit skipper les éléments absorbés en queue
            // pour matcher le texte typé qui est AVANT.
            string body = "<w:p>"
                + "<w:r><w:t xml:space=\"preserve\">typed</w:t></w:r>"
                + "<w:r><w:t xml:space=\"preserve\"> </w:t></w:r>"
                + BookmarkedOMath("eq_RIGHT", "OLD_RIGHT")
                + "</w:p>";

            string result = InlineOMathSplicer.SpliceOMathInDocXml(
                DocPkg(body), "typed", FakeOMath("MERGED"),
                new[] { "eq_RIGHT" });

            Assert.NotNull(result);
            var parsed = XDocument.Parse(result);
            var para = parsed.Descendants(W + "p").Single();
            Assert.DoesNotContain(para.Descendants(M + "t"),
                t => t.Value == "OLD_RIGHT");
            Assert.Contains(para.Descendants(M + "t"),
                t => t.Value == "MERGED");
            Assert.DoesNotContain(para.Descendants(W + "bookmarkStart"),
                bm => (string)bm.Attribute(W + "name") == "mcEq_eq_RIGHT");
        }

        [Fact]
        public void Splice_with_both_left_and_right_absorb()
        {
            // Cas extrême : OMath gauche + glue + typé + glue + OMath droite,
            // les 2 absorbées. Toute la séquence remplacée par la nouvelle.
            string body = "<w:p>"
                + "<w:r><w:t xml:space=\"preserve\">prefix </w:t></w:r>"
                + BookmarkedOMath("eq_L", "LEFT", 10)
                + "<w:r><w:t xml:space=\"preserve\"> </w:t></w:r>"
                + "<w:r><w:t xml:space=\"preserve\">middle</w:t></w:r>"
                + "<w:r><w:t xml:space=\"preserve\"> </w:t></w:r>"
                + BookmarkedOMath("eq_R", "RIGHT", 11)
                + "</w:p>";

            string result = InlineOMathSplicer.SpliceOMathInDocXml(
                DocPkg(body), "middle", FakeOMath("MERGED"),
                new[] { "eq_L", "eq_R" });

            Assert.NotNull(result);
            var parsed = XDocument.Parse(result);
            var para = parsed.Descendants(W + "p").Single();
            // Aucune des 2 OMaths absorbées ne doit subsister.
            Assert.DoesNotContain(para.Descendants(M + "t"),
                t => t.Value == "LEFT" || t.Value == "RIGHT");
            Assert.Contains(para.Descendants(M + "t"),
                t => t.Value == "MERGED");
            // Le préfixe "prefix " est préservé (pas absorbé).
            Assert.Contains(para.Descendants(W + "t"),
                t => t.Value.Contains("prefix"));
        }

        [Fact]
        public void Splice_with_absorbedHandles_null_behaves_like_legacy()
        {
            // Sans absorbedHandles, le splicer doit fonctionner comme
            // l'overload original (compat backward).
            string body = "<w:p><w:r><w:t xml:space=\"preserve\">Soit f</w:t></w:r></w:p>";

            string r1 = InlineOMathSplicer.SpliceOMathInDocXml(
                DocPkg(body), "f", FakeOMath("F"));
            string r2 = InlineOMathSplicer.SpliceOMathInDocXml(
                DocPkg(body), "f", FakeOMath("F"), null);

            Assert.NotNull(r1);
            Assert.Equal(r1, r2);
        }

        [Fact]
        public void Splice_with_unrelated_bookmark_does_not_absorb_it()
        {
            // Un bookmark mcEq_X présent dans le ¶ mais X PAS dans
            // absorbedHandles ne doit pas être touché par le splicer.
            // Important : seuls les handles explicitement marqués comme
            // absorbés sont retirés.
            string body = "<w:p>"
                + BookmarkedOMath("eq_PRESERVED", "KEEP")
                + "<w:r><w:t xml:space=\"preserve\"> et </w:t></w:r>"
                + "<w:r><w:t xml:space=\"preserve\">typed</w:t></w:r>"
                + "</w:p>";

            string result = InlineOMathSplicer.SpliceOMathInDocXml(
                DocPkg(body), "typed", FakeOMath("NEW"),
                new[] { "eq_OTHER_NOT_IN_PARA" });

            Assert.NotNull(result);
            var parsed = XDocument.Parse(result);
            var para = parsed.Descendants(W + "p").Single();
            // L'OMath "KEEP" reste, et son bookmark aussi.
            Assert.Contains(para.Descendants(M + "t"),
                t => t.Value == "KEEP");
            Assert.Contains(para.Descendants(W + "bookmarkStart"),
                bm => (string)bm.Attribute(W + "name") == "mcEq_eq_PRESERVED");
            // La nouvelle OMath est insérée.
            Assert.Contains(para.Descendants(M + "t"),
                t => t.Value == "NEW");
        }

        [Fact]
        public void Splice_with_empty_absorbedHandles_list_behaves_like_legacy()
        {
            // Liste vide = pas d'absorption demandée. Doit se comporter
            // exactement comme la version sans absorbedHandles.
            string body = "<w:p><w:r><w:t xml:space=\"preserve\">Soit f</w:t></w:r></w:p>";

            string r1 = InlineOMathSplicer.SpliceOMathInDocXml(
                DocPkg(body), "f", FakeOMath("F"));
            string r2 = InlineOMathSplicer.SpliceOMathInDocXml(
                DocPkg(body), "f", FakeOMath("F"),
                System.Array.Empty<string>());

            Assert.Equal(r1, r2);
        }
    }
}
