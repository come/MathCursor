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
