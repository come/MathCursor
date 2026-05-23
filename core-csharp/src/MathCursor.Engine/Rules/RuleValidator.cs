using System.Collections.Generic;
using System.Linq;
using MathCursor.Engine.Tokenization;
using MathCursor.Engine.Vocabulary;

namespace MathCursor.Engine.Rules
{
    /// <summary>
    /// Validateur au chargement — brief v4 §6.2 : signale les règles
    /// "shadowables" (= 2 shapes qui matchent le même input littéral parmi
    /// les <c>tests:</c> co-localisés).
    ///
    /// <para>Stratégie POC : pour chaque test co-localisé de chaque règle,
    /// on vérifie qu'au plus 1 règle match. Si plusieurs match → conflit
    /// rapporté. Le test ne quitte JAMAIS si 0 match (= ce serait un bug
    /// de la règle elle-même, pas un conflit).</para>
    ///
    /// <para>Aurait évité le bug F'(x)=1/x (= 2 templates matchent la même
    /// entrée parce que rien ne le détecte au load).</para>
    /// </summary>
    public static class RuleValidator
    {
        public sealed class Conflict
        {
            public string Input { get; }
            public IReadOnlyList<string> ConflictingRuleIds { get; }
            public Conflict(string input, IReadOnlyList<string> ruleIds)
            { Input = input; ConflictingRuleIds = ruleIds; }
            public override string ToString() =>
                $"'{Input}' matched by [{string.Join(", ", ConflictingRuleIds)}]";
        }

        public static IReadOnlyList<Conflict> Validate(
            IReadOnlyList<RuleSpec> rules, LocaleVocabulary vocab)
        {
            var conflicts = new List<Conflict>();
            var matcher = new ShapeMatcher(vocab);
            var tokenizer = new Tokenizer(vocab);

            // Collecte tous les inputs distincts depuis les `tests:` co-localisés.
            var inputs = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var rule in rules)
            {
                foreach (var t in rule.Tests)
                {
                    int idx = t.IndexOf("=>");
                    if (idx < 0) continue;
                    var input = t.Substring(0, idx).Trim().Trim('\'', '"');
                    inputs.Add(input);
                }
            }

            // Pour chaque input, compte combien de règles matchent au début.
            foreach (var input in inputs)
            {
                var tokens = tokenizer.Tokenize(input);
                var matched = new List<string>();
                foreach (var rule in rules)
                {
                    var m = matcher.TryMatch(rule, tokens, 0);
                    if (m != null) matched.Add(rule.Id);
                }
                if (matched.Count > 1)
                {
                    conflicts.Add(new Conflict(input, matched));
                }
            }
            return conflicts;
        }
    }
}
