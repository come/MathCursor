using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MathCursor.Engine.Rewriting
{
    /// <summary>
    /// Valide un jeu de <see cref="RewriteRule"/> au load. Lève une
    /// <see cref="InvalidRuleSetException"/> listant TOUTES les erreurs
    /// (= pas une à la fois) pour un diagnostic clair au boot.
    ///
    /// <para>Vérifie : id unique, emit ne référence que des slots existants,
    /// pattern non vide.</para>
    ///
    /// <para>Moteur V2 (2026-05-29).</para>
    /// </summary>
    public static class RuleValidator
    {
        public static void Validate(IReadOnlyList<RewriteRule> rules)
        {
            var errors = new List<string>();

            // 1. Ids uniques.
            foreach (var grp in rules.GroupBy(r => r.Id).Where(g => g.Count() > 1))
                errors.Add($"id dupliqué : '{grp.Key}' ({grp.Count()}×)");

            foreach (var rule in rules)
            {
                // 2. Pattern non vide.
                if (rule.Pattern.Elements.Count == 0)
                    errors.Add($"[{rule.Id}] pattern vide");

                // 3. emit ne référence que des slots déclarés.
                var declared = CollectSlotNames(rule.Pattern);
                foreach (var refName in CollectTemplateRefs(rule.EmitTemplate))
                    if (!declared.Contains(refName))
                        errors.Add(
                            $"[{rule.Id}] emit référence ${refName} " +
                            $"absent des slots {{{string.Join(", ", declared)}}}");
            }

            if (errors.Count > 0)
                throw new InvalidRuleSetException(errors);
        }

        private static HashSet<string> CollectSlotNames(Pattern pattern)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in pattern.Elements)
            {
                switch (e)
                {
                    case Slot s: names.Add(s.Name); break;
                    case RepeatGroup r: names.Add(r.Name); break;
                    case GridSlot g: names.Add(g.Name); break;
                    case ListSlot l: names.Add(l.Name); break;
                }
            }
            return names;
        }

        private static IEnumerable<string> CollectTemplateRefs(string template)
        {
            if (string.IsNullOrEmpty(template)) yield break;
            int i = 0;
            while (i < template.Length)
            {
                // Noms alphanumériques only (= '_' est un literal LaTeX, cf.
                // RewriteMatcher.ApplyTemplate).
                if (template[i] == '$' && i + 1 < template.Length
                    && char.IsLetter(template[i + 1]))
                {
                    int j = i + 1;
                    while (j < template.Length && char.IsLetterOrDigit(template[j]))
                        j++;
                    yield return template.Substring(i + 1, j - (i + 1));
                    i = j;
                }
                else i++;
            }
        }
    }

    public sealed class InvalidRuleSetException : Exception
    {
        public InvalidRuleSetException(IReadOnlyList<string> errors)
            : base(Build(errors)) { }

        private static string Build(IReadOnlyList<string> errors)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Jeu de règles invalide ({errors.Count} erreur(s)) :");
            foreach (var e in errors) sb.AppendLine("  - " + e);
            return sb.ToString();
        }
    }
}
