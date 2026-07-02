// MathCursor — capture d'intention mathématique depuis une saisie clavier linéaire.
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

namespace MathCursor.UI
{
    /// <summary>
    /// Étiquette discrète affichée à droite d'un candidat de la popup quand
    /// son RENDU d'aperçu ne suffit pas à le distinguer : l'aperçu WpfMath
    /// aplatit les matrices (les `&` deviennent des espaces larges), donc
    /// « (1 2) » matrice ligne et « (1,2) » tuple se ressemblent — dans Word
    /// la matrice est pourtant une vraie grille OMML. Pure compute — compilé
    /// aussi par MathCursor.Tests. (Cadrage user 2026-06-12 : « matrice et
    /// tuple se rendent exactement pareil donc bof dans la popup ».)
    /// </summary>
    internal static class CandidateHints
    {
        /// <summary>Étiquette pour un candidat LaTeX, null si le rendu se
        /// suffit (tuples, expressions ordinaires : pas de badge).</summary>
        public static string GetHint(string latex)
        {
            if (string.IsNullOrEmpty(latex)) return null;
            bool isMatrix = latex.IndexOf("\\begin{pmatrix}", StringComparison.Ordinal) >= 0
                || latex.IndexOf("\\begin{bmatrix}", StringComparison.Ordinal) >= 0
                || latex.IndexOf("\\begin{vmatrix}", StringComparison.Ordinal) >= 0;
            if (!isMatrix) return null;

            bool hasRows = latex.IndexOf("\\\\", StringComparison.Ordinal) >= 0;
            bool hasCols = latex.IndexOf("&", StringComparison.Ordinal) >= 0;
            if (hasRows && hasCols) return "matrice";
            if (hasRows) return "colonne";
            return "matrice ligne";
        }
    }
}
