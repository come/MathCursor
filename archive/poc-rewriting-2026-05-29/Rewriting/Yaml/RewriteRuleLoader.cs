using System;
using System.Collections.Generic;
using MathCursor.Engine.Rules;
using MathCursor.Engine.Vocabulary;

namespace MathCursor.Engine.Rewriting.Yaml
{
    /// <summary>
    /// Charge les règles YAML <c>data/concepts/*.yml</c> en
    /// <see cref="RewriteRule"/> directement consommables par
    /// <see cref="RewriteEngine"/>.
    ///
    /// <para>Format YAML natif (= pas de conversion legacy) :</para>
    /// <code>
    /// concept: fractions
    /// rules:
    ///   - id:       frac-explicit
    ///     pattern:  "frac {num} {den}"
    ///     produces: expr
    ///     emit:     "\frac{$num}{$den}"
    ///     tests:
    ///       - "frac a b => \frac{a}{b}"
    /// </code>
    /// </summary>
    public static class RewriteRuleLoader
    {
        public static IReadOnlyList<RewriteRule> LoadAllEmbedded(LocaleVocabulary? vocab = null)
        {
            var concepts = RuleLoader.LoadAllEmbedded();
            var rules = new List<RewriteRule>();
            foreach (var c in concepts)
            foreach (var r in c.Rules)
                rules.Add(BuildRule(r, vocab));
            return rules;
        }

        public static IReadOnlyList<RewriteRule> LoadConcept(string conceptName, LocaleVocabulary? vocab = null)
        {
            var c = RuleLoader.LoadEmbedded(conceptName);
            var rules = new List<RewriteRule>();
            foreach (var r in c.Rules)
                rules.Add(BuildRule(r, vocab));
            return rules;
        }

        public static RewriteRule BuildRule(RuleSpec spec, LocaleVocabulary? vocab = null)
        {
            var elements = ShapeParser.Parse(spec.Pattern, vocab);
            var pattern = new Pattern(elements);
            var produces = ParseCategory(spec.Produces);
            return new RewriteRule(
                id: spec.Id,
                pattern: pattern,
                produces: produces,
                emitTemplate: spec.Emit,
                priority: spec.Priority);
        }

        private static Category ParseCategory(string? value)
        {
            if (string.IsNullOrEmpty(value)) return Category.Expr;
            return value!.ToLowerInvariant() switch
            {
                "any" => Category.Any,
                "letter" => Category.Letter,
                "number" => Category.Number,
                "symbol" => Category.Symbol,
                "delim" => Category.Delim,
                "sep" => Category.Sep,
                "var" => Category.Var,
                "expr" => Category.Expr,
                "interval" => Category.Interval,
                "set" => Category.Set,
                "function" => Category.Function,
                "vector" => Category.Vector,
                _ => throw new ArgumentException(
                    $"Unknown produces category: '{value}'. Expected one of: " +
                    "any, letter, number, symbol, delim, sep, var, expr, " +
                    "interval, set, function, vector."),
            };
        }
    }
}
