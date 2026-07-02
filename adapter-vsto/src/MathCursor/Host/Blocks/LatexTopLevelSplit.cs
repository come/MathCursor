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
using System.Collections.Generic;

namespace MathCursor.Host.Blocks
{
    /// <summary>
    /// Scinde un LaTeX au PREMIER signe de relation top-level (profondeur 0
    /// hors accolades/parenthèses/crochets) — c'est le point d'alignement du
    /// bloc eqArr (« f(x)=2x » → « f(x) » | « =2x »). Le moteur forest garantit
    /// les relations au sommet, donc un scan plat suffit ; une relation
    /// parenthésée par l'utilisateur (« (a=b) ») n'est volontairement PAS un
    /// point d'alignement. Pure compute — compilé aussi par MathCursor.Tests.
    /// </summary>
    internal static class LatexTopLevelSplit
    {
        // Commandes LaTeX « relation » émises par le moteur au top-level.
        private static readonly HashSet<string> RelationCommands = new HashSet<string>(StringComparer.Ordinal)
        {
            "leq", "geq", "neq", "sim", "equiv", "cong", "approx", "propto",
            "in", "notin", "subset", "subseteq", "supset", "supseteq",
            "to", "mapsto", "colon", "mid", "perp",
        };

        /// <summary>
        /// (lhs, relRhs) où relRhs COMMENCE au signe. relRhs null = aucun
        /// signe top-level (expression sans relation).
        /// </summary>
        public static (string Lhs, string RelRhs) Split(string latex)
        {
            if (string.IsNullOrEmpty(latex)) return (latex ?? "", null);

            int depth = 0;
            for (int i = 0; i < latex.Length; i++)
            {
                char c = latex[i];
                if (c == '\\')
                {
                    // Commande : lit le nom (lettres) ou l'échappement 1-char
                    // (\{ \} \, …) — ne touche pas à la profondeur.
                    int j = i + 1;
                    while (j < latex.Length && char.IsLetter(latex[j])) j++;
                    if (j == i + 1) { i = j; continue; } // \{ \} \, : skip les 2 chars
                    string cmd = latex.Substring(i + 1, j - (i + 1));
                    if (depth == 0 && RelationCommands.Contains(cmd))
                        return (latex.Substring(0, i), latex.Substring(i));
                    i = j - 1;
                    continue;
                }
                if (c == '{' || c == '(' || c == '[') { depth++; continue; }
                if (c == '}' || c == ')' || c == ']') { depth--; continue; }
                if (depth == 0 && (c == '=' || c == '<' || c == '>'))
                    return (latex.Substring(0, i), latex.Substring(i));
            }
            return (latex, null);
        }
    }
}
