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
