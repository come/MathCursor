using System;
using System.Reflection;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.Layout
{
    /// <summary>
    /// Finalise le layout du doc après l'insertion d'une OMath. Couvre :
    /// strip ¶ vide résiduel post-cross-merge, alignment OMath ↔ ¶,
    /// création éventuelle d'un ¶ d'atterrissage caret, positionnement
    /// caret après l'OMath.
    ///
    /// <para>Bounded context DDD "post-commit layout" (P2.13 du refactor archi) :
    /// regroupe les corrections cosmétiques du doc qui suivent l'insertion
    /// d'une OMath via la pipeline. Pas concerné par le commit lui-même
    /// (= responsabilité des <see cref="Inserters"/>).</para>
    /// </summary>
    internal sealed class PostCommitLayoutFinalizer
    {
        private readonly Word.Application _app;
        private readonly Func<bool> _wasXmlTransplant;
        private readonly Action<string> _log;

        public PostCommitLayoutFinalizer(
            Word.Application app,
            Func<bool> wasXmlTransplant,
            Action<string> log)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _wasXmlTransplant = wasXmlTransplant ?? (() => false);
            _log = log ?? (s => { });
        }

        /// <summary>
        /// Pipeline complet de finalisation post-cross-merge :
        /// (1) strip ¶ vide résiduel à l'amont du nouvel OMath,
        /// (2) sync alignment OMath ↔ ¶ (skip si transplant XML l'a fait),
        /// (3) append ¶ vide d'atterrissage si l'OMath est dernier ¶,
        /// (4) caret au début du ¶ suivant.
        /// </summary>
        public void FinalizeCrossMerge(Word.Document doc, int replaceStart,
            ref int newStart, ref int newEnd, out bool didCreateAnchorPara)
        {
            didCreateAnchorPara = false;
            try
            {
                StripLeadingResidualEmptyParagraph(doc, replaceStart, ref newStart, ref newEnd);
                if (!_wasXmlTransplant())
                {
                    EnforceOMathParagraphAlignment(doc, newStart);
                }
                int caretPos = AppendEmptyParagraphAfterOMath(doc, newStart, out didCreateAnchorPara);
                if (caretPos >= 0) SetCaretAtPosition(caretPos);
            }
            catch (Exception ex) { _log("xparMerge_finalize_error: " + ex.Message); }
        }

        /// <summary>
        /// Crée un ¶ vide après l'OMath uniquement si l'OMath est le dernier
        /// ¶ du doc (sinon le ¶ suivant existe déjà comme landing zone).
        /// Retourne la position du caret final, ou -1 si pas trouvé.
        /// </summary>
        public int AppendEmptyParagraphAfterOMath(Word.Document doc, int posInOMath, out bool didCreateNewPara)
        {
            didCreateNewPara = false;
            try
            {
                foreach (Word.OMath om in doc.OMaths)
                {
                    if (om.Range.Start > posInOMath || om.Range.End <= posInOMath) continue;
                    var omPara = om.Range.Paragraphs[1];
                    int afterOMathPara = omPara.Range.End;

                    if (afterOMathPara >= doc.Content.End)
                    {
                        omPara.Range.InsertParagraphAfter();
                        didCreateNewPara = true;
                        _log("append_para: OMath était last para, ¶ vide créé pour caret");
                    }
                    return afterOMathPara;
                }
            }
            catch (Exception ex) { _log("xparMerge_append_para_error: " + ex.Message); }
            return -1;
        }

        public void SetCaretAtPosition(int caretPos)
        {
            try { _app.Selection.SetRange(caretPos, caretPos); }
            catch (Exception ex) { _log("xparMerge_setcaret_error: " + ex.Message); }
        }

        /// <summary>Aligne l'OMath couvrant <paramref name="pos"/> sur l'alignement du ¶.</summary>
        public void EnforceOMathParagraphAlignment(Word.Document doc, int pos)
        {
            try { SyncOMathJustificationToParagraph(doc, pos); }
            catch (Exception ex) { _log("xparMerge_enforce_align_error: " + ex.Message); }
        }

        // ── Internals ────────────────────────────────────────────────

        private void StripLeadingResidualEmptyParagraph(Word.Document doc, int replaceStart, ref int newStart, ref int newEnd)
        {
            if (newStart <= doc.Content.Start) return;
            try
            {
                var prevRange = doc.Range(newStart - 1, newStart - 1).Paragraphs[1].Range;
                // Garde anti-faux-positif : ne strip pas un ¶ utilisateur hors range.
                if (prevRange.Start < replaceStart)
                {
                    _log($"xparMerge_strip: ¶ at [{prevRange.Start},{prevRange.End}] hors range remplacé (start={replaceStart}), preserved");
                    return;
                }
                bool hasOMath = false;
                try { hasOMath = prevRange.OMaths != null && prevRange.OMaths.Count > 0; } catch { }
                if (hasOMath) return;
                string prevText = prevRange.Text ?? "";
                if (prevText.Replace("\r", "").Replace("\n", "").Trim().Length > 0) return;
                int delLen = prevRange.End - prevRange.Start;
                prevRange.Delete();
                newStart -= delLen;
                newEnd -= delLen;
            }
            catch (Exception ex) { _log("xparMerge_strip_lead_para_error: " + ex.Message); }
        }

        private void SyncOMathJustificationToParagraph(Word.Document doc, int pos)
        {
            try
            {
                int omathJc = MapParagraphAlignToOMathJc(ReadParagraphAlignment(doc, pos));

                // 1) Typed OMath.Justification setter (couvre les inline).
                foreach (Word.OMath om in doc.OMaths)
                {
                    var r = om.Range;
                    if (r.Start > pos || r.End <= pos) continue;
                    try { om.Justification = (Word.WdOMathJc)omathJc; } catch { }
                    break;
                }

                // 2) Patch OOXML pour OMathPara (seul path qui marche sur cette PIA Office15).
                PatchOMathParaJustificationViaXml(doc, pos, omathJc);
            }
            catch (Exception ex) { _log("align_sync_error: " + ex.Message); }
        }

        private static int ReadParagraphAlignment(Word.Document doc, int pos)
        {
            try
            {
                var format = doc.Range(pos, pos).Paragraphs[1].Format;
                return (int)format.GetType().InvokeMember(
                    "Alignment", BindingFlags.GetProperty, null, format, null);
            }
            catch { return 0; }
        }

        private static int MapParagraphAlignToOMathJc(int paragraphAlign)
        {
            switch (paragraphAlign)
            {
                case 1: return 2; // Center
                case 2: return 4; // Right
                default: return 3; // Left (couvre Left, Justify, variantes)
            }
        }

        private static string OMathJcToOoxmlVal(int jc)
        {
            switch (jc)
            {
                case 1: return "centerGroup";
                case 2: return "center";
                case 3: return "left";
                case 4: return "right";
                default: return null;
            }
        }

        private void PatchOMathParaJustificationViaXml(Word.Document doc, int pos, int omathJc)
        {
            string targetVal = OMathJcToOoxmlVal(omathJc);
            if (targetVal == null) return;
            try
            {
                var probeRange = doc.Range(pos, pos);
                var paras = probeRange.Paragraphs;
                if (paras == null || paras.Count == 0) return;
                var paraRange = paras[1].Range;
                string xml = paraRange.WordOpenXML;
                if (string.IsNullOrEmpty(xml)) return;
                bool changed;
                string patched = OMathParaJcPatcher.EnsureDisplayWithJc(xml, targetVal, out changed);
                if (!changed) return;
                // Réinsertion forcée : OMath.Justification setter ne déclenche
                // pas de re-layout. InsertXML re-process et force le repaint.
                paraRange.InsertXML(patched);
            }
            catch (Exception ex) { _log("align_sync_xml_error: " + ex.Message); }
        }
    }
}
