using System.Collections.Generic;
using MathCursor.Core.Patterns.Templates;
using MathCursor.Core.Patterns.Yaml;

namespace MathCursor.Core.Patterns
{
    /// <summary>
    /// Factory pour le <see cref="PatternRegistry"/> par défaut du Core :
    /// expose les templates pilote sous forme de registry pré-configurée.
    ///
    /// <para>Composition (P9e 2026-05-21) :</para>
    /// <list type="bullet">
    /// <item><b>Templates C#</b> : <c>forall-belongs</c>, <c>ensemble</c>,
    ///   <c>interval-union</c> (logiques complexes avec composition / sub-patterns
    ///   / classification var-domain → restent .cs)</item>
    /// <item><b>Templates YAML</b> : <c>lim</c>, <c>sum</c>, <c>integral</c>,
    ///   <c>derivative</c>, <c>probability</c> (= moule "args espace" générique
    ///   → définis via <c>data/patterns/*.yaml</c> embedded + chargés par
    ///   <see cref="YamlArgListPatternTemplate"/>)</item>
    /// </list>
    ///
    /// <para>Pour ajouter un nouveau pattern "args espace" : créer un nouveau
    /// fichier YAML dans <c>data/patterns/</c>, l'embed via le .csproj se fait
    /// automatiquement (wildcard <c>*.yaml</c>), et la factory le charge.
    /// Aucun .cs à écrire.</para>
    ///
    /// <para>Cf. ADR <c>2026-05-21-Feat-yaml-pattern-specs</c>.</para>
    /// </summary>
    public static class DefaultPatternRegistry
    {
        /// <summary>
        /// Construit une <see cref="PatternRegistry"/> peuplée. Chaque appel
        /// retourne une nouvelle instance avec une nouvelle liste de templates
        /// (= pas de singleton, permet aux tests d'avoir leurs registries
        /// indépendants).
        /// </summary>
        public static PatternRegistry Build()
        {
            return new PatternRegistry(LoadAllTemplates());
        }

        /// <summary>
        /// Construit registry + pipeline. <see cref="PatternPipeline"/> et
        /// <see cref="PatternRegistry"/> partagent la liste de templates.
        /// </summary>
        public static (PatternPipeline Pipeline, PatternRegistry Registry) BuildBoth()
        {
            var templates = LoadAllTemplates();
            return (new PatternPipeline(templates), new PatternRegistry(templates));
        }

        private static IReadOnlyList<IPatternTemplate> LoadAllTemplates()
        {
            var templates = new List<IPatternTemplate>(10);

            // Templates C# (logiques complexes hors moule "args espace")
            templates.Add(new ForallBelongsTemplate());
            templates.Add(new EnsembleTemplate());
            templates.Add(new IntervalUnionTemplate());
            templates.Add(new MatrixTemplate());
            templates.Add(new PrimedDerivativeTemplate());

            // Templates YAML (= moule "args espace" générique). Chaque YAML
            // embedded dans data/patterns/ est chargé et wrappé dans un
            // YamlArgListPatternTemplate.
            foreach (var yamlFile in PatternSpecLoader.ListEmbeddedPatternFiles())
            {
                var spec = PatternSpecLoader.LoadEmbedded(yamlFile);
                templates.Add(new YamlArgListPatternTemplate(spec));
            }

            return templates;
        }
    }
}
