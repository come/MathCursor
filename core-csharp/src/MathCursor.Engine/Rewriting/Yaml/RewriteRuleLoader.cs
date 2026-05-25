using System.Collections.Generic;
using MathCursor.Engine.Rules;
using MathCursor.Engine.Vocabulary;

namespace MathCursor.Engine.Rewriting.Yaml
{
    /// <summary>
    /// Adapter qui transforme les <see cref="ConceptFile"/> chargés par
    /// <see cref="RuleLoader"/> (= format YAML actuel <c>data-v2/concepts/*.yml</c>)
    /// en <see cref="RewriteRule"/> consommables par <see cref="RewriteEngine"/>.
    ///
    /// <para>Phase C-2 (2026-05-25) : permet de réutiliser le YAML existant
    /// sans le réécrire. La catégorie <c>produces:</c> n'étant pas encore
    /// dans le format, on infère <see cref="Category.Expr"/> par défaut +
    /// quelques cas spéciaux par <c>concept</c>.</para>
    /// </summary>
    public static class RewriteRuleLoader
    {
        /// <summary>Charge tous les concepts embarqués + convertit en
        /// <see cref="RewriteRule"/>. Le <paramref name="vocab"/> sert à
        /// résoudre <c>&lt;classname&gt;?</c> dans les shapes.</summary>
        public static IReadOnlyList<RewriteRule> LoadAllEmbedded(LocaleVocabulary? vocab = null)
        {
            var concepts = RuleLoader.LoadAllEmbedded();
            var rules = new List<RewriteRule>();
            foreach (var c in concepts)
            foreach (var r in c.Rules)
                rules.Add(ConvertRule(r, c.Concept, vocab));
            return rules;
        }

        /// <summary>Charge un seul concept par nom + convertit.</summary>
        public static IReadOnlyList<RewriteRule> LoadConcept(string conceptName, LocaleVocabulary? vocab = null)
        {
            var c = RuleLoader.LoadEmbedded(conceptName);
            var rules = new List<RewriteRule>();
            foreach (var r in c.Rules)
                rules.Add(ConvertRule(r, c.Concept, vocab));
            return rules;
        }

        /// <summary>Convertit une <see cref="RuleSpec"/> en
        /// <see cref="RewriteRule"/>.</summary>
        public static RewriteRule ConvertRule(RuleSpec spec, string conceptName, LocaleVocabulary? vocab = null)
        {
            var elements = ShapeParser.Parse(spec.Shape, vocab);
            var pattern = new Pattern(elements);
            var produces = InferProduces(conceptName);
            return new RewriteRule(
                id: spec.Id,
                pattern: pattern,
                produces: produces,
                emitTemplate: spec.Emit);
        }

        /// <summary>Heuristique V1 : infère la catégorie produite d'après
        /// le nom du concept. À remplacer par un champ <c>produces:</c>
        /// dans le YAML quand on stabilise le format.</summary>
        private static Category InferProduces(string conceptName)
        {
            return conceptName switch
            {
                "vecteurs" => Category.Vector,
                _ => Category.Expr,
            };
        }
    }
}
