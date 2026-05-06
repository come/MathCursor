using System.Collections.Generic;
using MathCursor.Core.Resolution;

namespace MathCursor.Host
{
    /// <summary>
    /// Logique pure de calcul du sidecar fusionné pour l'intra-merge
    /// (OMaths adjacents même paragraphe absorbés par <c>TryMergeWithAdjacentOMaths</c>).
    /// Extraite de SuggestionService pour être testable sans Word.
    /// <para>
    /// La fusion suit la même règle que la concaténation de la mergedSource :
    /// <c>leftSource + ' ' + middleSource + ' ' + rightSource</c> (séparateur
    /// espace, contrairement au cross-merge qui utilise <c>\n</c>).
    /// </para>
    /// <para>
    /// Bug 06-05 (intra-merge) : avant ce builder, <c>TryMergeWithAdjacentOMaths</c>
    /// retournait un MergeResult avec MergedSidecar = Empty → toutes les
    /// désambiguïsations vec/forall/etc. des OMaths absorbés étaient perdues
    /// au reranking (cf. ADR 06-05 et test xUnit
    /// <c>SidecarMergerTests.IntraMerge_same_line_recalibrates_with_space_separator</c>).
    /// </para>
    /// </summary>
    internal static class IntraMergeSidecarBuilder
    {
        /// <summary>
        /// Calcule le sidecar fusionné pour la mergedSource construite par
        /// <c>TryMergeWithAdjacentOMaths</c>. Tout argument null ou vide est
        /// silencieusement traité comme inexistant (cas dégradé OMath sans
        /// sidecar mémorisé).
        /// </summary>
        /// <param name="leftSource">Source de l'OMath gauche absorbé (null si pas de gauche).</param>
        /// <param name="leftSidecar">Sidecar du gauche (null tolérant).</param>
        /// <param name="middleSource">Source de l'OMath en cours de commit (le « middle »).</param>
        /// <param name="middleSidecar">Sidecar de la popup courante (jamais null en pratique mais toléré).</param>
        /// <param name="rightSource">Source de l'OMath droit absorbé (null si pas de droite).</param>
        /// <param name="rightSidecar">Sidecar du droit (null tolérant).</param>
        public static ResolutionSidecar Build(
            string leftSource, ResolutionSidecar leftSidecar,
            string middleSource, ResolutionSidecar middleSidecar,
            string rightSource, ResolutionSidecar rightSidecar)
        {
            var parts = new List<ResolutionSidecar>();
            var shifts = new List<int>();
            int cumShift = 0;

            if (leftSource != null)
            {
                parts.Add(leftSidecar ?? ResolutionSidecar.Empty);
                shifts.Add(cumShift);
                cumShift += leftSource.Length + 1; // +1 pour l'espace de jointure
            }

            parts.Add(middleSidecar ?? ResolutionSidecar.Empty);
            shifts.Add(cumShift);
            cumShift += (middleSource?.Length ?? 0) + 1;

            if (rightSource != null)
            {
                parts.Add(rightSidecar ?? ResolutionSidecar.Empty);
                shifts.Add(cumShift);
            }

            return SidecarMerger.Merge(parts, shifts);
        }
    }
}
