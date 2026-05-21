using System.Collections.Generic;

namespace MathCursor.Core.Patterns
{
    /// <summary>
    /// Contrat d'un pattern structuré compositionnel — forme idiomatique avec
    /// slots, opérant en plusieurs passes au fur et à mesure que l'utilisateur
    /// tape. Distinct de <c>MathCursor.Core.Lattice.Ambiguity.IAmbiguityScanner</c>
    /// (ambig fermées AB/tight-chain/decimal) :
    /// <list type="bullet">
    ///   <item><b>Ambig closed</b> : choix entre N rendus d'une même source figée</item>
    ///   <item><b>Pattern template</b> : forme à slots, sous-patterns isolables,
    ///   slots optionnels, désambig caret-aware</item>
    /// </list>
    ///
    /// <para>Cycle d'utilisation par le <see cref="PatternPipeline"/> :</para>
    /// <list type="number">
    ///   <item><see cref="TryMatchHead"/> : détecte le déclencheur dans la source
    ///   (ex. <c>V</c> pour <c>forall-belongs</c>, <c>Lim</c> pour <c>lim-tends-to</c>).
    ///   Retourne un <see cref="PatternMatch"/> partiel ou <c>null</c>.</item>
    ///   <item><see cref="Expand"/> : étend l'état actuel en consommant les
    ///   tokens suivants. Retourne les complétions proposables à partir d'ici.</item>
    /// </list>
    ///
    /// <para>Implémentations stateless : aucun état entre appels, le pipeline
    /// est responsable de la composition.</para>
    ///
    /// <para>Cf. ADR <c>2026-05-21-Meta-pattern-templates-vs-ambig-closed</c>,
    /// étape P2.</para>
    /// </summary>
    public interface IPatternTemplate
    {
        /// <summary>Identifiant stable du template (ex. <c>"forall-belongs"</c>,
        /// <c>"ensemble"</c>, <c>"interval-union"</c>). Utilisé pour la
        /// composition via <see cref="PatternRefSlot"/> et le lookup dans
        /// <see cref="PatternRegistry"/>.</summary>
        string TemplateId { get; }

        /// <summary>Ordre dans la pipeline. Plus petit = plus tôt. Permet
        /// d'établir des priorités entre templates qui pourraient matcher le
        /// même head (rare).</summary>
        int Order { get; }

        /// <summary>Tente de détecter le head de ce pattern dans la source.
        /// Retourne un <see cref="PatternMatch"/> avec <see cref="PatternMatch.SourceStart"/>
        /// et un <see cref="PatternMatch.SourceEnd"/> bornant la zone consommée
        /// par le head seul, et un <see cref="PatternMatch.Slots"/> initial
        /// (tous slots à <see cref="EmptySlot.Instance"/> typiquement).
        /// Retourne <c>null</c> si aucun head n'est détecté.</summary>
        PatternMatch? TryMatchHead(PatternScanContext ctx);

        /// <summary>Étend un <see cref="PatternMatch"/> en cours en consommant
        /// les tokens suivant <c>state.SourceEnd</c> dans la source. Retourne
        /// la liste des complétions proposables à partir de l'état résultant
        /// (peut inclure des formes partielles avec slots vides matérialisés
        /// par des carrés dans <see cref="PatternCompletion.HintLatex"/>).</summary>
        IReadOnlyList<PatternCompletion> Expand(PatternMatch state, PatternScanContext ctx);
    }
}
