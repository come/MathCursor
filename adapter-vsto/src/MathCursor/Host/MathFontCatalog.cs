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

namespace MathCursor.Host
{
    /// <summary>
    /// Catalogue (PUR, sans Word) des polices math proposées par le sélecteur
    /// (ADR 2026-06-22-Feat-math-font-selector). Liste CURATÉE de fontes à
    /// table OpenType « MATH » : une fonte de texte ordinaire (Arial…) sans
    /// cette table casserait le rendu des équations, d'où l'absence de
    /// sélecteur libre. Testable hors Word (lié au projet de tests pure-compute).
    /// </summary>
    internal static class MathFontCatalog
    {
        /// <summary>Police math par défaut de Word (rien à « ajouter »).</summary>
        public const string Default = "Cambria Math";

        /// <summary>Fontes proposées, dans l'ordre du menu : Cambria (défaut)
        /// puis les ajouts demandés — Latin Modern (look LaTeX) + STIX Two.</summary>
        public static readonly string[] Fonts =
        {
            "Cambria Math",
            "Latin Modern Math",
            "STIX Two Math",
        };

        /// <summary>Vrai si <paramref name="font"/> est le défaut Word (ou vide).</summary>
        public static bool IsDefault(string font)
            => string.IsNullOrEmpty(font)
               || string.Equals(font, Default, StringComparison.OrdinalIgnoreCase);

        /// <summary>Index de la police dans <see cref="Fonts"/> ; 0 (défaut) si
        /// inconnue ou vide.</summary>
        public static int IndexOf(string font)
        {
            if (string.IsNullOrEmpty(font)) return 0;
            for (int i = 0; i < Fonts.Length; i++)
                if (string.Equals(Fonts[i], font, StringComparison.OrdinalIgnoreCase))
                    return i;
            return 0;
        }

        /// <summary>Page de téléchargement officielle (gratuite) d'une fonte à
        /// installer ; <c>null</c> pour le défaut Cambria (déjà fourni par Word)
        /// ou une fonte inconnue. Pages officielles (pas de lien direct .otf, qui
        /// pourrirait) — GUST e-foundry pour Latin Modern, stixfonts.org pour STIX.</summary>
        public static string DownloadUrl(string font)
        {
            if (string.IsNullOrEmpty(font)) return null;
            if (string.Equals(font, "Latin Modern Math", StringComparison.OrdinalIgnoreCase))
                return "https://www.gust.org.pl/projects/e-foundry/lm-math";
            if (string.Equals(font, "STIX Two Math", StringComparison.OrdinalIgnoreCase))
                return "https://www.stixfonts.org/";
            return null;
        }
    }
}
