using System;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.Debug
{
    /// <summary>
    /// Recherche d'une recette d'insertion display + CC qui ne déclenche
    /// PAS l'ajout de <c>&lt;w:br/&gt;</c> par Word.
    ///
    /// <para>Référence validée 2026-05-19 : <see cref="RunVariantG_NoCc"/>
    /// produit un display propre (OMath dans m:oMathPara, sans &lt;w:br/&gt;,
    /// sans ligne vide visible) MAIS sans backlink CC pour edit/revert.</para>
    ///
    /// <para>Objectif des variantes G1, G2, G3 : repartir de la même recette
    /// G mais essayer d'ajouter un CC d'une façon qui ne casse pas le
    /// rendu / n'ajoute pas de &lt;w:br/&gt;.</para>
    /// </summary>
    internal static class OMathInsertVariants
    {
        private const string DefaultSource = "g(x)=1/x";

        // ─── Variante E : BuildUp first SANS Type set + CC RichText block-level ────────

        public static string RunVariantE_BuildUpFirst_NoTypeSet_CcOnFullPara(Word.Application app)
        {
            var sb = StartTrace("E: BuildUp first → AUCUN Type set (trust Word) → CC RichText sur paragraph COMPLET (block-level)");
            try
            {
                var (doc, sel, _) = Setup(app, sb);
                if (doc == null) return sb.ToString();

                sel.TypeText(DefaultSource);
                int afterEnd = sel.Start;
                int srcStart = afterEnd - DefaultSource.Length;

                var typedRange = doc.Range(srcStart, afterEnd);
                var added = typedRange.OMaths.Add(typedRange);
                added.OMaths.BuildUp();
                var om = FirstOMath(added);
                if (om == null) { sb.AppendLine("OMath null"); return sb.ToString(); }
                sb.AppendLine($"[2] OMaths.Add+BuildUp: om.Range=[{om.Range.Start},{om.Range.End}) om.Type={om.Type} (trust Word, on touche pas)");

                om.Justification = Word.WdOMathJc.wdOMathJcLeft;
                sb.AppendLine($"[3] Justification=Left only");

                var paraRange = om.Range.Paragraphs[1].Range;
                sb.AppendLine($"[4] target paragraph COMPLET = [{paraRange.Start},{paraRange.End})");
                Word.ContentControl cc = null;
                try
                {
                    cc = paraRange.ContentControls.Add(Word.WdContentControlType.wdContentControlRichText);
                    cc.Title = MathCursor.Host.CCMeta.MCMetaJson.CcTitle;
                    cc.Appearance = Word.WdContentControlAppearance.wdContentControlHidden;
                    sb.AppendLine($"[5] CC wrap (block-level): cc.Range=[{cc.Range.Start},{cc.Range.End})");
                }
                catch (Exception exCc) { sb.AppendLine("[5] CC wrap ERR: " + exCc.Message); }

                return Finalize(sb, om, cc);
            }
            catch (Exception ex) { sb.AppendLine("ERR " + ex.Message); return sb.ToString(); }
        }

        // ─── Variante G4 : G3 + caret explicite après l'OMath (continuation in-line) ───

        public static string RunVariantG4_LikeG3PlusCaretAfterOm(Word.Application app)
        {
            var sb = StartTrace("G4: G3 + caret SetRange explicite à om.Range.End post-CC, pour pouvoir continuer à taper directement après l'équation");
            try
            {
                var (doc, sel, _) = Setup(app, sb);
                if (doc == null) return sb.ToString();

                sel.TypeText(DefaultSource);
                int afterEnd = sel.Start;
                int srcStart = afterEnd - DefaultSource.Length;

                var typedRange = doc.Range(srcStart, afterEnd);
                var added = typedRange.OMaths.Add(typedRange);
                added.OMaths.BuildUp();
                var om = FirstOMath(added);
                if (om == null) { sb.AppendLine("OMath null"); return sb.ToString(); }
                sb.AppendLine($"[2] OMath built: om.Range=[{om.Range.Start},{om.Range.End}) om.Type={om.Type}");

                om.Type = Word.WdOMathType.wdOMathDisplay;
                om.Justification = Word.WdOMathJc.wdOMathJcLeft;
                sb.AppendLine($"[3] Type=Display + Justification set");

                Word.ContentControl cc = null;
                try
                {
                    cc = om.Range.ContentControls.Add(Word.WdContentControlType.wdContentControlRichText);
                    cc.Title = MathCursor.Host.CCMeta.MCMetaJson.CcTitle;
                    cc.Appearance = Word.WdContentControlAppearance.wdContentControlHidden;
                    sb.AppendLine($"[4] CC RichText sur om.Range : cc.Range=[{cc.Range.Start},{cc.Range.End})");
                }
                catch (Exception exCc) { sb.AppendLine("[4] CC wrap ERR: " + exCc.Message); }

                // Caret juste après l'OMath, dans le même ¶ — pas de saut de ¶.
                // Plusieurs cibles à essayer en cascade :
                //   1. cc.Range.End (= juste après le CC, devrait être hors)
                //   2. om.Range.End (fallback)
                try
                {
                    int target;
                    if (cc != null) target = cc.Range.End;
                    else target = om.Range.End;
                    sel.SetRange(target, target);
                    sb.AppendLine($"[5] caret SetRange @ {target}, sel=[{sel.Start},{sel.End})");
                }
                catch (Exception exC) { sb.AppendLine("[5] caret set ERR: " + exC.Message); }

                return Finalize(sb, om, cc);
            }
            catch (Exception ex) { sb.AppendLine("ERR " + ex.Message); return sb.ToString(); }
        }

        // ─── Variante G : recette validée sans CC ───────────────────

        public static string RunVariantG_NoCc(Word.Application app)
        {
            var sb = StartTrace("G: BuildUp first → Type=Display → AUCUN CC (= référence display propre)");
            try
            {
                var (doc, sel, _) = Setup(app, sb);
                if (doc == null) return sb.ToString();

                sel.TypeText(DefaultSource);
                int afterEnd = sel.Start;
                int srcStart = afterEnd - DefaultSource.Length;
                sb.AppendLine($"[1] TypeText: typedRange=[{srcStart},{afterEnd})");

                var typedRange = doc.Range(srcStart, afterEnd);
                var added = typedRange.OMaths.Add(typedRange);
                added.OMaths.BuildUp();
                var om = FirstOMath(added);
                if (om == null) { sb.AppendLine("OMath null"); return sb.ToString(); }
                sb.AppendLine($"[2] OMaths.Add+BuildUp: om.Range=[{om.Range.Start},{om.Range.End}) om.Type={om.Type}");

                om.Type = Word.WdOMathType.wdOMathDisplay;
                om.Justification = Word.WdOMathJc.wdOMathJcLeft;
                sb.AppendLine($"[3] Type=Display set: om.Type={om.Type}");
                sb.AppendLine($"[4] (pas de CC)");

                return Finalize(sb, om, null);
            }
            catch (Exception ex) { sb.AppendLine("ERR " + ex.Message); return sb.ToString(); }
        }

        // ─── Variante G1 : G + anchor CC ZWSP avant l'OMath ────────

        public static string RunVariantG1_AnchorCcBefore(Word.Application app)
        {
            var sb = StartTrace("G1: G + anchor CC sur ZWSP hidden AVANT l'OMath (pas autour)");
            try
            {
                var (doc, sel, _) = Setup(app, sb);
                if (doc == null) return sb.ToString();

                sel.TypeText(DefaultSource);
                int afterEnd = sel.Start;
                int srcStart = afterEnd - DefaultSource.Length;

                var typedRange = doc.Range(srcStart, afterEnd);
                var added = typedRange.OMaths.Add(typedRange);
                added.OMaths.BuildUp();
                var om = FirstOMath(added);
                if (om == null) { sb.AppendLine("OMath null"); return sb.ToString(); }
                sb.AppendLine($"[2] OMaths.Add+BuildUp: om.Range=[{om.Range.Start},{om.Range.End}) om.Type={om.Type}");

                om.Type = Word.WdOMathType.wdOMathDisplay;
                om.Justification = Word.WdOMathJc.wdOMathJcLeft;
                sb.AppendLine($"[3] Type=Display set: om.Type={om.Type}");

                // Insère un ZWSP juste avant om.Range.Start (= dans le ¶, hors OMath)
                int anchorPos = om.Range.Start;
                sb.AppendLine($"[4] anchor pos = {anchorPos} (= om.Range.Start)");
                Word.ContentControl cc = null;
                try
                {
                    var anchorRange = doc.Range(anchorPos, anchorPos);
                    anchorRange.InsertBefore("​"); // ZWSP
                    // Le ZWSP est inséré au position [anchorPos, anchorPos+1)
                    var ccRange = doc.Range(anchorPos, anchorPos + 1);
                    // Marque le ZWSP comme hidden
                    try { ccRange.Font.Hidden = -1; } catch { } // -1 = true en VSTO
                    cc = ccRange.ContentControls.Add(Word.WdContentControlType.wdContentControlRichText);
                    cc.Title = MathCursor.Host.CCMeta.MCMetaJson.CcTitle;
                    cc.Appearance = Word.WdContentControlAppearance.wdContentControlHidden;
                    sb.AppendLine($"[5] anchor CC créé sur ZWSP : cc.Range=[{cc.Range.Start},{cc.Range.End})");
                }
                catch (Exception exA) { sb.AppendLine("[5] anchor err: " + exA.Message); }

                // Re-probe OMath car positions ont bougé
                Word.OMath omAfter = null;
                try
                {
                    foreach (Word.OMath o in doc.Range(0, doc.Content.End).OMaths) { omAfter = o; break; }
                }
                catch { }

                return Finalize(sb, omAfter ?? om, cc);
            }
            catch (Exception ex) { sb.AppendLine("ERR " + ex.Message); return sb.ToString(); }
        }

        // ─── Variante G2 : G + CC inséré via OOXML direct (XDocument) ────

        public static string RunVariantG2_CcViaOoxml(Word.Application app)
        {
            var sb = StartTrace("G2: G + wrap CC via OOXML direct (XDocument). Bypass l'API ContentControls.Add qui peut déclencher <w:br/>.");
            try
            {
                var (doc, sel, _) = Setup(app, sb);
                if (doc == null) return sb.ToString();

                sel.TypeText(DefaultSource);
                int afterEnd = sel.Start;
                int srcStart = afterEnd - DefaultSource.Length;

                var typedRange = doc.Range(srcStart, afterEnd);
                var added = typedRange.OMaths.Add(typedRange);
                added.OMaths.BuildUp();
                var om = FirstOMath(added);
                if (om == null) { sb.AppendLine("OMath null"); return sb.ToString(); }
                sb.AppendLine($"[2] OMaths.Add+BuildUp: om.Range=[{om.Range.Start},{om.Range.End}) om.Type={om.Type}");

                om.Type = Word.WdOMathType.wdOMathDisplay;
                om.Justification = Word.WdOMathJc.wdOMathJcLeft;
                sb.AppendLine($"[3] Type=Display set: om.Type={om.Type}");

                // Lit l'OOXML du paragraph entier
                var paraRange = om.Range.Paragraphs[1].Range;
                string xml = paraRange.WordOpenXML;
                sb.AppendLine($"[4] paragraph.OOXML lu : {xml.Length} chars");

                // Wrap le <w:p> contenant l'OMath dans un <w:sdt>
                var xdoc = XDocument.Parse(xml);
                var w = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
                var pEl = xdoc.Descendants(w + "p").FirstOrDefault();
                if (pEl == null) { sb.AppendLine("[5] <w:p> introuvable"); return sb.ToString(); }

                var sdtPr = new XElement(w + "sdtPr",
                    new XElement(w + "alias", new XAttribute(w + "val", MathCursor.Host.CCMeta.MCMetaJson.CcTitle)),
                    new XElement(w + "id", new XAttribute(w + "val", "12345")));
                var sdtContent = new XElement(w + "sdtContent", new XElement(pEl));
                var sdt = new XElement(w + "sdt", sdtPr, sdtContent);
                pEl.ReplaceWith(sdt);
                sb.AppendLine("[5] wrapped <w:p> in <w:sdt> via OOXML");

                string newXml = xdoc.ToString(SaveOptions.DisableFormatting);
                try
                {
                    paraRange.InsertXML(newXml);
                    sb.AppendLine("[6] InsertXML OK");
                }
                catch (Exception exI) { sb.AppendLine("[6] InsertXML ERR: " + exI.Message); }

                // Re-probe
                Word.OMath omAfter = null;
                Word.ContentControl ccAfter = null;
                try
                {
                    foreach (Word.OMath o in doc.Range(0, doc.Content.End).OMaths) { omAfter = o; break; }
                    if (omAfter != null)
                    {
                        try { ccAfter = omAfter.Range.ParentContentControl; } catch { }
                    }
                }
                catch { }

                return Finalize(sb, omAfter, ccAfter);
            }
            catch (Exception ex) { sb.AppendLine("ERR " + ex.Message); return sb.ToString(); }
        }

        // ─── POC OMML : structure native injectée LOCALEMENT (pas de BuildUp) ───
        //
        // Objectif : valider que Word rend la STRUCTURE exacte d'une
        // lim-de-fraction sans re-parser une ligne UnicodeMath.
        // Contrainte forte : 100% LOCAL — on ne lit JAMAIS le WordOpenXML du
        // doc/paragraphe (sérialisation coûteuse sur gros docs). On fabrique un
        // fragment flat-OPC AUTONOME et on l'InsertXML au caret (O(1)).
        public static string RunPocOmmlLimFraction(Word.Application app)
        {
            var sb = StartTrace("POC OMML: lim_(x→0) 1/(x+1) en OMML structuré, InsertXML LOCAL au caret (pas de UnicodeMath, pas de BuildUp, pas de lecture WordOpenXML).");
            try
            {
                var doc = app?.ActiveDocument;
                var sel = app?.Selection;
                if (doc == null || sel == null) { sb.AppendLine("doc/sel null"); return sb.ToString(); }
                int pos = sel.Start;
                sb.AppendLine($"[0] caret={pos} docEnd={doc.Content.End}");

                // OMML : lim (au-dessus de x→0) appliqué à la fraction 1/(x+1).
                // \frac → m:f{num,den} ; \lim_{..} → m:func{m:limLow{e,lim}, e}.
                XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                XNamespace m = "http://schemas.openxmlformats.org/officeDocument/2006/math";
                Func<string, XElement> R = t => new XElement(m + "r", new XElement(m + "t", t));
                var oMath = new XElement(m + "oMath",
                    new XElement(m + "func",
                        new XElement(m + "fName", new XElement(m + "limLow",
                            new XElement(m + "e", R("lim")),
                            new XElement(m + "lim", R("x→0")))),
                        new XElement(m + "e", new XElement(m + "f",
                            new XElement(m + "num", R("1")),
                            new XElement(m + "den", R("x+1"))))));

                // Wrapper VALIDE : on lit le WordOpenXML de la LIGNE VIDE
                // SEULEMENT (range minuscule → O(1), LOCAL, pas le doc entier),
                // et on y injecte l'oMath. Donne un paquet flat-OPC complet
                // (content-types, rels…) que InsertXML accepte — un paquet
                // fait-main trop minimal est refusé.
                var target = sel.Paragraphs[1].Range;
                sb.AppendLine($"[1] target ¶ = [{target.Start},{target.End}) — WordOpenXML LOCAL (1 ¶)");
                var xdoc = XDocument.Parse(target.WordOpenXML);
                var pEl = xdoc.Descendants(w + "p").FirstOrDefault();
                if (pEl == null) { sb.AppendLine("[1] <w:p> introuvable"); DumpToLog(sb.ToString()); return sb.ToString(); }
                pEl.Elements().Where(e => e.Name != w + "pPr").Remove(); // vide runs, garde pPr
                pEl.Add(oMath);                                          // injecte l'oMath inline
                string newXml = xdoc.ToString(SaveOptions.DisableFormatting);
                sb.AppendLine($"[1b] paquet injecté ({newXml.Length} chars)");
                try
                {
                    target.InsertXML(newXml);
                    sb.AppendLine("[2] InsertXML LOCAL OK");
                }
                catch (Exception exI)
                {
                    sb.AppendLine("[2] InsertXML ERR: " + exI.Message);
                    DumpToLog(sb.ToString());
                    return sb.ToString();
                }

                // Re-probe local autour du caret (pas de scan global).
                Word.OMath omAfter = null;
                try { foreach (Word.OMath o in doc.Range(pos, Math.Min(pos + 40, doc.Content.End)).OMaths) { omAfter = o; break; } }
                catch { }

                // DUMP : le WordOpenXML RÉELLEMENT produit par Word (range local
                // de l'OMath, pas le doc entier) → voir si la structure func/f
                // est gardée ou re-mangée.
                if (omAfter != null)
                {
                    try
                    {
                        string producedXml = omAfter.Range.WordOpenXML ?? "";
                        sb.AppendLine("[3] OMath WordOpenXML produit (" + producedXml.Length + " chars) :");
                        sb.AppendLine(ExtractMathXml(producedXml));
                    }
                    catch (Exception exX) { sb.AppendLine("[3] dump XML ERR: " + exX.Message); }
                }
                else sb.AppendLine("[3] aucune OMath trouvée après InsertXML (structure non insérée ?)");

                var outTrace = Finalize(sb, omAfter, null);
                DumpToLog(outTrace);
                return outTrace;
            }
            catch (Exception ex) { sb.AppendLine("ERR " + ex.Message); return sb.ToString(); }
        }

        // ─── POC INLINE chirurgical : « Soit ▮ et g » (prose avant ET après) ─
        // But : insérer l'OMML SANS remplacer le ¶, sans casser les positions
        // ni la prose. Deux approches comparées (A: InsertXML range 1-char ;
        // B: hybride BuildUp placeholder → InsertXML sur om.Range).

        private static System.Xml.Linq.XElement LimFracOMath()
        {
            XNamespace m = "http://schemas.openxmlformats.org/officeDocument/2006/math";
            Func<string, XElement> R = t => new XElement(m + "r", new XElement(m + "t", t));
            return new XElement(m + "oMath",
                new XElement(m + "func",
                    new XElement(m + "fName", new XElement(m + "limLow",
                        new XElement(m + "e", R("lim")),
                        new XElement(m + "lim", R("x→0")))),
                    new XElement(m + "e", new XElement(m + "f",
                        new XElement(m + "num", R("1")),
                        new XElement(m + "den", R("x+1"))))));
        }

        // Setup commun : « Soit ▮ et g » avec caret entre "Soit " et " et g".
        private static (Word.Document doc, Word.Selection sel, int caret) SetupInline(Word.Application app, StringBuilder sb)
        {
            var doc = app?.ActiveDocument; var sel = app?.Selection;
            if (doc == null || sel == null) { sb.AppendLine("doc/sel null"); return (null, null, 0); }
            sel.SetRange(doc.Content.End, doc.Content.End);
            sel.TypeText("\rSoit ");
            int caret = sel.Start;
            sel.TypeText(" et g");
            sel.SetRange(caret, caret);
            sb.AppendLine($"[setup] « Soit ▮ et g », caret={caret}, docEnd={doc.Content.End}");
            return (doc, sel, caret);
        }

        private static void DiagInline(StringBuilder sb, Word.Document doc, int paraProbePos)
        {
            try
            {
                var para = doc.Range(paraProbePos, paraProbePos).Paragraphs[1].Range;
                int n = 0; Word.OMath om = null;
                foreach (Word.OMath o in para.OMaths) { n++; if (om == null) om = o; }
                sb.AppendLine($"[diag] ¶ text=\"{(para.Text ?? "").Replace("\r","⏎")}\"  OMaths={n}");
                if (om != null)
                {
                    sb.AppendLine($"[diag] OMath [{om.Range.Start},{om.Range.End}) type={om.Type}");
                    var (cc, _) = MathCursor.Host.CCMeta.CcMetaResolver.ResolveAt(om);
                    sb.AppendLine($"[diag] backlink CcMetaResolver → {(cc == null ? "NULL" : "OK")}");
                    sb.AppendLine("[diag] oMath XML: " + ExtractMathXml(om.Range.WordOpenXML ?? ""));
                }
            }
            catch (Exception ex) { sb.AppendLine("[diag] ERR: " + ex.Message); }
        }

        // Approche A : ZWSP + placeholder 1-char, puis InsertXML sur la RANGE
        // du placeholder (pas le ¶). Surgical ? ou split ?
        public static string RunPocInlineA(Word.Application app)
        {
            var sb = StartTrace("POC INLINE A : InsertXML sur range placeholder 1-char (pas le ¶).");
            try
            {
                var (doc, sel, caret) = SetupInline(app, sb);
                if (doc == null) { DumpToLog(sb.ToString()); return sb.ToString(); }
                sel.TypeText("​"); int zwspStart = caret, zwspEnd = sel.Start;
                try { doc.Range(zwspStart, zwspEnd).Font.Hidden = -1; } catch { }
                int phStart = sel.Start; sel.TypeText("□"); int phEnd = sel.Start;
                var phRange = doc.Range(phStart, phEnd);
                XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                var xdoc = XDocument.Parse(phRange.WordOpenXML);
                sb.AppendLine($"[A] phRange.WordOpenXML = {phRange.WordOpenXML.Length} chars (paragraphe entier ? {(xdoc.Descendants(w + "p").Count() == 1 ? "1 <w:p>" : "?")})");
                XElement phRun = null;
                foreach (var r in xdoc.Descendants(w + "r"))
                { var t = r.Element(w + "t"); if (t != null && t.Value == "□") { phRun = r; break; } }
                if (phRun != null) phRun.ReplaceWith(LimFracOMath());
                else sb.AppendLine("[A] run □ introuvable");
                try { phRange.InsertXML(xdoc.ToString(SaveOptions.DisableFormatting)); sb.AppendLine("[A] InsertXML OK"); }
                catch (Exception ex) { sb.AppendLine("[A] InsertXML ERR: " + ex.Message); }
                // anchor CC sur le ZWSP (miroir prod step 7) → teste le backlink inline.
                try
                {
                    var anchor = doc.Range(zwspStart, zwspEnd);
                    var cc = anchor.ContentControls.Add(Word.WdContentControlType.wdContentControlRichText);
                    cc.Title = MathCursor.Host.CCMeta.MCMetaJson.CcTitle;
                    try { cc.Appearance = Word.WdContentControlAppearance.wdContentControlHidden; } catch { }
                    sb.AppendLine($"[A] anchor CC [{cc.Range.Start},{cc.Range.End})");
                }
                catch (Exception exCc) { sb.AppendLine("[A] CC ERR: " + exCc.Message); }
                DiagInline(sb, doc, zwspStart);
                DumpToLog(sb.ToString()); return sb.ToString();
            }
            catch (Exception ex) { sb.AppendLine("ERR " + ex.Message); DumpToLog(sb.ToString()); return sb.ToString(); }
        }

        // Approche B : hybride. TypeText placeholder → OMaths.Add+BuildUp
        // (positionnement chirurgical PROUVÉ) → puis remplace le contenu de
        // l'OMath par notre structure via InsertXML sur om.Range.
        public static string RunPocInlineB(Word.Application app)
        {
            var sb = StartTrace("POC INLINE B : hybride BuildUp placeholder → InsertXML sur om.Range.");
            try
            {
                var (doc, sel, caret) = SetupInline(app, sb);
                if (doc == null) { DumpToLog(sb.ToString()); return sb.ToString(); }
                sel.TypeText("​"); int zwspStart = caret, zwspEnd = sel.Start;
                try { doc.Range(zwspStart, zwspEnd).Font.Hidden = -1; } catch { }
                int phStart = sel.Start; sel.TypeText("x"); int phEnd = sel.Start;
                Word.OMath om = null;
                var typed = doc.Range(phStart, phEnd);
                var added = typed.OMaths.Add(typed); added.OMaths.BuildUp();
                foreach (Word.OMath o in added.OMaths) { om = o; break; }
                if (om == null) { sb.AppendLine("[B] OMath null après BuildUp"); DumpToLog(sb.ToString()); return sb.ToString(); }
                sb.AppendLine($"[B] OMath placeholder créé [{om.Range.Start},{om.Range.End}) — prose préservée ? (voir diag)");
                // Remplace le contenu de l'OMath : lit son WordOpenXML, swap le
                // <m:oMath>, InsertXML sur om.Range (la range de l'OMath, pas le ¶).
                XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                XNamespace m = "http://schemas.openxmlformats.org/officeDocument/2006/math";
                var omRange = om.Range;
                var xdoc = XDocument.Parse(omRange.WordOpenXML);
                var omEl = xdoc.Descendants(m + "oMath").FirstOrDefault();
                if (omEl != null) omEl.ReplaceWith(LimFracOMath());
                else sb.AppendLine("[B] <m:oMath> introuvable dans om.Range.WordOpenXML");
                try { omRange.InsertXML(xdoc.ToString(SaveOptions.DisableFormatting)); sb.AppendLine("[B] InsertXML(om.Range) OK"); }
                catch (Exception ex) { sb.AppendLine("[B] InsertXML ERR: " + ex.Message); }
                DiagInline(sb, doc, zwspStart);
                DumpToLog(sb.ToString()); return sb.ToString();
            }
            catch (Exception ex) { sb.AppendLine("ERR " + ex.Message); DumpToLog(sb.ToString()); return sb.ToString(); }
        }

        // ═══ POC ÉCHAPPEMENT CARET (zone math sticky) ════════════════════
        // Après un insert OMML, le caret posé en om.Range.End reste "dans" la
        // math → la frappe suivante sort en italique math (bug retour-saisie +
        // □-leak adjacent). Chaque bouton tente UNE technique d'échappement,
        // tape "ABC", puis JUGE : ABC en <w:t> (texte plat = ✓) ou <m:t>
        // (math = ✗). But : trouver la technique à câbler dans InsertOMathAt.

        public static string RunEscape_Baseline(Word.Application app)
            => RunCaretEscape(app, "0 BASELINE (aucun échappement — reproduit le bug)",
                (doc, sel, om) => sel.SetRange(om.Range.End, om.Range.End));

        public static string RunEscape_MoveRight(Word.Application app)
            => RunCaretEscape(app, "1 MoveRight 1 char",
                (doc, sel, om) => { sel.SetRange(om.Range.End, om.Range.End); sel.MoveRight(Word.WdUnits.wdCharacter, 1, Word.WdMovementType.wdMove); });

        public static string RunEscape_MoveRightLeft(Word.Application app)
            => RunCaretEscape(app, "2 MoveRight puis MoveLeft (sortie/retour flèche)",
                (doc, sel, om) => { sel.SetRange(om.Range.End, om.Range.End); sel.MoveRight(Word.WdUnits.wdCharacter, 1, Word.WdMovementType.wdMove); sel.MoveLeft(Word.WdUnits.wdCharacter, 1, Word.WdMovementType.wdMove); });

        public static string RunEscape_EndKey(Word.Application app)
            => RunCaretEscape(app, "3 EndKey wdLine",
                (doc, sel, om) => { sel.SetRange(om.Range.End, om.Range.End); sel.EndKey(Word.WdUnits.wdLine, Word.WdMovementType.wdMove); });

        public static string RunEscape_ItalicOff(Word.Application app)
            => RunCaretEscape(app, "4 Font.Italic=0 + Name Calibri au caret",
                (doc, sel, om) => { sel.SetRange(om.Range.End, om.Range.End); try { sel.Font.Italic = 0; sel.Font.Name = "Calibri"; } catch { } });

        public static string RunEscape_TrailingSpace(Word.Application app)
            => RunCaretEscape(app, "5 Type espace après om puis recale le caret après",
                (doc, sel, om) => { sel.SetRange(om.Range.End, om.Range.End); sel.TypeText(" "); int after = sel.Start; sel.SetRange(after, after); });

        public static string RunEscape_RangeAfterPlusOne(Word.Application app)
            => RunCaretEscape(app, "6 doc.Range(om.End+1) collapsed",
                (doc, sel, om) => { int p = System.Math.Min(doc.Content.End, om.Range.End + 1); sel.SetRange(p, p); });

        // ── Échappement EN TABLEAU et EN LISTE (MoveRight vs EndKey) ──────
        // En cellule, MoveRight en fin de cellule peut sauter à la cellule
        // suivante ; en liste la ligne diffère. On vérifie la technique
        // gagnante (MoveRight) dans ces décors + EndKey en comparaison.

        public static string RunEscapeTable_MoveRight(Word.Application app)
            => RunCaretEscape(app, "TABLEAU — MoveRight 1 char",
                (doc, sel, om) => { sel.SetRange(om.Range.End, om.Range.End); sel.MoveRight(Word.WdUnits.wdCharacter, 1, Word.WdMovementType.wdMove); },
                DecorTableCell);

        public static string RunEscapeTable_EndKey(Word.Application app)
            => RunCaretEscape(app, "TABLEAU — EndKey wdLine",
                (doc, sel, om) => { sel.SetRange(om.Range.End, om.Range.End); sel.EndKey(Word.WdUnits.wdLine, Word.WdMovementType.wdMove); },
                DecorTableCell);

        public static string RunEscapeList_MoveRight(Word.Application app)
            => RunCaretEscape(app, "LISTE — MoveRight 1 char",
                (doc, sel, om) => { sel.SetRange(om.Range.End, om.Range.End); sel.MoveRight(Word.WdUnits.wdCharacter, 1, Word.WdMovementType.wdMove); },
                DecorNumberedList);

        public static string RunEscapeList_EndKey(Word.Application app)
            => RunCaretEscape(app, "LISTE — EndKey wdLine",
                (doc, sel, om) => { sel.SetRange(om.Range.End, om.Range.End); sel.EndKey(Word.WdUnits.wdLine, Word.WdMovementType.wdMove); },
                DecorNumberedList);

        // Décor par défaut : « Soit ▮ » sur une ligne neuve (rien après).
        private static int DecorPlain(Word.Document doc, Word.Selection sel, StringBuilder sb)
        {
            sel.SetRange(doc.Content.End - 1, doc.Content.End - 1);
            sel.TypeText("\rSoit ");
            sb.AppendLine("[decor] ligne neuve « Soit ▮ »");
            return sel.Start;
        }

        // Décor tableau : tableau 2×2 en fin de doc, caret cellule(1,1) « Soit ▮ ».
        private static int DecorTableCell(Word.Document doc, Word.Selection sel, StringBuilder sb)
        {
            sel.SetRange(doc.Content.End - 1, doc.Content.End - 1);
            sel.TypeText("\r");
            var tbl = doc.Tables.Add(doc.Range(doc.Content.End - 1, doc.Content.End - 1), 2, 2);
            var cell = tbl.Cell(1, 1);
            sel.SetRange(cell.Range.Start, cell.Range.Start);
            sel.TypeText("Soit ");
            sb.AppendLine("[decor] tableau 2×2, cellule(1,1) « Soit ▮ »");
            return sel.Start;
        }

        // Décor liste : ligne neuve + liste numérotée, caret « Soit ▮ ».
        private static int DecorNumberedList(Word.Document doc, Word.Selection sel, StringBuilder sb)
        {
            sel.SetRange(doc.Content.End - 1, doc.Content.End - 1);
            sel.TypeText("\r");
            try
            {
                var gallery = doc.Application.ListGalleries[Word.WdListGalleryType.wdNumberGallery];
                sel.Range.ListFormat.ApplyListTemplate(gallery.ListTemplates[1], false);
            }
            catch (Exception ex) { sb.AppendLine("[decor] liste ERR: " + ex.Message); }
            sel.TypeText("Soit ");
            sb.AppendLine("[decor] liste numérotée « Soit ▮ »");
            return sel.Start;
        }

        // Insert inline OMath f(x) via OMML (= approche A prod) dans le décor,
        // applique l'escape, tape "ABC", juge math vs plain.
        private static string RunCaretEscape(Word.Application app, string name,
            Action<Word.Document, Word.Selection, Word.OMath> escape,
            Func<Word.Document, Word.Selection, StringBuilder, int> decor = null)
        {
            var sb = StartTrace("ÉCHAPPEMENT CARET — " + name);
            try
            {
                var doc = app?.ActiveDocument; var sel = app?.Selection;
                if (doc == null || sel == null) { sb.AppendLine("doc/sel null"); DumpToLog(sb.ToString()); return sb.ToString(); }
                XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                XNamespace m = "http://schemas.openxmlformats.org/officeDocument/2006/math";

                int caret = (decor ?? DecorPlain)(doc, sel, sb);

                // Insert inline OMath via OMML (approche A).
                sel.TypeText("​"); int zwspStart = caret, zwspEnd = sel.Start;
                try { doc.Range(zwspStart, zwspEnd).Font.Hidden = -1; } catch { }
                int phStart = sel.Start; sel.TypeText("□"); int phEnd = sel.Start;
                var phRange = doc.Range(phStart, phEnd);
                var xdoc = XDocument.Parse(phRange.WordOpenXML);
                XElement phRun = null;
                foreach (var r in xdoc.Descendants(w + "r")) { var t = r.Element(w + "t"); if (t != null && t.Value == "□") { phRun = r; break; } }
                if (phRun == null) { sb.AppendLine("run □ introuvable"); DumpToLog(sb.ToString()); return sb.ToString(); }
                phRun.ReplaceWith(MathCursor.Core.LatexToOmml.Convert(@"f\left(x\right)"));
                try { phRange.InsertXML(xdoc.ToString(SaveOptions.DisableFormatting)); }
                catch (Exception ex) { sb.AppendLine("InsertXML ERR: " + ex.Message); DumpToLog(sb.ToString()); return sb.ToString(); }

                Word.OMath om = null;
                foreach (Word.OMath o in doc.Range(phStart, System.Math.Min(doc.Content.End, phStart + 60)).OMaths) { om = o; break; }
                if (om == null) { sb.AppendLine("om introuvable post-insert"); DumpToLog(sb.ToString()); return sb.ToString(); }
                sb.AppendLine($"om=[{om.Range.Start},{om.Range.End}) text=\"{(om.Range.Text ?? "").Trim()}\"");

                // Technique d'échappement.
                try { escape(doc, sel, om); } catch (Exception ex) { sb.AppendLine("escape ERR: " + ex.Message); }
                int caretBeforeType = app.Selection.Start;

                // Frappe témoin.
                app.Selection.TypeText("ABC");
                sb.AppendLine($"caret avant frappe={caretBeforeType}");

                // Verdict : "ABC" en <w:t> (plain ✓) ou <m:t> (math ✗) ?
                var para = doc.Range(zwspStart, zwspStart).Paragraphs[1].Range;
                var px = XDocument.Parse(para.WordOpenXML);
                bool inMath = px.Descendants(m + "t").Any(t => (t.Value ?? "").Contains("ABC"));
                bool inText = px.Descendants(w + "t").Any(t => (t.Value ?? "").Contains("ABC"));
                if (!inMath && !inText)
                {
                    inMath = px.Descendants(m + "t").Any(t => (t.Value ?? "").Contains("A"));
                    inText = px.Descendants(w + "t").Any(t => { var v = t.Value ?? ""; return v.Contains("A") && !v.Contains("Soit"); });
                }
                string verdict = inText && !inMath ? "✓ PLAIN (échappement OK)" : inMath ? "✗ MATH (absorbée)" : "? introuvable";
                sb.AppendLine($"VERDICT: <m:t>={inMath}  <w:t>={inText}  → {verdict}");
                sb.AppendLine($"¶ text=\"{(para.Text ?? "").Replace("\r", "⏎")}\"");
                DumpToLog(sb.ToString()); return sb.ToString();
            }
            catch (Exception ex) { sb.AppendLine("ERR " + ex.Message); DumpToLog(sb.ToString()); return sb.ToString(); }
        }

        // ─── BATTERIE OMML : insère plein de cas via LatexToOmml ──────────
        // Un seul clic → tous les cas rendus dans le doc (chacun sur sa ligne)
        // + XML loggé. Pour valider la couverture de l'émetteur en une session.
        public static string RunOmmlBattery(Word.Application app)
        {
            var sb = StartTrace("BATTERIE OMML : LatexToOmml → InsertXML, chaque cas sur sa ligne.");
            var cases = new (string label, string latex)[]
            {
                ("frac",        @"\frac{1}{x+1}"),
                ("lim/frac",    @"\lim_{x \to 0} \frac{1}{x+1}"),
                ("sqrt",        @"\sqrt{x+1}"),
                ("sqrt[n]",     @"\sqrt[3]{8}"),
                ("sup",         @"x^{2}"),
                ("sub",         @"u_{n}"),
                ("subsup",      @"x_{i}^{2}"),
                ("sum",         @"\sum_{k=1}^{n} \frac{1}{k}"),
                ("int",         @"\int_{0}^{1} x^{2}"),
                ("vec",         @"\vec{AB}"),
                ("setminus",    @"\mathbb{R} \setminus \{0\}"),
                ("relation",    @"f(x) = \frac{1}{x}+1"),
                ("interval",    @"[0;1] \cup [2;3]"),
                ("nested",      @"\frac{1}{\sum_{k=1}^{n} \frac{1}{k+1}}"),
                ("greek/sup",   @"\alpha^{2}+\beta"),
            };
            try
            {
                var doc = app?.ActiveDocument;
                var sel = app?.Selection;
                if (doc == null || sel == null) { sb.AppendLine("doc/sel null"); return sb.ToString(); }

                foreach (var (label, latex) in cases)
                {
                    try
                    {
                        sel.SetRange(doc.Content.End, doc.Content.End);
                        sel.TypeText(label + " :  ");
                        // oMath inline après le label (même ¶).
                        var para = sel.Paragraphs[1].Range;
                        var xdoc = XDocument.Parse(para.WordOpenXML);
                        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                        var pEl = xdoc.Descendants(w + "p").FirstOrDefault();
                        if (pEl == null) { sb.AppendLine($"[{label}] <w:p> introuvable"); continue; }
                        pEl.Add(MathCursor.Core.LatexToOmml.Convert(latex));
                        para.InsertXML(xdoc.ToString(SaveOptions.DisableFormatting));
                        sel.SetRange(doc.Content.End, doc.Content.End);
                        sel.TypeText("\r");
                        sb.AppendLine($"[{label}] OK  latex={latex}");
                    }
                    catch (Exception exC) { sb.AppendLine($"[{label}] ERR: {exC.Message}  latex={latex}"); }
                }
                DumpToLog(sb.ToString());
                return sb.ToString();
            }
            catch (Exception ex) { sb.AppendLine("ERR " + ex.Message); DumpToLog(sb.ToString()); return sb.ToString(); }
        }

        // ─── POC OMML CHEMIN COMPLET : réplique InsertOMathAt avec OMML ───
        // Teste l'intégration RÉELLE : ZWSP caché → oMath inline (InsertXML,
        // même ¶) → anchor CC sur le ZWSP → VÉRIFIE que le backlink
        // CcMetaResolver.ResolveAt(om) retrouve le CC. C'est l'étape qui
        // dé-risque le remplacement de TypeText+BuildUp par OMML.
        public static string RunPocOmmlFullSequence(Word.Application app)
        {
            var sb = StartTrace("POC OMML CHEMIN COMPLET: ZWSP → oMath inline (même ¶, InsertXML) → anchor CC → vérifie backlink. Pas de BuildUp.");
            try
            {
                var doc = app?.ActiveDocument;
                var sel = app?.Selection;
                if (doc == null || sel == null) { sb.AppendLine("doc/sel null"); return sb.ToString(); }

                XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                XNamespace m = "http://schemas.openxmlformats.org/officeDocument/2006/math";
                Func<string, XElement> R = t => new XElement(m + "r", new XElement(m + "t", t));
                Func<XElement> BuildOMath = () => new XElement(m + "oMath",
                    new XElement(m + "func",
                        new XElement(m + "fName", new XElement(m + "limLow",
                            new XElement(m + "e", R("lim")),
                            new XElement(m + "lim", R("x→0")))),
                        new XElement(m + "e", new XElement(m + "f",
                            new XElement(m + "num", R("1")),
                            new XElement(m + "den", R("x+1"))))));

                // 1. ZWSP caché = ancre du CC (InsertOMathAt étape 4).
                int caretBefore = sel.Start;
                sel.TypeText("​");
                int zwspStart = caretBefore, zwspEnd = sel.Start;
                try { doc.Range(zwspStart, zwspEnd).Font.Hidden = -1; } catch { }
                sb.AppendLine($"[1] ZWSP caché [{zwspStart},{zwspEnd})");

                // 2. oMath INLINE dans le MÊME ¶, APRÈS le ZWSP. Lecture
                //    WordOpenXML du ¶ courant (local 1 ¶), append oMath, InsertXML.
                var para = sel.Paragraphs[1].Range;
                sb.AppendLine($"[2] ¶ courant [{para.Start},{para.End})");
                var xdoc = XDocument.Parse(para.WordOpenXML);
                var pEl = xdoc.Descendants(w + "p").FirstOrDefault();
                if (pEl == null) { sb.AppendLine("[2] <w:p> introuvable"); DumpToLog(sb.ToString()); return sb.ToString(); }
                pEl.Add(BuildOMath()); // append après les runs existants (le ZWSP)
                try
                {
                    para.InsertXML(xdoc.ToString(SaveOptions.DisableFormatting));
                    sb.AppendLine("[2b] InsertXML (oMath inline après ZWSP) OK");
                }
                catch (Exception exI) { sb.AppendLine("[2b] InsertXML ERR: " + exI.Message); DumpToLog(sb.ToString()); return sb.ToString(); }

                // 3. Probe l'OMath.
                Word.OMath om = null;
                try { foreach (Word.OMath o in sel.Paragraphs[1].Range.OMaths) { om = o; break; } } catch { }
                if (om == null) { sb.AppendLine("[3] OMath introuvable après InsertXML"); DumpToLog(sb.ToString()); return sb.ToString(); }
                sb.AppendLine($"[3] OMath [{om.Range.Start},{om.Range.End}) type={om.Type}");
                try { om.Justification = Word.WdOMathJc.wdOMathJcLeft; } catch { }

                // 4. Anchor CC sur le char juste AVANT l'OMath (= le ZWSP).
                Word.ContentControl cc = null;
                try
                {
                    var anchorRange = doc.Range(om.Range.Start - 1, om.Range.Start);
                    cc = anchorRange.ContentControls.Add(Word.WdContentControlType.wdContentControlRichText);
                    cc.Title = MathCursor.Host.CCMeta.MCMetaJson.CcTitle;
                    try { cc.Appearance = Word.WdContentControlAppearance.wdContentControlHidden; } catch { }
                    sb.AppendLine($"[4] anchor CC [{cc.Range.Start},{cc.Range.End})");
                }
                catch (Exception exCc) { sb.AppendLine("[4] CC ERR: " + exCc.Message); }

                // 5. VÉRIF CRUCIALE : le backlink est-il retrouvé depuis l'OMath ?
                try
                {
                    var (foundCc, meta) = MathCursor.Host.CCMeta.CcMetaResolver.ResolveAt(om);
                    sb.AppendLine($"[5] CcMetaResolver.ResolveAt(om) → {(foundCc == null ? "NULL ⇒ BACKLINK CASSÉ" : $"OK cc=[{foundCc.Range.Start},{foundCc.Range.End}]")}");
                }
                catch (Exception exR) { sb.AppendLine("[5] ResolveAt ERR: " + exR.Message); }

                try { sb.AppendLine("[6] OMath XML :\n" + ExtractMathXml(om.Range.WordOpenXML ?? "")); } catch { }

                var outTrace = Finalize(sb, om, cc);
                DumpToLog(outTrace);
                return outTrace;
            }
            catch (Exception ex) { sb.AppendLine("ERR " + ex.Message); DumpToLog(sb.ToString()); return sb.ToString(); }
        }

        // ─── POC : delete OMath + anchor CC au caret (= bloc-revert isolé) ───

        public static string RunPocDeleteOMathAndAnchor(Word.Application app)
        {
            var sb = StartTrace("POC DELETE: supprime l'OMath au caret + son anchor CC. Pas de TypeText. Pour isoler le problème de suppression du flow revert.");
            try
            {
                var doc = app?.ActiveDocument;
                var sel = app?.Selection;
                if (doc == null || sel == null) { sb.AppendLine("doc/sel null"); return sb.ToString(); }

                sb.AppendLine($"[INITIAL] caret={sel.Start} docEnd={doc.Content.End} paraCount={doc.Paragraphs.Count}");

                // ── 1. Trouve l'OMath au caret ────────────────────────
                Word.OMath om = null;
                try
                {
                    foreach (Word.OMath o in sel.OMaths) { om = o; break; }
                    if (om == null)
                    {
                        var paraRange = sel.Paragraphs[1].Range;
                        int caret = sel.Start;
                        foreach (Word.OMath o in paraRange.OMaths)
                        {
                            if (caret > o.Range.Start && caret < o.Range.End) { om = o; break; }
                        }
                    }
                }
                catch (Exception ex) { sb.AppendLine($"OMath find ERR: {ex.Message}"); }
                if (om == null) { sb.AppendLine("[FAIL] Pas d'OMath au caret. Place le caret DANS une OMath et clique encore."); return sb.ToString(); }

                int omS = om.Range.Start, omE = om.Range.End;
                sb.AppendLine($"[STEP 1] OMath trouvée : Range=[{omS},{omE}) Type={om.Type}");

                // ── 2. Trouve l'anchor CC via backward probe ─────────
                Word.ContentControl cc = null;
                for (int delta = 1; delta <= 5 && cc == null; delta++)
                {
                    int p = omS - delta;
                    if (p < 0) break;
                    try
                    {
                        var probe = doc.Range(p, p + 1).ParentContentControl;
                        if (probe != null && probe.Title == MathCursor.Host.CCMeta.MCMetaJson.CcTitle)
                        {
                            cc = probe;
                            sb.AppendLine($"[STEP 2] anchor CC trouvé via backward probe delta={delta} : cc.Range=[{cc.Range.Start},{cc.Range.End}) Title=\"{cc.Title}\" Tag.Len={(cc.Tag ?? "").Length}");
                        }
                    }
                    catch { }
                }
                if (cc == null)
                {
                    // Fallback : essaie le ParentContentControl direct
                    try { cc = om.Range.ParentContentControl; } catch { }
                    if (cc != null && cc.Title == MathCursor.Host.CCMeta.MCMetaJson.CcTitle)
                        sb.AppendLine($"[STEP 2] anchor CC trouvé via om.Range.ParentContentControl : cc.Range=[{cc.Range.Start},{cc.Range.End})");
                    else { cc = null; sb.AppendLine("[STEP 2] Aucun anchor CC trouvé"); }
                }

                int ccS = cc?.Range.Start ?? -1;
                int ccE = cc?.Range.End ?? -1;
                bool ccLock = false;
                try { ccLock = cc?.LockContentControl ?? false; } catch { }
                sb.AppendLine($"[STEP 2.1] cc.LockContentControl = {ccLock}");

                // ── 3. Tente cc.Delete(true) D'ABORD ─────────────────
                if (cc != null)
                {
                    // Unlock
                    try { cc.LockContents = false; sb.AppendLine($"[STEP 3.1] cc.LockContents = false OK"); }
                    catch (Exception ex) { sb.AppendLine($"[STEP 3.1] LockContents err: {ex.Message}"); }
                    try { cc.LockContentControl = false; sb.AppendLine($"[STEP 3.2] cc.LockContentControl = false OK"); }
                    catch (Exception ex) { sb.AppendLine($"[STEP 3.2] LockContentControl err: {ex.Message}"); }

                    int dEndBefore = doc.Content.End;
                    try
                    {
                        cc.Delete(true);
                        int dEndAfter = doc.Content.End;
                        sb.AppendLine($"[STEP 3.3] cc.Delete(true) appelé, docEnd: {dEndBefore} → {dEndAfter} (delta={dEndBefore - dEndAfter})");
                    }
                    catch (Exception ex) { sb.AppendLine($"[STEP 3.3] cc.Delete ERR: {ex.Message}"); }
                }

                // ── 4. Tente om.Range.Delete() ───────────────────────
                int dEndBeforeOm = doc.Content.End;
                int omParaCountBefore = doc.Paragraphs.Count;
                try
                {
                    // Re-fetch om car positions ont pu shifter
                    // (mais om.Range est COM, devrait être live)
                    int omSnow = om.Range.Start;
                    int omEnow = om.Range.End;
                    sb.AppendLine($"[STEP 4.0] om.Range maintenant = [{omSnow},{omEnow}) (était [{omS},{omE}))");
                    om.Range.Delete();
                    int dEndAfterOm = doc.Content.End;
                    int omParaCountAfter = doc.Paragraphs.Count;
                    sb.AppendLine($"[STEP 4.1] om.Range.Delete appelé, docEnd: {dEndBeforeOm} → {dEndAfterOm} (delta={dEndBeforeOm - dEndAfterOm}) paraCount: {omParaCountBefore} → {omParaCountAfter}");
                }
                catch (Exception ex) { sb.AppendLine($"[STEP 4.1] om.Range.Delete ERR: {ex.Message}"); }

                // ── 5. État final du paragraph ───────────────────────
                sb.AppendLine();
                sb.AppendLine("─── État final ───────────────────────────────────");
                try
                {
                    var paraRange = sel.Paragraphs[1].Range;
                    string txt = paraRange.Text ?? "";
                    sb.AppendLine($"paragraph.Range=[{paraRange.Start},{paraRange.End}) text.Length={txt.Length}");
                    sb.AppendLine($"paragraph chars: {CharCodes(txt)}");
                    int omLeft = 0, ccLeft = 0;
                    try { omLeft = paraRange.OMaths.Count; } catch { }
                    try { ccLeft = paraRange.ContentControls.Count; } catch { }
                    sb.AppendLine($"paragraph residus : OMaths={omLeft} CCs={ccLeft}");
                }
                catch (Exception ex) { sb.AppendLine("para state err: " + ex.Message); }

                sb.AppendLine();
                sb.AppendLine("─── OOXML (body) ─────────────────────────────────");
                try
                {
                    string xml = sel.Paragraphs[1].Range.WordOpenXML ?? "";
                    int b = xml.IndexOf("<w:body>", StringComparison.Ordinal);
                    int e = xml.IndexOf("</w:body>", StringComparison.Ordinal);
                    string slice = (b >= 0 && e > b) ? xml.Substring(b, e - b + 9) : xml;
                    if (slice.Length > 1500) slice = slice.Substring(0, 1500) + "…[truncated]";
                    sb.AppendLine(slice);
                }
                catch (Exception ex) { sb.AppendLine("XML dump err: " + ex.Message); }

                return sb.ToString();
            }
            catch (Exception ex) { sb.AppendLine("FATAL: " + ex.Message); return sb.ToString(); }
        }

        // ─── Variante G3 : G + CC RichText sur om.Range APRÈS Justification + délai ───

        public static string RunVariantG3_CcOmRangeLate(Word.Application app)
        {
            var sb = StartTrace("G3: G + CC RichText sur om.Range TOUT À LA FIN (après Type, Justification). Test : Word a-t-il finalisé l'OMathPara avant le CC ?");
            try
            {
                var (doc, sel, _) = Setup(app, sb);
                if (doc == null) return sb.ToString();

                sel.TypeText(DefaultSource);
                int afterEnd = sel.Start;
                int srcStart = afterEnd - DefaultSource.Length;

                var typedRange = doc.Range(srcStart, afterEnd);
                var added = typedRange.OMaths.Add(typedRange);
                added.OMaths.BuildUp();
                var om = FirstOMath(added);
                if (om == null) { sb.AppendLine("OMath null"); return sb.ToString(); }
                sb.AppendLine($"[2] OMaths.Add+BuildUp: om.Range=[{om.Range.Start},{om.Range.End}) om.Type={om.Type}");

                om.Type = Word.WdOMathType.wdOMathDisplay;
                om.Justification = Word.WdOMathJc.wdOMathJcLeft;
                sb.AppendLine($"[3] Type=Display set: om.Type={om.Type}  OOXML stabilisé");

                // Maintenant que tout est stable, wrap CC sur om.Range
                Word.ContentControl cc = null;
                try
                {
                    cc = om.Range.ContentControls.Add(Word.WdContentControlType.wdContentControlRichText);
                    cc.Title = MathCursor.Host.CCMeta.MCMetaJson.CcTitle;
                    cc.Appearance = Word.WdContentControlAppearance.wdContentControlHidden;
                    sb.AppendLine($"[4] CC RichText sur om.Range : cc.Range=[{cc.Range.Start},{cc.Range.End})");
                }
                catch (Exception exCc) { sb.AppendLine("[4] CC wrap ERR: " + exCc.Message); }

                return Finalize(sb, om, cc);
            }
            catch (Exception ex) { sb.AppendLine("ERR " + ex.Message); return sb.ToString(); }
        }

        // ─── Helpers ─────────────────────────────────────────────────

        // Écrit la trace POC dans mathcursor.log (le pane inspecteur n'y va pas).
        private static void DumpToLog(string trace)
        {
            try
            {
                var dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MathCursor", "logs");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "mathcursor.log"),
                    "\n===== POC OMML TRACE =====\n" + trace + "\n");
            }
            catch { }
        }

        // Isole le <m:oMath>…</m:oMath> du paquet WordOpenXML pour lecture.
        private static string ExtractMathXml(string fullXml)
        {
            if (string.IsNullOrEmpty(fullXml)) return "(vide)";
            int a = fullXml.IndexOf("<m:oMath", StringComparison.Ordinal);
            int b = fullXml.LastIndexOf("</m:oMath", StringComparison.Ordinal);
            if (a >= 0 && b > a)
            {
                int end = fullXml.IndexOf('>', b);
                return end > a ? fullXml.Substring(a, end - a + 1) : fullXml.Substring(a);
            }
            return fullXml.Length > 2000 ? fullXml.Substring(0, 2000) + "…" : fullXml;
        }

        private static StringBuilder StartTrace(string description)
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════════════════");
            sb.AppendLine($" Variant : {description}");
            sb.AppendLine($" Source  : \"{DefaultSource}\"");
            sb.AppendLine("═══════════════════════════════════════════════════════");
            return sb;
        }

        private static (Word.Document doc, Word.Selection sel, int insertPos) Setup(Word.Application app, StringBuilder sb)
        {
            var doc = app?.ActiveDocument;
            var sel = app?.Selection;
            if (doc == null || sel == null) { sb.AppendLine("doc/sel null"); return (null, null, 0); }
            int pos = sel.Start;
            sb.AppendLine($"[0] insertPos={pos}  docEnd={doc.Content.End}");
            return (doc, sel, pos);
        }

        private static Word.OMath FirstOMath(Word.Range range)
        {
            try { foreach (Word.OMath o in range.OMaths) return o; }
            catch { }
            return null;
        }

        private static string Finalize(StringBuilder sb, Word.OMath om, Word.ContentControl cc)
        {
            sb.AppendLine();
            sb.AppendLine("─── État final ─────────────────────────────────────");
            if (om != null)
            {
                sb.AppendLine($"om.Range = [{om.Range.Start},{om.Range.End})  om.Type = {om.Type}");
                try
                {
                    var paraRange = om.Range.Paragraphs[1].Range;
                    sb.AppendLine($"om paragraph.Range = [{paraRange.Start},{paraRange.End})  text.Length = {(paraRange.Text ?? "").Length}");
                    sb.AppendLine($"om paragraph chars : {CharCodes(paraRange.Text ?? "")}");
                }
                catch (Exception ex) { sb.AppendLine("paragraph err: " + ex.Message); }
            }
            if (cc != null)
            {
                sb.AppendLine($"cc.Range = [{cc.Range.Start},{cc.Range.End})  Title=\"{cc.Title}\"  Lock={cc.LockContents}/{cc.LockContentControl}");
            }

            // OOXML dump
            sb.AppendLine();
            sb.AppendLine("─── OOXML (body) ───────────────────────────────────");
            try
            {
                Word.Range xmlRange = om?.Range.Paragraphs[1].Range ?? cc?.Range;
                if (xmlRange != null)
                {
                    string xml = xmlRange.WordOpenXML ?? "";
                    int b = xml.IndexOf("<w:body>", StringComparison.Ordinal);
                    int e = xml.IndexOf("</w:body>", StringComparison.Ordinal);
                    string slice = (b >= 0 && e > b) ? xml.Substring(b, e - b + 9) : xml;
                    if (slice.Length > 1800) slice = slice.Substring(0, 1800) + "…[truncated " + (slice.Length - 1800) + " more]";
                    sb.AppendLine(slice);
                }
            }
            catch (Exception ex) { sb.AppendLine("XML dump err: " + ex.Message); }
            return sb.ToString();
        }

        private static string CharCodes(string s)
        {
            var b = new StringBuilder();
            for (int i = 0; i < s.Length && i < 40; i++)
            {
                char c = s[i];
                if (c >= 32 && c < 127) b.Append(c);
                else b.Append($"[{(int)c:X2}]");
            }
            if (s.Length > 40) b.Append("…");
            return b.ToString();
        }
    }
}
