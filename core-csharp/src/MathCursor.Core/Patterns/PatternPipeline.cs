using System.Collections.Generic;
using System.Linq;
using MathCursor.Core.Patterns.Ranking;

namespace MathCursor.Core.Patterns
{
    /// <summary>
    /// Orchestrateur stateless qui exécute les <see cref="IPatternTemplate"/>
    /// dans l'ordre <see cref="IPatternTemplate.Order"/>. Pour chaque template,
    /// tente <see cref="IPatternTemplate.TryMatchHead"/> puis
    /// <see cref="IPatternTemplate.Expand"/> et collecte les complétions.
    ///
    /// <para>P10 (2026-05-21) : ctor accepte un <see cref="IPatternRanker"/>
    /// optionnel. Si fourni, <see cref="Run"/> applique <c>Rank()</c> avant de
    /// retourner — dédup + scoring + NMS overlap. Si null, comportement legacy
    /// (= concat brut dans l'ordre Order asc). Cf. ADR
    /// <c>2026-05-21-Feat-pattern-ranker</c>.</para>
    ///
    /// <para>Cf. ADR <c>2026-05-21-Meta-pattern-templates-vs-ambig-closed</c>,
    /// étape P2.</para>
    /// </summary>
    public sealed class PatternPipeline
    {
        private readonly IReadOnlyList<IPatternTemplate> _templates;
        private readonly IPatternRanker? _ranker;

        public PatternPipeline(IEnumerable<IPatternTemplate> templates)
            : this(templates, ranker: null) { }

        public PatternPipeline(IEnumerable<IPatternTemplate> templates, IPatternRanker? ranker)
        {
            if (templates == null) throw new System.ArgumentNullException(nameof(templates));
            _templates = templates.OrderBy(t => t.Order).ToList();
            _ranker = ranker;
        }

        /// <summary>
        /// Exécute la pipeline sur <paramref name="ctx"/>. Pour chaque template,
        /// matche le head puis étend. Concatène toutes les complétions émises,
        /// puis applique le <see cref="IPatternRanker"/> s'il a été fourni au
        /// ctor (dédup + scoring + NMS overlap).
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
            return _ranker != null ? _ranker.Rank(completions, ctx) : completions;
        }
    }
}
