using System.Collections.Generic;
using MathCursor.Engine.Rules;
using MathCursor.Engine.Vocabulary;

namespace MathCursor.Engine.Rewriting
{
    /// <summary>
    /// Charge les règles YAML <c>data/concepts/*.yml</c> en
    /// <see cref="RewriteRule"/> directement consommables par le
    /// <see cref="RewriteEngine"/>. Format natif (= pas de conversion) :
    /// <c>pattern:</c> / <c>produces:</c> / <c>emit:</c> / <c>allow_partial:</c>
    /// / <c>priority:</c>.
    ///
    /// <para>Moteur V2 (2026-05-29).</para>
    /// </summary>
    public static class RuleSetLoader
    {
        public static IReadOnlyList<RewriteRule> LoadAllEmbedded(LocaleVocabulary vocab)
        {
            var concepts = RuleLoader.LoadAllEmbedded();
            var rules = new List<RewriteRule>();
            foreach (var c in concepts)
                foreach (var spec in c.Rules)
                    rules.Add(Build(spec, vocab));
            RuleValidator.Validate(rules);
            return rules;
        }

        public static IReadOnlyList<RewriteRule> LoadConcept(string conceptName, LocaleVocabulary vocab)
        {
            var c = RuleLoader.LoadEmbedded(conceptName);
            var rules = new List<RewriteRule>();
            foreach (var spec in c.Rules)
                rules.Add(Build(spec, vocab));
            return rules;
        }

        public static RewriteRule Build(RuleSpec spec, LocaleVocabulary vocab)
        {
            var pattern = PatternParser.Parse(spec.Pattern, vocab);
            var produces = Categories.Parse(spec.Produces);
            return new RewriteRule(
                id: spec.Id,
                pattern: pattern,
                produces: produces,
                emitTemplate: spec.Emit,
                allowPartial: spec.AllowPartial,
                priority: spec.Priority);
        }
    }
}
