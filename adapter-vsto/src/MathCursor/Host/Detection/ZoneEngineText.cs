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

namespace MathCursor.Host.Detection
{
    /// <summary>
    /// Texte de zone destiné au MOTEUR. Les bornes de zone sont trimées
    /// (l'espace tapé reste dans le doc après le commit, l'ancre popup ne
    /// bouge pas), mais le lexer distingue <c>R*␣</c> (étoile postfixe
    /// détachée → ℝ^*) de <c>R*</c> en fin d'entrée (multiplication
    /// incomplète → R×□) : l'espace EST le signal, il faut le restituer
    /// (ADR 2026-06-10-Feat-culture-scoped-aliases, régression « R*␣ »).
    ///
    /// Pure compute (pas d'interop Word) : compilé aussi par MathCursor.Tests.
    /// </summary>
    internal static class ZoneEngineText
    {
        /// <summary>Ajoute UN espace à <paramref name="zoneText"/> si le ¶ en
        /// contient un juste après <paramref name="stringEnd"/> (le lexer n'a
        /// besoin que du signal « détaché », pas du run entier).</summary>
        public static string WithTrailingSpaceSignal(string zoneText, string paragraphText, int stringEnd)
            => stringEnd < paragraphText.Length && char.IsWhiteSpace(paragraphText[stringEnd])
                ? zoneText + " "
                : zoneText;
    }
}
