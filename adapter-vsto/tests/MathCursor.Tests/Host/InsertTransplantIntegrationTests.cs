using System.Linq;
using System.Xml.Linq;
using MathCursor.Host;
using Xunit;

namespace MathCursor.Tests.Host
{
    /// <summary>
    /// Tests d'intégration du pipeline d'insertion (splice OMath dans le
    /// full doc XML), simulés avec des fixtures XML réalistes plutôt que
    /// via Word. Couvre TOUS les scénarios discutés avec l'utilisateur
    /// 2026-05-07 — pour qu'on n'ait plus à les retester manuellement
    /// en VSTO :
    ///
    /// <list type="bullet">
    /// <item>Soit f → commit f (¶ avec texte autour, pas d'OMath voisine)</item>
    /// <item>Soit f et g → commit g (¶ avec OMath voisine intacte)</item>
    /// <item>Soit x et y → commit y</item>
    /// <item>Soit 2x et y → commit y</item>
    /// <item>Soit AB [popup vec] et y → commit y (OMath voisine = vec(AB))</item>
    /// <item>Soit x_2 [popup indice] et y2 [popup revert] → commit y2</item>
    /// </list>
    ///
    /// Cf. ADR <c>2026-05-07-Fix-insert-via-paragraph-xml-splice</c>.
    /// </summary>
    public sealed class InsertTransplantIntegrationTests
    {
        // ─── Fixture builder ───────────────────────────────────────────

        /// <summary>Wrapper pkg:package minimal mimant le format que Word
        /// renvoie pour <c>doc.Content.WordOpenXML</c>. Le contenu XML de
        /// <c>&lt;w:body&gt;</c> est <paramref name="bodyInner"/>.</summary>
        private static string FullDocPkg(string bodyInner)
            => "<?xml version=\"1.0\"?>"
            + "<pkg:package xmlns:pkg=\"http://schemas.microsoft.com/office/2006/xmlPackage\">"
            + "<pkg:part pkg:name=\"/word/document.xml\""
            + " pkg:contentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\">"
            + "<pkg:xmlData>"
            + "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\""
            + " xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\">"
            + "<w:body>" + bodyInner + "</w:body>"
            + "</w:document>"
            + "</pkg:xmlData></pkg:part>"
            // Simule une autre part (styles) qui contient elle aussi des
            // <w:r><w:t>...</w:t></w:r> pour vérifier qu'on ne touche QUE
            // au <w:p> cible du body.
            + "<pkg:part pkg:name=\"/word/styles.xml\""
            + " pkg:contentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\">"
            + "<pkg:xmlData>"
            + "<w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">"
            + "<w:style><w:r><w:t>style example f</w:t></w:r></w:style>"
            + "</w:styles>"
            + "</pkg:xmlData></pkg:part>"
            + "</pkg:package>";

        /// <summary>Petit OMath synthétique pour la nouvelle math.</summary>
        private static string FakeOMath(string content)
            => "<m:oMath><m:r><m:t>" + content + "</m:t></m:r></m:oMath>";

        // ─── Soit f → commit f ────────────────────────────────────────

        [Fact]
        public void Soit_f_commit_f_keeps_Soit_prefix()
        {
            string body = "<w:p><w:pPr/><w:r><w:t xml:space=\"preserve\">Soit f</w:t></w:r></w:p>";
            string fullDocXml = FullDocPkg(body);
            string newOMath = FakeOMath("f");

            string result = InlineOMathSplicer.SpliceOMathInDocXml(fullDocXml, "f", newOMath);

            Assert.NotNull(result);
            Assert.Contains("Soit ", result);
            Assert.Contains(newOMath, result);
            // Le styles.xml part ne doit PAS avoir été touché — il contient
            // "style example f" en queue d'un <w:r>, le splicer ne doit pas
            // confondre avec le body (la part styles.xml n'a pas de <w:p>).
            Assert.Contains("<w:t>style example f</w:t>", result);
        }

        [Fact]
        public void Soit_f_commit_f_preserves_trailing_space_via_xml_space_preserve()
        {
            // Bug user 2026-05-07 : "soit(espace)f" + commit f → l'espace
            // est mangé. Cause : Word strip les espaces trailing si
            // xml:space="preserve" manque sur le <w:t>. Fix : on l'injecte
            // si on garde un texte qui finit par un espace.
            //
            // Cas où le <w:t> original n'a PAS xml:space (run sans espaces
            // d'origine, mais on lui en laisse un en queue après splice).
            string body = "<w:p><w:r><w:t>soit f</w:t></w:r></w:p>";
            string newOMath = FakeOMath("f");

            string result = InlineOMathSplicer.SpliceOMathInDocXml(
                FullDocPkg(body), "f", newOMath);

            Assert.NotNull(result);
            // L'espace après "soit" doit survivre → xml:space="preserve" injecté.
            Assert.Contains("xml:space=\"preserve\"", result);
            // Et le run "soit " avec trailing space doit être présent.
            Assert.Contains(">soit </w:t>", result);
        }

        [Fact]
        public void Soit_f_commit_f_keeps_existing_xml_space_preserve()
        {
            // Si <w:t> a déjà xml:space="preserve", on ne le duplique pas.
            string body = "<w:p><w:r><w:t xml:space=\"preserve\">soit f</w:t></w:r></w:p>";
            string newOMath = FakeOMath("f");

            string result = InlineOMathSplicer.SpliceOMathInDocXml(
                FullDocPkg(body), "f", newOMath);

            Assert.NotNull(result);
            // Pas de duplication d'attribut.
            int countPreserve = 0; int idx = 0;
            while ((idx = result.IndexOf("xml:space=\"preserve\"", idx, System.StringComparison.Ordinal)) >= 0)
            { countPreserve++; idx++; }
            // Le doc a déjà 2 occurrences (soit f + style example) — on
            // doit en avoir exactement le même nombre après splice.
            Assert.Equal(1, countPreserve); // juste celle du run gardé
            Assert.Contains(">soit </w:t>", result);
        }

        // ─── Soit f et g → commit g, f doit rester OMath ──────────────

        [Fact]
        public void Soit_f_et_g_commit_g_preserves_f_as_OMath()
        {
            string fOMath = FakeOMath("f");
            string body =
                "<w:p>"
                + "<w:r><w:t xml:space=\"preserve\">Soit </w:t></w:r>"
                + fOMath
                + "<w:r><w:t xml:space=\"preserve\"> et g</w:t></w:r>"
                + "</w:p>";
            string fullDocXml = FullDocPkg(body);
            string newOMath = FakeOMath("g");

            string result = InlineOMathSplicer.SpliceOMathInDocXml(fullDocXml, "g", newOMath);

            Assert.NotNull(result);
            Assert.Contains(fOMath, result);
            Assert.Contains(newOMath, result);
            Assert.Contains("Soit ", result);
            Assert.Contains(" et ", result);
        }

        // ─── Soit x et y → commit y ───────────────────────────────────

        [Fact]
        public void Soit_x_et_y_commit_y_preserves_x_as_OMath()
        {
            string xOMath = FakeOMath("x");
            string body =
                "<w:p>"
                + "<w:r><w:t xml:space=\"preserve\">Soit </w:t></w:r>"
                + xOMath
                + "<w:r><w:t xml:space=\"preserve\"> et y</w:t></w:r>"
                + "</w:p>";
            string newOMath = FakeOMath("y");

            string result = InlineOMathSplicer.SpliceOMathInDocXml(
                FullDocPkg(body), "y", newOMath);

            Assert.NotNull(result);
            Assert.Contains(xOMath, result);
            Assert.Contains(newOMath, result);
        }

        // ─── Soit 2x et y → commit y ──────────────────────────────────

        [Fact]
        public void Soit_2x_et_y_commit_y_preserves_2x_as_OMath()
        {
            string twoXOMath = FakeOMath("2x");
            string body =
                "<w:p>"
                + "<w:r><w:t xml:space=\"preserve\">Soit </w:t></w:r>"
                + twoXOMath
                + "<w:r><w:t xml:space=\"preserve\"> et y</w:t></w:r>"
                + "</w:p>";
            string newOMath = FakeOMath("y");

            string result = InlineOMathSplicer.SpliceOMathInDocXml(
                FullDocPkg(body), "y", newOMath);

            Assert.NotNull(result);
            Assert.Contains(twoXOMath, result);
            Assert.Contains(newOMath, result);
        }

        // ─── Soit AB [popup vec] et y → commit y ──────────────────────

        [Fact]
        public void Soit_AB_vec_et_y_commit_y_preserves_vec_AB_as_OMath()
        {
            // OMath complexe (vec(AB)) avec accent au-dessus.
            string vecABOMath =
                "<m:oMath>"
                + "<m:acc>"
                + "<m:accPr><m:chr m:val=\"⃗\"/></m:accPr>"
                + "<m:e><m:r><m:t>AB</m:t></m:r></m:e>"
                + "</m:acc>"
                + "</m:oMath>";
            string body =
                "<w:p>"
                + "<w:r><w:t xml:space=\"preserve\">Soit </w:t></w:r>"
                + vecABOMath
                + "<w:r><w:t xml:space=\"preserve\"> et y</w:t></w:r>"
                + "</w:p>";

            string result = InlineOMathSplicer.SpliceOMathInDocXml(
                FullDocPkg(body), "y", FakeOMath("y_NEW_MARKER"));

            Assert.NotNull(result);
            // Parse pour vérification sémantique (XDocument normalise le
            // XML donc on ne peut pas chercher des strings littérales).
            var parsed = XDocument.Parse(result);
            XNamespace m = "http://schemas.openxmlformats.org/officeDocument/2006/math";
            // Le vec(AB) doit toujours être là, identifiable par son <m:acc>
            // contenant <m:t>AB</m:t>.
            var accs = parsed.Descendants(m + "acc").ToList();
            Assert.Single(accs);
            Assert.Equal("AB", accs[0].Descendants(m + "t").First().Value);
            // Et la nouvelle OMath y_NEW_MARKER doit être présente.
            Assert.Contains(parsed.Descendants(m + "t"),
                t => t.Value == "y_NEW_MARKER");
        }

        // ─── Soit x_2 [popup indice] et y2 [popup revert] ──────────────

        [Fact]
        public void Soit_x2_indice_et_y2_revert_commit_preserves_x2_OMath()
        {
            // x_2 OMath complexe (subscript).
            string x2OMath =
                "<m:oMath>"
                + "<m:sSub>"
                + "<m:e><m:r><m:t>x</m:t></m:r></m:e>"
                + "<m:sub><m:r><m:t>2</m:t></m:r></m:sub>"
                + "</m:sSub>"
                + "</m:oMath>";
            string body =
                "<w:p>"
                + "<w:r><w:t xml:space=\"preserve\">Soit </w:t></w:r>"
                + x2OMath
                + "<w:r><w:t xml:space=\"preserve\"> et y2</w:t></w:r>"
                + "</w:p>";
            string newOMath = FakeOMath("y_2_via_revert"); // marker test

            string result = InlineOMathSplicer.SpliceOMathInDocXml(
                FullDocPkg(body), "y2", newOMath);

            Assert.NotNull(result);
            Assert.Contains(x2OMath, result); // x_2 OMath byte-à-byte intact
            Assert.Contains(newOMath, result);
            Assert.Contains("Soit ", result);
            Assert.Contains(" et ", result);
        }

        // ─── OMath seule dans le ¶ → force jc=left ────────────────────

        [Fact]
        public void OMath_alone_in_para_is_wrapped_with_jc_left()
        {
            // ¶ qui ne contenait que la math source (pas de texte autour).
            // Après splice, l'OMath est seule dans le <w:p> → doit être
            // wrappée en <m:oMathPara><m:jc=left> sinon Word centre.
            string body = "<w:p><w:r><w:t>f</w:t></w:r></w:p>";

            string result = InlineOMathSplicer.SpliceOMathInDocXml(
                FullDocPkg(body), "f", FakeOMath("f"));

            Assert.NotNull(result);
            var parsed = XDocument.Parse(result);
            XNamespace m = "http://schemas.openxmlformats.org/officeDocument/2006/math";
            // m:oMathPara avec m:jc m:val="left" doit être présent.
            var paraEls = parsed.Descendants(m + "oMathPara").ToList();
            Assert.Single(paraEls);
            var jc = paraEls[0].Descendants(m + "jc").FirstOrDefault();
            Assert.NotNull(jc);
            Assert.Equal("left", jc.Attribute(m + "val")?.Value);
        }

        [Fact]
        public void OMath_alone_with_existing_oMathPara_centerGroup_is_patched_to_left()
        {
            // Cas réel : Word capture une OMath standalone via BuildUp et
            // l'auto-promote en m:oMathPara avec jc="centerGroup" (default).
            // On simule en passant un newOMath déjà wrappé en oMathPara
            // avec centerGroup → mon code doit patcher le jc à "left".
            string body = "<w:p><w:r><w:t>F(x)=1/X</w:t></w:r></w:p>";
            string newOMathParaCenterGroup =
                "<m:oMathPara>"
                + "<m:oMathParaPr><m:jc m:val=\"centerGroup\"/></m:oMathParaPr>"
                + "<m:oMath><m:r><m:t>F(x)=1/X</m:t></m:r></m:oMath>"
                + "</m:oMathPara>";

            string result = InlineOMathSplicer.SpliceOMathInDocXml(
                FullDocPkg(body), "F(x)=1/X", newOMathParaCenterGroup);

            Assert.NotNull(result);
            var parsed = XDocument.Parse(result);
            XNamespace m = "http://schemas.openxmlformats.org/officeDocument/2006/math";
            // Pas de double oMathPara imbriqué.
            var paraEls = parsed.Descendants(m + "oMathPara").ToList();
            Assert.Single(paraEls);
            // jc patché à "left" (plus centerGroup).
            var jc = paraEls[0].Descendants(m + "jc").Single();
            Assert.Equal("left", jc.Attribute(m + "val")?.Value);
        }

        [Fact]
        public void OMath_alongside_text_is_NOT_wrapped_with_oMathPara()
        {
            // À l'inverse : OMath inline avec du texte autour ne doit PAS
            // être promue en display (sinon ça casse le flow inline).
            string body = "<w:p><w:r><w:t xml:space=\"preserve\">Soit f</w:t></w:r></w:p>";

            string result = InlineOMathSplicer.SpliceOMathInDocXml(
                FullDocPkg(body), "f", FakeOMath("f"));

            Assert.NotNull(result);
            var parsed = XDocument.Parse(result);
            XNamespace m = "http://schemas.openxmlformats.org/officeDocument/2006/math";
            // Aucun m:oMathPara : l'OMath reste inline parce qu'il y a "Soit ".
            Assert.Empty(parsed.Descendants(m + "oMathPara"));
        }

        // ─── Cas dégénérés ────────────────────────────────────────────

        [Fact]
        public void Returns_null_when_math_source_not_in_any_paragraph()
        {
            string body =
                "<w:p><w:r><w:t>first para</w:t></w:r></w:p>"
                + "<w:p><w:r><w:t>second para</w:t></w:r></w:p>";
            // "absent" n'existe nulle part en queue de <w:p> → null.
            string result = InlineOMathSplicer.SpliceOMathInDocXml(
                FullDocPkg(body), "absent", FakeOMath("x"));
            Assert.Null(result);
        }

        [Fact]
        public void Targets_correct_paragraph_by_content_match()
        {
            // 3 ¶s, "f" en queue du 2ème seulement → content-based doit
            // toucher uniquement le 2ème.
            string body =
                "<w:p><w:r><w:t>premier</w:t></w:r></w:p>"
                + "<w:p><w:r><w:t xml:space=\"preserve\">Soit f</w:t></w:r></w:p>"
                + "<w:p><w:r><w:t>troisieme</w:t></w:r></w:p>";
            string newOMath = FakeOMath("f");

            string result = InlineOMathSplicer.SpliceOMathInDocXml(
                FullDocPkg(body), "f", newOMath);

            Assert.NotNull(result);
            // Les ¶s 0 et 2 doivent être intacts byte-à-byte.
            Assert.Contains("<w:p><w:r><w:t>premier</w:t></w:r></w:p>", result);
            Assert.Contains("<w:p><w:r><w:t>troisieme</w:t></w:r></w:p>", result);
            // Le ¶ 1 contient "Soit " + nouvelle OMath.
            Assert.Contains("Soit ", result);
            Assert.Contains(newOMath, result);
        }

        // ─── Cas extension cases multi-ligne (route legacy display) ───

        [Fact]
        public void Cases_extension_replaces_target_paragraph_in_doc_xml()
        {
            // Reproduit le flow legacy display math (cascade merger
            // cases multi-ligne) : on a un doc avec ¶[0]=cases system
            // pre-deleted (= ¶ vide), ¶[1]="{ x+2 = 3" texte typé,
            // ¶[2]=marker. Le cascade merger calcule mergedSource="...",
            // construit la nouvelle cases OMath, et appelle
            // ReplaceParagraphsInDocXml(idx0=1, count=1, newPara=cases).
            string body =
                "<w:p/>" // ¶[0] vide après pre-delete de l'ancienne cases
                + "<w:p><w:r><w:t xml:space=\"preserve\">{ x+2 = 3</w:t></w:r></w:p>" // ¶[1]
                + "<w:p><w:r><w:t xml:space=\"preserve\">{ </w:t></w:r></w:p>"; // ¶[2] marker
            string fullDocXml = FullDocPkg(body);

            // newPara représente la cases OMath multi-ligne mergée.
            string newPara =
                "<w:p>"
                + "<m:oMathPara><m:oMathParaPr><m:jc m:val=\"left\"/></m:oMathParaPr>"
                + "<m:oMath><m:r><m:t>NEW_CASES_2LINES</m:t></m:r></m:oMath>"
                + "</m:oMathPara>"
                + "</w:p>";

            string result = InlineOMathSplicer.ReplaceParagraphsInDocXml(
                fullDocXml, new[] { "{ x+2 = 3" }, newPara);

            Assert.NotNull(result);
            var parsed = XDocument.Parse(result);
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            XNamespace m = "http://schemas.openxmlformats.org/officeDocument/2006/math";
            var bodyEl = parsed.Descendants(w + "body")
                .First(b => b.Elements(w + "p").Any());
            var paras = bodyEl.Elements(w + "p").ToList();
            // Toujours 3 ¶s.
            Assert.Equal(3, paras.Count);
            // ¶[0] reste vide (le self-closing).
            Assert.Empty(paras[0].Elements());
            // ¶[1] contient la nouvelle cases (= NEW_CASES_2LINES).
            Assert.Contains(paras[1].Descendants(m + "t"),
                t => t.Value == "NEW_CASES_2LINES");
            // L'ancien texte "{ x+2 = 3" doit avoir disparu de ¶[1].
            Assert.DoesNotContain(paras[1].Descendants(w + "t"),
                t => t.Value.Contains("{ x+2 = 3"));
            // ¶[2] (marker) intact.
            Assert.Equal("{ ",
                paras[2].Descendants(w + "t").First().Value);
        }

        [Fact]
        public void Self_closing_empty_paragraphs_are_ignored_by_content_match()
        {
            // Word peut émettre <w:p/> auto-fermant pour les ¶ vides.
            // Le navigateur content-based ignore les ¶ qui n'ont rien
            // en queue (pas de <w:r>) — il trouve uniquement celui dont
            // la queue match mathSource.
            string body =
                "<w:p><w:r><w:t>premier</w:t></w:r></w:p>"
                + "<w:p/>" // ¶ vide auto-fermé : ignoré par le splicer
                + "<w:p><w:r><w:t xml:space=\"preserve\">Soit f</w:t></w:r></w:p>"; // cible

            string result = InlineOMathSplicer.SpliceOMathInDocXml(
                FullDocPkg(body), "f", FakeOMath("f_NEW"));

            Assert.NotNull(result);
            var parsed = XDocument.Parse(result);
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            XNamespace m = "http://schemas.openxmlformats.org/officeDocument/2006/math";
            // Toujours 3 paragraphes dans le body principal.
            var bodyEl = parsed.Descendants(w + "body")
                .First(b => b.Elements(w + "p").Any());
            var paras = bodyEl.Elements(w + "p").ToList();
            Assert.Equal(3, paras.Count);
            // ¶[0] = "premier" intact.
            Assert.Equal("premier",
                paras[0].Descendants(w + "t").First().Value);
            // ¶[1] = vide (le self-closing).
            Assert.Empty(paras[1].Elements());
            // ¶[2] = "Soit " + nouvelle OMath.
            Assert.Contains("Soit ",
                paras[2].Descendants(w + "t").Select(t => t.Value));
            Assert.Contains(paras[2].Descendants(m + "t"),
                t => t.Value == "f_NEW");
        }
    }
}
