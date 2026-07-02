// MathCursor — capture d'intention mathématique depuis une saisie clavier linéaire.
// Copyright (C) 2026  Côme Percin
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.Detection
{
    /// <summary>
    /// Traduit une position-string (= offset dans <c>paragraph.Range.Text</c>)
    /// en position interne Word (= ce que <c>SetRange</c> attend).
    ///
    /// <para>Implémentation : itère <c>paragraph.Range.Characters</c> et
    /// accumule <c>Character.Text.Length</c>. Quand le cumul dépasse la cible,
    /// retourne la position Word interne. Cette approche gère correctement
    /// les surrogate pairs (1 entrée <c>Characters</c> = 2 chars dans
    /// <c>.Text</c>).</para>
    ///
    /// <para><b>Pourquoi pas <c>Range.MoveStart(wdCharacter, N)</c></b> :
    /// <c>wdCharacter</c> compte 1 par entrée <c>Range.Characters</c>, alors
    /// que <c>.Text.Length</c> compte 2 pour les surrogate pairs (math italic
    /// 𝑓 𝑥 etc.). Sur un paragraphe avec OMath voisine contenant des
    /// surrogates, l'écart s'accumule → bug observé 2026-05-19 sur
    /// <c>"On a f: x ↦ 2x+1 et g :x->1/(x+1)"</c> où l'OMath 1 occupe 14
    /// wdChars mais 16 chars Text → décalage de +2 en aval. La version
    /// itérative ci-dessous ne tombe pas dans ce piège.</para>
    ///
    /// <para>Pourquoi : les positions des OMath et autres structurels Word
    /// (CC wrappers, OMathPara, paragraph markers internes display math)
    /// prennent des slots internes qui n'apparaissent pas dans
    /// <c>Range.Text</c>. Le NER bosse en coords string, <c>SetRange</c> en
    /// coords internes. Sans traduction → snap arrière sur OMath voisine →
    /// bug F=1=1.</para>
    ///
    /// <para>Utilisé uniquement au commit (= action user), pas au tick
    /// polling. Coût : O(N chars du ¶) par commit — acceptable.</para>
    /// </summary>
    internal static class ParagraphPositionTranslator
    {
        /// <summary>
        /// Traduit <paramref name="stringPos"/> (offset dans
        /// <c>paragraphRange.Text</c>) en position absolue Word interne.
        /// Renvoie <c>paragraphRange.End</c> si <paramref name="stringPos"/>
        /// dépasse la fin du paragraphe. Gère les surrogate pairs (offset
        /// interne au caractère renvoyé pour les positions à l'intérieur
        /// d'un surrogate).
        /// </summary>
        public static int StringPosToInternal(Word.Range paragraphRange, int stringPos)
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
                /* fallback : interprète stringPos comme offset interne (= ancien comportement) */
                return paragraphRange.Start + stringPos;
            }

            return paragraphRange.End;
        }

        /// <summary>
        /// Backup natif : <c>Range.MoveStart(wdCharacter, N)</c>. Plus rapide
        /// (1 appel COM) mais incorrect avec surrogate pairs. Conservé pour
        /// référence et tests A/B.
        /// </summary>
        public static int StringPosToInternalNative(Word.Range paragraphRange, int stringPos)
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
                return paragraphRange.Start + stringPos;
            }
        }
    }
}
