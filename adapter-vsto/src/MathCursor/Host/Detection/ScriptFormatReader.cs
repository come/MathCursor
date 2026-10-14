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

using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.Detection
{
    /// <summary>
    /// Lit la mise en forme Indice/Exposant sur la plage d'une zone (ADR
    /// 2026-10-14-Feat-word-subscript-superscript-format). Lecture seule.
    /// Une zone ne contient jamais d'OMath → 1 caractère Word = 1 caractère
    /// de <see cref="ZoneSpan.Text"/> (vérifié, sinon repli).
    /// POC Word : fast path ~1 ms, ~0,25 ms/caractère quand la zone est mixte.
    /// </summary>
    internal static class ScriptFormatReader
    {
        private const int MaxChars = 200; // garde-fou perf (zone anormalement longue)

        /// <summary>Un <see cref="ScriptKind"/> par caractère de la zone, ou
        /// null si aucune mise en forme (cas courant) ou lecture impossible.</summary>
        public static ScriptKind[] TryRead(Word.Document doc, ZoneSpan zone)
        {
            if (doc == null || zone == null || zone.IsEmpty) return null;
            try
            {
                if (!zone.TryToInternal(doc, out int absStart, out int absEnd)) return null;
                var range = doc.Range(absStart, absEnd);
                // Fast path : 0 = aucun caractère concerné (-1 = tous, 9999999 = mixte).
                if (range.Font.Subscript == 0 && range.Font.Superscript == 0) return null;

                string text = zone.Text;
                var chars = range.Characters;
                int count = chars.Count;
                if (count != text.Length || count > MaxChars) return null;

                var kinds = new ScriptKind[count];
                bool any = false;
                for (int i = 1; i <= count; i++)
                {
                    var c = chars[i];
                    var font = c.Font;
                    if (font.Subscript == -1) { kinds[i - 1] = ScriptKind.Sub; any = true; }
                    else if (font.Superscript == -1) { kinds[i - 1] = ScriptKind.Sup; any = true; }
                }
                return any ? kinds : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
