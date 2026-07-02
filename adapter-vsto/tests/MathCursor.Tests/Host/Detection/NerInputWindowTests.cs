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

using System.Collections.Generic;
using MathCursor.Host.Detection;
using Xunit;

namespace MathCursor.Tests.Host.Detection
{
    /// <summary>
    /// Tests <see cref="NerInputWindow.Compute"/> — fenêtre NER autour du caret
    /// bornée par les OMaths voisines (ou les bornes du paragraphe).
    ///
    /// <para>Convention : dans les chaînes des tests, on utilise des espaces
    /// pour représenter les zones où Word aurait masqué une OMath (= ce qui
    /// arrive à <c>NerInputWindow.Compute</c> en runtime via
    /// <c>WordContextReader</c>). Les positions des régions OMath sont en
    /// coordonnées string (post-masking).</para>
    /// </summary>
    public sealed class NerInputWindowTests
    {
        private static IReadOnlyList<(int, int)> Omaths(params (int, int)[] r) => r;

        [Fact(DisplayName = "Sans OMath → toute la chaîne")]
        public void No_omath_returns_full_paragraph()
        {
            var w = NerInputWindow.Compute("hello world", Omaths(), caretInParagraph: 5);
            Assert.Equal(0, w.LeftCut);
            Assert.Equal(11, w.RightCut);
            Assert.Equal("hello world", w.Input);
        }

        [Fact(DisplayName = "Sans OMath, caret au début")]
        public void No_omath_caret_at_start()
        {
            var w = NerInputWindow.Compute("abc", Omaths(), caretInParagraph: 0);
            Assert.Equal(0, w.LeftCut);
            Assert.Equal(3, w.RightCut);
            Assert.Equal("abc", w.Input);
        }

        [Fact(DisplayName = "Sans OMath, caret à la fin")]
        public void No_omath_caret_at_end()
        {
            var w = NerInputWindow.Compute("abc", Omaths(), caretInParagraph: 3);
            Assert.Equal(0, w.LeftCut);
            Assert.Equal(3, w.RightCut);
            Assert.Equal("abc", w.Input);
        }

        [Fact(DisplayName = "OMath à gauche du caret → leftCut à sa fin")]
        public void Omath_left_of_caret_sets_leftcut()
        {
            // "    =1" : OMath masquée [0,4), texte user [4,6)
            var w = NerInputWindow.Compute("    =1", Omaths((0, 4)), caretInParagraph: 6);
            Assert.Equal(4, w.LeftCut);
            Assert.Equal(6, w.RightCut);
            Assert.Equal("=1", w.Input);
        }

        [Fact(DisplayName = "OMath à droite du caret → rightCut à son début")]
        public void Omath_right_of_caret_sets_rightcut()
        {
            // "abc    " : texte user [0,3), OMath masquée [3,7)
            var w = NerInputWindow.Compute("abc    ", Omaths((3, 7)), caretInParagraph: 1);
            Assert.Equal(0, w.LeftCut);
            Assert.Equal(3, w.RightCut);
            Assert.Equal("abc", w.Input);
        }

        [Fact(DisplayName = "Deux OMaths encadrant le caret → window restreinte")]
        public void Two_omaths_around_caret_clips_both_sides()
        {
            // "    =1 g(y)" simplifié en "    =1    " (2 OMaths masquées)
            // OMath A [0,4), texte " =1 " [4,8), OMath B [8,12)
            var w = NerInputWindow.Compute("     =1     ", Omaths((0, 4), (8, 12)), caretInParagraph: 6);
            Assert.Equal(4, w.LeftCut);
            Assert.Equal(8, w.RightCut);
            Assert.Equal(" =1 ", w.Input);
        }

        [Fact(DisplayName = "Caret juste APRÈS l'OMath gauche → leftCut = fin OMath")]
        public void Caret_at_omath_left_end()
        {
            // OMath [0,4), caret pile à 4 (juste après)
            var w = NerInputWindow.Compute("    abc", Omaths((0, 4)), caretInParagraph: 4);
            Assert.Equal(4, w.LeftCut);
            Assert.Equal(7, w.RightCut);
            Assert.Equal("abc", w.Input);
        }

        [Fact(DisplayName = "Caret juste AVANT l'OMath droite → rightCut = début OMath")]
        public void Caret_at_omath_right_start()
        {
            // OMath [3,7), caret pile à 3 (juste avant)
            var w = NerInputWindow.Compute("abc    ", Omaths((3, 7)), caretInParagraph: 3);
            Assert.Equal(0, w.LeftCut);
            Assert.Equal(3, w.RightCut);
            Assert.Equal("abc", w.Input);
        }

        [Fact(DisplayName = "Caret DANS une OMath → window dégenérée mais safe")]
        public void Caret_inside_omath_yields_safe_window()
        {
            // OMath [0,6) avec caret = 3 (dedans).
            // Aucune OMath ne satisfait `End <= caret` ni `Start >= caret`
            // → leftCut=0, rightCut=paraLen. Returns whole para (sain).
            var w = NerInputWindow.Compute("      tail", Omaths((0, 6)), caretInParagraph: 3);
            Assert.Equal(0, w.LeftCut);
            Assert.Equal(10, w.RightCut);
            Assert.Equal("      tail", w.Input);
        }

        [Fact(DisplayName = "OMaths multiples à gauche → leftCut = la plus à droite")]
        public void Multiple_left_omaths_picks_rightmost_end()
        {
            // Trois OMaths à gauche du caret : [0,2), [3,5), [6,8)
            // Caret à 10, leftCut doit être 8 (= max des End)
            var w = NerInputWindow.Compute("        ab", Omaths((0, 2), (3, 5), (6, 8)), caretInParagraph: 10);
            Assert.Equal(8, w.LeftCut);
            Assert.Equal(10, w.RightCut);
            Assert.Equal("ab", w.Input);
        }

        [Fact(DisplayName = "OMaths multiples à droite → rightCut = la plus à gauche")]
        public void Multiple_right_omaths_picks_leftmost_start()
        {
            // Trois OMaths à droite du caret : [3,5), [6,8), [9,11)
            // Caret à 1, rightCut doit être 3 (= min des Start)
            var w = NerInputWindow.Compute("ab          ", Omaths((3, 5), (6, 8), (9, 11)), caretInParagraph: 1);
            Assert.Equal(0, w.LeftCut);
            Assert.Equal(3, w.RightCut);
            Assert.Equal("ab ", w.Input);
        }

        [Fact(DisplayName = "Cas user 2026-05-18 : f(x) + ' =1' → NER reçoit ' =1'")]
        public void Real_user_scenario_after_fx_typed_equals_one()
        {
            // OMath f(x) masquée à [0,4) (= "F(x)" = 4 chars visibles).
            // User a tapé " =1" puis le \r de fin de ¶.
            // Paragraph string : "     =1\r" (4 spaces + " =1\r") len 8
            var paragraphText = "     =1\r";
            var caretAtParaMark = 7;
            var w = NerInputWindow.Compute(paragraphText, Omaths((0, 4)), caretAtParaMark);
            Assert.Equal(4, w.LeftCut);
            Assert.Equal(8, w.RightCut);
            Assert.Equal(" =1\r", w.Input);
        }

        [Fact(DisplayName = "Paragraphe vide → window vide")]
        public void Empty_paragraph_returns_empty_window()
        {
            var w = NerInputWindow.Compute("", Omaths(), caretInParagraph: 0);
            Assert.Equal(0, w.LeftCut);
            Assert.Equal(0, w.RightCut);
            Assert.Equal("", w.Input);
        }

        [Fact(DisplayName = "Null paragraphText → window vide (no crash)")]
        public void Null_paragraph_text_returns_empty()
        {
            var w = NerInputWindow.Compute(null, Omaths(), caretInParagraph: 0);
            Assert.Equal(0, w.LeftCut);
            Assert.Equal(0, w.RightCut);
            Assert.Equal("", w.Input);
        }

        [Fact(DisplayName = "Null omathRegions → traité comme aucune OMath")]
        public void Null_omath_regions_treated_as_none()
        {
            var w = NerInputWindow.Compute("hello", null, caretInParagraph: 2);
            Assert.Equal(0, w.LeftCut);
            Assert.Equal(5, w.RightCut);
            Assert.Equal("hello", w.Input);
        }
    }
}
