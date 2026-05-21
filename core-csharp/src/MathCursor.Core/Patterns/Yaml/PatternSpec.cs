using System.Collections.Generic;

namespace MathCursor.Core.Patterns.Yaml
{
    /// <summary>
    /// Spec de pattern chargée depuis un fichier YAML embedded
    /// (<c>data/patterns/*.yaml</c>). Consommée par
    /// <see cref="YamlArgListPatternTemplate"/> qui implémente
    /// <see cref="IPatternTemplate"/> via cette spec.
    ///
    /// <para>Format YAML (P9e 2026-05-21) :</para>
    /// <code>
    /// template_id: lim
    /// order: 0
    /// heads:
    ///   - { source: Lim, latex: '\lim', mutation: lim, weight: 100 }
    ///   - { source: lim, latex: '\lim', mutation: lim, weight: 95 }
    /// slots:
    ///   - { position: 0, name: var }
    ///   - { position: 1, name: limit, convert: infinity }
    ///   - { position: 2, name: expression, multi_token: true }
    /// scoring:
    ///   base: 25
    ///   per_slot: 25
    /// render:
    ///   preview: '\lim_{&lt;var&gt; \to &lt;limit&gt;} &lt;expression&gt;'
    ///   hint:    '\lim_{&lt;var|\square&gt; \to &lt;limit|\square&gt;} &lt;expression|\square&gt;'
    ///   description: 'lim_&lt;var|▭&gt;→&lt;limit|▭&gt; &lt;expression|▭&gt;'
    /// </code>
    ///
    /// <para>Placeholders dans les render templates :
    /// <c>&lt;name&gt;</c> = valeur du slot (vide si non rempli en preview),
    /// <c>&lt;name|fallback&gt;</c> = valeur du slot ou fallback si vide.</para>
    ///
    /// <para>Cf. ADR <c>2026-05-21-Feat-yaml-pattern-specs</c>.</para>
    /// </summary>
    public sealed class PatternSpec
    {
        public string TemplateId { get; set; } = string.Empty;
        public int Order { get; set; }
        public List<HeadSpec> Heads { get; set; } = new();
        public List<PatternSlotSpec> Slots { get; set; } = new();
        public ScoringSpec Scoring { get; set; } = new();
        public RenderTemplates Render { get; set; } = new();
    }

    /// <summary>Spec d'un head (variant de raccourci pour le pattern).</summary>
    public sealed class HeadSpec
    {
        public string Source { get; set; } = string.Empty;
        public string Latex { get; set; } = string.Empty;
        public string Mutation { get; set; } = string.Empty;
        public int Weight { get; set; } = 100;
    }

    /// <summary>Spec d'un slot positionnel du pattern.</summary>
    public sealed class PatternSlotSpec
    {
        public int Position { get; set; }
        public string Name { get; set; } = string.Empty;
        /// <summary>Convertisseur appliqué à la valeur du slot avant rendu.
        /// <c>infinity</c> = applique <see cref="ArgListPatternBase.ConvertInfinityToken"/>
        /// (et variante Unicode pour description). <c>null</c>/vide = pas de
        /// conversion (rendu littéral).</summary>
        public string? Convert { get; set; }
        /// <summary>Si true, ce slot consomme tous les args restants depuis
        /// sa position (= expression multi-tokens, ex. <c>Lim x 0 f x</c> où
        /// expression = "f x"). Doit être en dernière position.</summary>
        public bool MultiToken { get; set; }
    }

    /// <summary>Spec de calcul du <c>CompletenessScore</c>.</summary>
    public sealed class ScoringSpec
    {
        public int Base { get; set; } = 25;
        public int PerSlot { get; set; } = 25;
    }

    /// <summary>Render templates avec placeholders <c>&lt;name&gt;</c> et
    /// <c>&lt;name|fallback&gt;</c>.</summary>
    public sealed class RenderTemplates
    {
        public string Preview { get; set; } = string.Empty;
        public string Hint { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
