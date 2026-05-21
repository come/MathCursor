using System.Collections.Generic;
using System.Linq;

namespace MathCursor.Core.Patterns
{
    /// <summary>
    /// Orchestrateur stateless qui exécute les <see cref="IPatternTemplate"/>
    /// dans l'ordre <see cref="IPatternTemplate.Order"/>. Pour chaque template,
    /// tente <see cref="IPatternTemplate.TryMatchHead"/> puis
    /// <see cref="IPatternTemplate.Expand"/> et collecte les complétions.
    ///
    /// <para>Au stade P2 : aucun template n'est encore inscrit, le pipeline
    /// tourne à vide. P3+ ajoutera EnsembleTemplate, IntervalUnionTemplate,
    /// ForallBelongsTemplate.</para>
    ///
    /// <para>Cf. ADR <c>2026-05-21-Meta-pattern-templates-vs-ambig-closed</c>,
    /// étape P2.</para>
    /// </summary>
    public sealed class PatternPipeline
    {
        private readonly IReadOnlyList<IPatternTemplate> _templates;

        public PatternPipeline(IEnumerable<IPatternTemplate> templates)
        {
            if (templates == null) throw new System.ArgumentNullException(nameof(templates));
            _templates = templates.OrderBy(t => t.Order).ToList();
        }

        /// <summary>
        /// Exécute la pipeline sur <paramref name="ctx"/>. Pour chaque template,
        /// matche le head puis étend. Concatène toutes les complétions émises.
        /// Retourne une liste vide si aucun template ne matche.
        /// </summary>
        public IReadOnlyList<PatternCompletion> Run(PatternScanContext ctx)
        {
            if (ctx == null) throw new System.ArgumentNullException(nameof(ctx));
            if (_templates.Count == 0) return System.Array.Empty<PatternCompletion>();

            var completions = new List<PatternCompletion>();
            foreach (var template in _templates)
            {
                var head = template.TryMatchHead(ctx);
                if (head == null) continue;
                var expanded = template.Expand(head, ctx);
                if (expanded == null) continue;
                completions.AddRange(expanded);
            }
            return completions;
        }
    }
}
