using System;
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
        private readonly Action<string> _log;

        public PostCommitLayoutFinalizer(
            Word.Application app,
            Action<string> log)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _log = log ?? (s => { });
        }

        /// <summary>
        /// Pipeline de finalisation post-cross-merge :
        /// (1) strip ¶ vide résiduel à l'amont du nouvel OMath,
        /// (2) append ¶ vide d'atterrissage si l'OMath est dernier ¶,
        /// (3) caret au début du ¶ suivant.
        ///
        /// <para>L'alignment OMath ↔ ¶ n'est plus appelé ici : il est posé
        /// uniformément post-insert par <c>SuggestionService.InsertOMathAt</c>
        /// pour tous les chemins (fast_path/splice/atomic). Cf. ADR
        /// <c>2026-05-13-Fix-omath-alignment-uniform-post-insert</c>.</para>
        /// </summary>
        public void FinalizeCrossMerge(Word.Document doc, int replaceStart,
            ref int newStart, ref int newEnd, out bool didCreateAnchorPara)
        {
            didCreateAnchorPara = false;
            try
            {
                StripLeadingResidualEmptyParagraph(doc, replaceStart, ref newStart, ref newEnd);
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
    }
}
