using System;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.Detection
{
    /// <summary>
    /// Traduit une position-string (= offset dans <c>paragraph.Range.Text</c>)
    /// en position interne Word (= ce que <c>SetRange</c> attend).
    ///
    /// <para>Implémentation : délègue à Word via
    /// <c>Range.MoveStart(wdCharacter, N)</c>. Validé en A/B contre l'itération
    /// <c>Range.Characters</c> sur le cas réel <c>F(x) + =1</c> → MATCH.</para>
    ///
    /// <para>Pourquoi : les positions des OMath et autres structurels Word
    /// (CC wrappers, OMathPara, paragraph markers internes display math)
    /// prennent des slots internes qui n'apparaissent pas dans
    /// <c>Range.Text</c>. Le NER bosse en coords string, <c>SetRange</c> en
    /// coords internes. Sans traduction → snap arrière sur OMath voisine →
    /// bug F=1=1.</para>
    ///
    /// <para>Utilisé uniquement au commit (= action user), pas au tick
    /// polling.</para>
    /// </summary>
    internal static class ParagraphPositionTranslator
    {
        /// <summary>
        /// Traduit <paramref name="stringPos"/> (offset dans
        /// <c>paragraphRange.Text</c>) en position absolue Word interne.
        /// Renvoie <c>paragraphRange.End</c> si <paramref name="stringPos"/>
        /// dépasse la fin du paragraphe.
        /// </summary>
        public static int StringPosToInternal(Word.Range paragraphRange, int stringPos)
        {
            if (paragraphRange == null) return 0;
            if (stringPos <= 0) return paragraphRange.Start;

            try
            {
                var probe = paragraphRange.Duplicate;
                probe.Collapse(Word.WdCollapseDirection.wdCollapseStart);
                probe.MoveStart(Word.WdUnits.wdCharacter, stringPos);
                return probe.Start;
            }
            catch
            {
                /* fallback : interprète stringPos comme offset interne (= ancien comportement) */
                return paragraphRange.Start + stringPos;
            }
        }

        /// <summary>
        /// Backup : itération manuelle de <c>paragraphRange.Characters</c>.
        /// Conservé comme référence et plan B si un jour <c>MoveStart</c>
        /// se comporte différemment (ex. version Word qui skip les invisibles).
        /// Coût O(N chars du ¶) au lieu d'un appel natif.
        /// </summary>
        public static int StringPosToInternalIterative(Word.Range paragraphRange, int stringPos)
        {
            if (paragraphRange == null) return 0;
            if (stringPos <= 0) return paragraphRange.Start;

            int cumulative = 0;
            try
            {
                foreach (Word.Range c in paragraphRange.Characters)
                {
                    int charLen = (c.Text ?? "").Length;
                    if (cumulative + charLen > stringPos)
                    {
                        int offsetWithinChar = stringPos - cumulative;
                        return c.Start + offsetWithinChar;
                    }
                    cumulative += charLen;
                }
            }
            catch
            {
                return paragraphRange.Start + stringPos;
            }

            return paragraphRange.End;
        }
    }
}
