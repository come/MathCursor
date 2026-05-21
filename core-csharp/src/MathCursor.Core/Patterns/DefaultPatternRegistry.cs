using MathCursor.Core.Patterns.Templates;

namespace MathCursor.Core.Patterns
{
    /// <summary>
    /// Factory pour le <see cref="PatternRegistry"/> par défaut du Core :
    /// expose les 3 templates pilote (<c>forall-belongs</c>, <c>ensemble</c>,
    /// <c>interval-union</c>) sous forme de registry pré-configurée.
    ///
    /// <para>Source unique de vérité pour "la liste des templates pilote".
    /// Consumer (adapter VSTO `SuggestionService`, tests d'intégration P7+)
    /// appelle <see cref="Build"/> et injecte le résultat au
    /// <see cref="ZoneResolver"/>.</para>
    ///
    /// <para>Migration P9+ vers YAML : remplacer le contenu de <see cref="Build"/>
    /// par un loader YAML (ex. <c>groups/quantifier.yaml</c> + <c>groups/belonging.yaml</c>
    /// + <c>templates/*.yaml</c>). L'interface du consumer ne change pas.</para>
    ///
    /// <para>Cf. ADR <c>2026-05-21-Meta-pattern-templates-vs-ambig-closed</c>
    /// + <c>2026-05-21-Feat-pattern-pipeline-integration-zone-resolver</c> (P7a).</para>
    /// </summary>
    public static class DefaultPatternRegistry
    {
        /// <summary>
        /// Construit une nouvelle <see cref="PatternRegistry"/> peuplée avec
        /// les 3 templates pilote dans l'ordre de leurs <c>Order</c> respectifs.
        /// Chaque appel retourne une nouvelle instance (pas de singleton —
        /// permet aux tests d'avoir leurs propres registries indépendants).
        /// </summary>
        public static PatternRegistry Build()
        {
            return new PatternRegistry(new IPatternTemplate[]
            {
                new ForallBelongsTemplate(),
                new EnsembleTemplate(),
                new IntervalUnionTemplate(),
            });
        }

        /// <summary>
        /// Construit aussi le <see cref="PatternPipeline"/> peuplé avec les
        /// mêmes templates. <see cref="PatternRegistry"/> et
        /// <see cref="PatternPipeline"/> partagent la liste de templates
        /// (registry = lookup par nom, pipeline = orchestration ordonnée).
        /// Renvoie un tuple pour faciliter l'injection conjointe.
        /// </summary>
        public static (PatternPipeline Pipeline, PatternRegistry Registry) BuildBoth()
        {
            var templates = new IPatternTemplate[]
            {
                new ForallBelongsTemplate(),
                new EnsembleTemplate(),
                new IntervalUnionTemplate(),
            };
            return (new PatternPipeline(templates), new PatternRegistry(templates));
        }
    }
}
