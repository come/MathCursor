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
using System.Text;
using MathCursor.Engine;

namespace MathCursor.Host.Detection
{
    /// <summary>Mise en forme d'un caractère : normal, indice ou exposant.</summary>
    internal enum ScriptKind { None, Sub, Sup }

    /// <summary>
    /// Traduit la mise en forme Indice/Exposant de Word en marqueurs moteur
    /// (<see cref="ScriptMarkers"/>, ADR 2026-10-14-Feat-word-subscript-
    /// superscript-format). Pur : texte + un <see cref="ScriptKind"/> par
    /// caractère → texte moteur. Les marqueurs n'existent que dans le texte
    /// envoyé au moteur, jamais dans les positions Word.
    /// </summary>
    internal static class ScriptMarkup
    {
        /// <summary>Entoure chaque run indice/exposant de ses marqueurs.
        /// <paramref name="kinds"/> null, ou de longueur différente du texte →
        /// texte inchangé (repli sûr).</summary>
        public static string Apply(string text, IReadOnlyList<ScriptKind> kinds)
        {
            if (string.IsNullOrEmpty(text) || kinds == null || kinds.Count != text.Length)
                return text ?? "";
            kinds = WithoutOrdinals(text, kinds);
            var sb = new StringBuilder(text.Length + 8);
            var current = ScriptKind.None;
            for (int i = 0; i < text.Length; i++)
            {
                if (kinds[i] != current)
                {
                    Close(sb, current);
                    Open(sb, kinds[i]);
                    current = kinds[i];
                }
                sb.Append(text[i]);
            }
            Close(sb, current);
            return sb.ToString();
        }

        // Suffixes d'ordinal que la correction automatique de Word met en exposant
        // (2ᵉ, 1ᵉʳ, 2ⁿᵈ, 3ʳᵈ) : du texte, pas un exposant mathématique (ADR
        // 2026-10-16-Fix-word-ordinal-superscript-ignored).
        private static readonly HashSet<string> OrdinalSuffixes = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "e", "er", "re", "ère", "ème", "eme", "es", "ers", "res", "èmes",
            "nd", "nde", "nds", "ndes", "d", "de", "st", "rd", "th",
        };

        // Run en exposant = suffixe d'ordinal collé derrière un chiffre et suivi
        // d'une fin de mot → lu comme texte normal.
        private static IReadOnlyList<ScriptKind> WithoutOrdinals(string text, IReadOnlyList<ScriptKind> kinds)
        {
            List<ScriptKind> result = null;
            for (int i = 0; i < text.Length; i++)
            {
                if (kinds[i] != ScriptKind.Sup || (i > 0 && kinds[i - 1] == ScriptKind.Sup)) continue;
                int end = i;
                while (end < text.Length && kinds[end] == ScriptKind.Sup) end++;
                bool afterDigit = i > 0 && char.IsDigit(text[i - 1]);
                bool wordEnd = end == text.Length || !char.IsLetter(text[end]);
                if (afterDigit && wordEnd && OrdinalSuffixes.Contains(text.Substring(i, end - i)))
                {
                    if (result == null) result = new List<ScriptKind>(kinds);
                    for (int k = i; k < end; k++) result[k] = ScriptKind.None;
                }
            }
            return result ?? kinds;
        }

        private static void Open(StringBuilder sb, ScriptKind k)
        {
            if (k == ScriptKind.Sub) sb.Append(ScriptMarkers.SubOpen);
            else if (k == ScriptKind.Sup) sb.Append(ScriptMarkers.SupOpen);
        }

        private static void Close(StringBuilder sb, ScriptKind k)
        {
            if (k == ScriptKind.Sub) sb.Append(ScriptMarkers.SubClose);
            else if (k == ScriptKind.Sup) sb.Append(ScriptMarkers.SupClose);
        }
    }
}
