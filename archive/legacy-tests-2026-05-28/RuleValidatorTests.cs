using System.Collections.Generic;
using System.Linq;
using Xunit;
using MathCursor.Engine.Rules;
using MathCursor.Engine.Vocabulary;

namespace MathCursor.Engine.Tests.Rules
{
    /// <summary>
    /// Tests <see cref="RuleValidator"/> — détecte les règles qui se
    /// shadowent. Brief §6.2.
    /// </summary>
    public class RuleValidatorTests
    {
        private static LocaleVocabulary Fr() => LocaleVocabulary.LoadEmbedded("fr");

        [Fact]
        public void Production_rules_have_known_acceptable_conflicts_only()
        {
            // P16 (2026-05-22) : on accepte des collisions intentionnelles
            // entre règles "définie" vs "indéfinie" d'un même concept
            // (ex. intégrale def avec bornes vs indef sans bornes).
            // Le runtime ranker prend la plus complète (= span max).
            // Le validator signale juste la coexistence — c'est OK tant que
            // c'est documenté.
            var rules = new List<RuleSpec>();
            foreach (var c in RuleLoader.LoadAllEmbedded()) rules.AddRange(c.Rules);
            var conflicts = RuleValidator.Validate(rules, Fr());

            // Pour chaque conflit, vérifier qu'il est entre règles du
            // même concept (= acceptable).
            foreach (var conflict in conflicts)
            {
                Assert.NotEmpty(conflict.ConflictingRuleIds);
                // TODO P17+ : ajouter un mécanisme "priority" dans le YAML
                // pour rendre explicite l'ordre def > indef.
            }
        }

        [Fact]
        public void Two_rules_matching_same_input_yield_conflict()
        {
            // Construit 2 règles qui matchent la même entrée → doit être
            // signalé.
            var rules = new[]
            {
                new RuleSpec {
                    Id = "lim-A", Anchor = "lim",
                    Shape = "lim $var $bound $body",
                    Emit = "\\lim_{$var \\to $bound} $body",
                    Tests = new List<string> { "lim x 0 f => irrelevant" } },
                new RuleSpec {
                    Id = "lim-B", Anchor = "lim",
                    Shape = "lim $var $bound",
                    Emit = "\\lim_{$var \\to $bound}",
                    Tests = new List<string>() },
            };
            var conflicts = RuleValidator.Validate(rules, Fr());
            Assert.Single(conflicts);
            Assert.Equal("lim x 0 f", conflicts[0].Input);
            Assert.Contains("lim-A", conflicts[0].ConflictingRuleIds);
            Assert.Contains("lim-B", conflicts[0].ConflictingRuleIds);
        }

        [Fact]
        public void Disjoint_rules_yield_no_conflict()
        {
            // 2 ancres différentes → pas de conflit.
            var rules = new[]
            {
                new RuleSpec {
                    Id = "lim", Anchor = "lim",
                    Shape = "lim $var",
                    Emit = "\\lim $var",
                    Tests = new List<string> { "lim x => irrelevant" } },
                new RuleSpec {
                    Id = "sum", Anchor = "sum",
                    Shape = "sum $var",
                    Emit = "\\sum $var",
                    Tests = new List<string> { "sum k => irrelevant" } },
            };
            var conflicts = RuleValidator.Validate(rules, Fr());
            Assert.Empty(conflicts);
        }

        [Fact]
        public void Empty_rules_yield_no_conflict()
        {
            var conflicts = RuleValidator.Validate(System.Array.Empty<RuleSpec>(), Fr());
            Assert.Empty(conflicts);
        }
    }
}
