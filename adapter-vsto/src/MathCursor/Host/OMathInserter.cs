using System;
using MathCursor.Host.CCMeta;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host
{
    /// <summary>
    /// Insertion d'UNE équation OMath dans Word — extraction des primitives
    /// validées de l'ex-SuggestionService (POC + ADRs). Séquence (ORDRE
    /// CRITIQUE, cf. ADR 2026-05-19-Feat-anchor-cc-pattern §1bis et
    /// ADR 2026-06-02-Feat-omml-insertion) :
    ///
    /// <list type="number">
    /// <item>Normalisation des bornes en positions internes Word
    ///   (<c>SetRange</c> + readback — Word snap silencieusement).</item>
    /// <item><see cref="ZoneCleaner.ClearZone"/> : vide structurellement la
    ///   plage (CCs + OMaths + plain text).</item>
    /// <item>ZWSP en plain text + <c>Font.Hidden</c> (après cc.Add si en
    ///   liste — workaround bug Word).</item>
    /// <item>OMML natif (<see cref="MathCursor.Serialization.LatexToOmml"/>)
    ///   inséré CHIRURGICALEMENT via <c>InsertXML</c> sur une range
    ///   placeholder 1-char — Word ne re-parse rien.</item>
    /// <item><see cref="DecideOMathTyping"/> : Display si seule dans son
    ///   contexte, Inline sinon ; toujours alignée à gauche.</item>
    /// <item>Anchor CC EN DERNIER (Tag JSON <see cref="MCMeta"/>).</item>
    /// <item>Échappement caret <c>MoveRight</c> (clôt la saisie math).</item>
    /// </list>
    /// </summary>
    internal sealed class OMathInserter
    {
        private readonly Word.Application _app;
        private readonly Action<string> _log;

        public OMathInserter(Word.Application app, Action<string> log = null)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _log = log ?? (_ => { });
        }

        /// <summary>Génère un handle id unique, persisté dans cc.Tag.</summary>
        public static string NewHandleId() => "eq_" + Guid.NewGuid().ToString("N").Substring(0, 12);

        /// <summary>
        /// Remplace <c>[absStart, absEnd)</c> par l'OMath rendant
        /// <paramref name="latex"/>, avec anchor CC portant
        /// <paramref name="source"/> (sténo brute). Retourne les bornes de
        /// l'OMath inséré + son handle (null si échec).
        /// </summary>
        public (int newStart, int newEnd, string newHandle) Insert(
            int absStart, int absEnd, string latex, string source)
        {
            var doc = _app.ActiveDocument;
            _log($"OMathInserter: IN [{absStart},{absEnd}) latex=\"{Preview(latex)}\"");
            if (doc == null) return (absStart, absEnd, null);

            // Clamp + trim whitespaces aux bords.
            int docStart = doc.Content.Start, docEnd = doc.Content.End;
            if (absStart < docStart) absStart = docStart;
            if (absEnd > docEnd) absEnd = docEnd;
            if (absEnd <= absStart) return (absStart, absEnd, null);
            while (absStart < absEnd && IsWhitespaceCharAt(doc, absStart)) absStart++;
            while (absEnd > absStart && IsWhitespaceCharAt(doc, absEnd - 1)) absEnd--;
            if (absEnd <= absStart) return (absStart, absEnd, null);

            var sel = _app.Selection;
            if (sel == null) return (absStart, absEnd, null);

            // 1. Normalisation des bornes en positions internes Word.
            int internalStart, internalEnd;
            try
            {
                sel.SetRange(absStart, absStart);
                internalStart = sel.Start;
                sel.SetRange(absEnd, absEnd);
                internalEnd = sel.Start;
            }
            catch (Exception ex) { _log("insert_normalize_error: " + ex.Message); return (absStart, absEnd, null); }

            // 2. Cleanup structurel (CCs + OMaths résiduelles + plain text).
            int afterCleanupPos;
            try { afterCleanupPos = ZoneCleaner.ClearZone(doc, internalStart, internalEnd, _log); }
            catch (Exception ex) { _log("insert_clearzone_error: " + ex.Message); return (absStart, absEnd, null); }

            // 3. SetRange collapsed sur la position post-cleanup.
            try { sel.SetRange(afterCleanupPos, afterCleanupPos); }
            catch (Exception ex) { _log("insert_setrange_error: " + ex.Message); return (absStart, absEnd, null); }

            // 3b. Détection liste : Font.Hidden APRÈS cc.Add + Inline forcé
            //     (cf. bug 2026-05-20, wrap du run vanish foiré par Word).
            bool isInList = false;
            try
            {
                var listFormat = doc.Range(afterCleanupPos, afterCleanupPos).Paragraphs[1].Range.ListFormat;
                isInList = listFormat != null && listFormat.ListType != Word.WdListType.wdListNoNumbering;
            }
            catch (Exception exL) { _log("insert_list_probe_error: " + exL.Message); }

            // 4. ZWSP en plain text (PAS encore CC).
            int caretBeforeZwsp = sel.Start;
            try { sel.TypeText("​"); }
            catch (Exception ex) { _log("insert_zwsp_typetext_error: " + ex.Message); return (absStart, absEnd, null); }
            int zwspStart = caretBeforeZwsp;
            int zwspEnd = sel.Start;
            if (!isInList)
            {
                try { doc.Range(zwspStart, zwspEnd).Font.Hidden = -1; } catch { }
            }

            // 5-6. OMML inséré chirurgicalement + typage Display/Inline.
            int newStart = zwspEnd, newEnd = zwspEnd;
            Word.OMath om;
            try
            {
                om = BuildOMathViaOmml(doc, sel, latex, zwspEnd);
                if (om != null)
                {
                    newStart = om.Range.Start;
                    newEnd = om.Range.End;

                    var (omType, omJc) = isInList
                        ? (Word.WdOMathType.wdOMathInline, Word.WdOMathJc.wdOMathJcLeft)
                        : DecideOMathTyping(om, source, _log);

                    Word.WdOMathType currentType;
                    try { currentType = om.Type; }
                    catch { currentType = Word.WdOMathType.wdOMathInline; }
                    if (currentType != omType)
                    {
                        try { om.Type = omType; }
                        catch (Exception exType) { _log("insert_omath_type_error: " + exType.Message); }
                    }
                    try { om.Justification = omJc; }
                    catch (Exception exJc) { _log("insert_omath_jc_error: " + exJc.Message); }
                    newStart = om.Range.Start;
                    newEnd = om.Range.End;
                }
                else _log("OMathInserter: OMML inséré mais OMath introuvable au re-probe");
            }
            catch (Exception ex) { _log("insert_omml_error: " + ex.Message); return (zwspEnd, zwspEnd, null); }

            // 7. Anchor CC EN DERNIER (le math est settled, la CC ne le perturbe plus).
            Word.ContentControl cc = null;
            try
            {
                var anchorRange = doc.Range(zwspStart, zwspEnd);
                cc = anchorRange.ContentControls.Add(Word.WdContentControlType.wdContentControlRichText);
                cc.Title = MCMetaJson.CcTitle;
                try { cc.Appearance = Word.WdContentControlAppearance.wdContentControlHidden; } catch { }
                try { cc.LockContentControl = false; } catch { }
                try { cc.LockContents = false; } catch { }
                if (isInList)
                {
                    try { cc.Range.Font.Hidden = -1; } catch (Exception exH) { _log("insert_cc_font_hidden_error: " + exH.Message); }
                }

                // Re-probe om : les positions ont pu shifter post-CC wrap.
                try
                {
                    foreach (Word.OMath o2 in doc.Range(cc.Range.End,
                        Math.Min(doc.Content.End, cc.Range.End + (newEnd - newStart) + 5)).OMaths)
                    { om = o2; break; }
                    if (om != null) { newStart = om.Range.Start; newEnd = om.Range.End; }
                }
                catch (Exception exP) { _log("insert_reprobe_om_error: " + exP.Message); }

                // Caret APRÈS l'OMath + MoveRight : franchit la frontière et
                // CLÔT la saisie math (sinon frappe suivante en italique math).
                if (om != null)
                {
                    try
                    {
                        _app.Selection.SetRange(om.Range.End, om.Range.End);
                        _app.Selection.MoveRight(Word.WdUnits.wdCharacter, 1, Word.WdMovementType.wdMove);
                    }
                    catch { }
                }
            }
            catch (Exception exCc) { _log("insert_anchor_cc_error: " + exCc.Message); cc = null; }

            // 8. Tag JSON sur la CC (hash POST wrap → store-hash == read-hash).
            string newHandle = null;
            if (cc != null && om != null)
            {
                try
                {
                    newHandle = NewHandleId();
                    string hash = Sha1Helper.Compute(om.Range.WordOpenXML ?? "");
                    var meta = new MCMeta
                    {
                        V = 1,
                        HandleId = newHandle,
                        Steno = source ?? "",
                        Latex = latex ?? "",
                        Version = typeof(OMathInserter).Assembly.GetName().Version?.ToString() ?? "0",
                        OmmlHash = hash,
                        ParsedAt = DateTime.UtcNow,
                    };
                    cc.Tag = MCMetaJson.Serialize(meta);
                }
                catch (Exception exTag) { _log("insert_tag_error: " + exTag.Message); }
            }

            _log($"OMathInserter: OUT range=[{newStart},{newEnd}) handle={(newHandle ?? "null")}");
            return (newStart, newEnd, newHandle);
        }

        /// <summary>
        /// LaTeX → OMML natif inséré sur une range placeholder 1-char à
        /// <paramref name="mathStart"/> (juste après le ZWSP). JAMAIS sur le
        /// ¶ entier (casse positions + prose inline). Lecture WordOpenXML
        /// LOCALE (¶ courant), pas O(doc). Renvoie null si échec.
        /// </summary>
        private Word.OMath BuildOMathViaOmml(Word.Document doc, Word.Selection sel, string latex, int mathStart)
        {
            var w = System.Xml.Linq.XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
            System.Xml.Linq.XElement oMath;
            try { oMath = MathCursor.Serialization.LatexToOmml.Convert(latex); }
            catch (Exception ex) { _log("BuildOMathViaOmml: LatexToOmml error: " + ex.Message); return null; }
            if (oMath == null) return null;

            // Placeholder éphémère 1-char à la position math.
            sel.SetRange(mathStart, mathStart);
            int phStart = sel.Start;
            sel.TypeText("□");
            int phEnd = sel.Start;
            var phRange = doc.Range(phStart, phEnd);

            System.Xml.Linq.XDocument xdoc;
            try { xdoc = System.Xml.Linq.XDocument.Parse(phRange.WordOpenXML); }
            catch (Exception ex) { _log("BuildOMathViaOmml: parse WordOpenXML error: " + ex.Message); return null; }

            System.Xml.Linq.XElement phRun = null;
            foreach (var r in xdoc.Descendants(w + "r"))
            {
                var t = r.Element(w + "t");
                if (t != null && t.Value == "□") { phRun = r; break; }
            }
            if (phRun == null) { _log("BuildOMathViaOmml: run placeholder introuvable"); return null; }
            phRun.ReplaceWith(oMath);

            try { phRange.InsertXML(xdoc.ToString(System.Xml.Linq.SaveOptions.DisableFormatting)); }
            catch (Exception ex) { _log("BuildOMathViaOmml: InsertXML error: " + ex.Message); return null; }

            // Re-probe LOCAL de l'OMath fraîchement insérée.
            Word.OMath om = null;
            try
            {
                int probeEnd = Math.Min(doc.Content.End, phStart + 200);
                foreach (Word.OMath o in doc.Range(phStart, probeEnd).OMaths) { om = o; break; }
            }
            catch (Exception ex) { _log("BuildOMathViaOmml: probe error: " + ex.Message); }
            return om;
        }

        /// <summary>
        /// Display si l'OMath est seule dans son contexte structurel (¶ vide,
        /// cellule vide), Inline sinon ou si la source commence par un espace
        /// (override user explicite). Toujours alignée à gauche.
        /// </summary>
        private static (Word.WdOMathType type, Word.WdOMathJc justification)
            DecideOMathTyping(Word.OMath om, string source, Action<string> log)
        {
            var fallback = (Word.WdOMathType.wdOMathInline, Word.WdOMathJc.wdOMathJcLeft);
            if (om == null) return fallback;
            try
            {
                if (!string.IsNullOrEmpty(source) && source.StartsWith(" ")) return fallback;

                string paraText, omText;
                try
                {
                    paraText = om.Range.Paragraphs[1].Range.Text ?? "";
                    omText = om.Range.Text ?? "";
                }
                catch { return fallback; }

                // Strip OMath + chars structurels Word (\r \n \v \a \t \f + ZWSP).
                string remaining = paraText.Replace(omText, "")
                    .Replace("\r", "").Replace("\n", "")
                    .Replace("\v", "").Replace("\a", "")
                    .Replace("\t", "").Replace("\f", "")
                    .Replace("​", "")
                    .Trim();

                if (string.IsNullOrEmpty(remaining))
                    return (Word.WdOMathType.wdOMathDisplay, Word.WdOMathJc.wdOMathJcLeft);
                return fallback;
            }
            catch (Exception ex)
            {
                log?.Invoke("decide_typing_error: " + ex.Message);
                return fallback;
            }
        }

        private static bool IsWhitespaceCharAt(Word.Document doc, int pos)
        {
            try
            {
                var t = doc.Range(pos, pos + 1).Text;
                return !string.IsNullOrEmpty(t) && char.IsWhiteSpace(t[0]);
            }
            catch { return false; }
        }

        private static string Preview(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
            return s.Length > 120 ? s.Substring(0, 120) + "…" : s;
        }
    }
}
