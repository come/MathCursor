using System.Collections.Generic;

namespace MathCursor.Host.Blocks
{
    /// <summary>
    /// Table des MARQUEURS de tête de ligne d'une chaîne de raisonnement
    /// (ADR 2026-06-10-Feat-multiline-chain-eqarr-architecture, P1) : c'est
    /// CE module qui possède les connecteurs logiques (`&lt;=&gt;`, `=&gt;`…)
    /// volontairement retirés du moteur forest, et le LaTeX d'affichage de
    /// chaque marqueur. Pure compute — compilé aussi par MathCursor.Tests.
    /// </summary>
    internal static class RelationMarkers
    {
        /// <summary>(forme tapée, LaTeX). Ordre = longueur décroissante
        /// (plus-long-match : « &lt;=&gt; » avant « &lt;= » avant « = »).</summary>
        public static readonly IReadOnlyList<(string Typed, string Latex)> Table =
            new (string, string)[]
            {
                ("<=>", "\\Leftrightarrow "),
                ("=>",  "\\Rightarrow "),
                ("<=",  "\\leq "),
                (">=",  "\\geq "),
                ("!=",  "\\neq "),
                ("⟺",   "\\Leftrightarrow "),
                ("⇔",   "\\Leftrightarrow "),
                ("⟹",   "\\Rightarrow "),
                ("⇒",   "\\Rightarrow "),
                ("≤",   "\\leq "),
                ("≥",   "\\geq "),
                ("≠",   "\\neq "),
                ("=",   "="),
                ("<",   "<"),
                (">",   ">"),
            };

        /// <summary>Plus-long-match d'un marqueur à <paramref name="start"/>.
        /// Null si aucun.</summary>
        public static (string Typed, string Latex)? TryMatch(string text, int start)
        {
            if (string.IsNullOrEmpty(text) || start < 0 || start >= text.Length) return null;
            foreach (var (typed, latex) in Table)
            {
                if (start + typed.Length > text.Length) continue;
                if (string.CompareOrdinal(text, start, typed, 0, typed.Length) == 0)
                    return (typed, latex);
            }
            return null;
        }
    }
}
