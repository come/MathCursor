// MathCursor: capturing mathematical intent from linear keyboard input.
// Copyright (C) 2026  Côme de Percin
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

using System.Collections.Generic;

namespace MathCursor.Host.Detection
{
    /// <summary>
    /// Fenêtre de texte envoyée au NER : extrait du paragraphe entre les
    /// OMaths les plus proches autour du caret.
    ///
    /// <para>Règle :</para>
    /// <list type="bullet">
    /// <item><c>LeftCut</c>  = max(OMath.End)  où OMath.End  ≤ caret. 0 si aucune.</item>
    /// <item><c>RightCut</c> = min(OMath.Start) où OMath.Start ≥ caret. <c>paragraphText.Length</c> si aucune.</item>
    /// <item><c>Input</c>    = <c>paragraphText[LeftCut..RightCut)</c></item>
    /// </list>
    ///
    /// <para>= « texte entre l'OMath voisine de gauche (ou début ¶) et l'OMath
    /// voisine de droite (ou fin ¶) ». Évite que le NER drag du contexte
    /// math résiduel dans une zone de texte adjacente.</para>
    /// </summary>
    internal readonly struct NerInputWindow
    {
        public int LeftCut { get; }
        public int RightCut { get; }
        public string Input { get; }

        public NerInputWindow(int leftCut, int rightCut, string input)
        {
            LeftCut = leftCut;
            RightCut = rightCut;
            Input = input;
        }

        /// <summary>
        /// Calcule la fenêtre NER autour du caret.
        /// </summary>
        /// <param name="paragraphText">Texte du ¶ courant, OMaths masquées
        /// par des espaces (pour préserver les positions).</param>
        /// <param name="omathRegions">Régions OMath dans le string (start, end).</param>
        /// <param name="caretInParagraph">Position du caret dans le string ¶.</param>
        public static NerInputWindow Compute(
            string paragraphText,
            IReadOnlyList<(int start, int end)> omathRegions,
            int caretInParagraph)
        {
            if (paragraphText == null) return new NerInputWindow(0, 0, string.Empty);

            int leftCut = 0;
            int rightCut = paragraphText.Length;

            if (omathRegions != null)
            {
                foreach (var (s, e) in omathRegions)
                {
                    if (e <= caretInParagraph && e > leftCut) leftCut = e;
                    if (s >= caretInParagraph && s < rightCut) rightCut = s;
                }
            }
            if (rightCut < leftCut) rightCut = leftCut;

            string input = paragraphText.Substring(leftCut, rightCut - leftCut);
            return new NerInputWindow(leftCut, rightCut, input);
        }
    }
}
