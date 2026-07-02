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

using System;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host
{
    /// <summary>
    /// Application d'une police math (ADR 2026-06-22-Feat-math-font-selector).
    /// Pose <c>Range.Font.Name</c> : pour une fonte à table OpenType « MATH »
    /// sélectionnée sur une zone math, Word re-typographie l'équation (= ce
    /// qu'on ferait à la main via la boîte police, équation sélectionnée).
    ///
    /// <para>100 % cosmétique : chaque sous-étape est en try/catch, on n'échoue
    /// jamais une insertion ni l'action ruban pour un réglage de fonte. Une
    /// fonte non installée → Word retombe silencieusement sur Cambria.</para>
    /// </summary>
    internal static class MathFontApplier
    {
        /// <summary>La fonte est-elle installée sur ce poste (<c>FontNames</c>) ?</summary>
        public static bool IsInstalled(Word.Application app, string font)
        {
            if (app == null || string.IsNullOrEmpty(font)) return false;
            try
            {
                var names = app.FontNames;
                if (names == null) return false;
                for (int i = 1; i <= names.Count; i++)
                    if (string.Equals(names[i], font, StringComparison.OrdinalIgnoreCase))
                        return true;
                return false;
            }
            catch { return false; }
        }

        /// <summary>Pose la police sur une range (une équation). No-op si vide.</summary>
        public static void ApplyToRange(Word.Range range, string font)
        {
            if (range == null || string.IsNullOrEmpty(font)) return;
            try { range.Font.Name = font; } catch { }
        }

        /// <summary>Applique la police à TOUTES les équations du document
        /// (portée « doc entier »). Retourne le nombre d'équations touchées.</summary>
        public static int ApplyToDocument(Word.Document doc, string font, Action<string> log = null)
        {
            if (doc == null || string.IsNullOrEmpty(font)) return 0;
            int count = 0;
            try
            {
                foreach (Word.OMath om in doc.OMaths)
                {
                    try { om.Range.Font.Name = font; count++; }
                    catch (Exception ex) { log?.Invoke("mathfont_one_error: " + ex.Message); }
                }
            }
            catch (Exception ex) { log?.Invoke("mathfont_doc_error: " + ex.Message); }
            log?.Invoke($"mathfont_apply: \"{font}\" -> {count} equation(s)");
            return count;
        }
    }
}
