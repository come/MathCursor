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

namespace MathCursor.Engine;

/// <summary>
/// Contrat d'entrée « indice / exposant exacts » (ADR 2026-10-14-Feat-word-
/// subscript-superscript-format) : un hôte qui CONNAÎT la mise en forme
/// (bouton Indice/Exposant de Word) entoure le groupe de ces marqueurs
/// d'usage privé. Le lexer l'analyse à part et l'attache par un _ / ^ sticky
/// → lecture unique, sans re-deviner l'étendue. Miroir Rust :
/// <c>rust/mc-engine/src/lexer.rs</c> (SUB_OPEN…SUP_CLOSE).
/// </summary>
public static class ScriptMarkers
{
    public const char SubOpen = '';
    public const char SubClose = '';
    public const char SupOpen = '';
    public const char SupClose = '';

    public static bool IsMarker(char c) => c >= SubOpen && c <= SupClose;
}
