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
