using System.Collections.Generic;

namespace MathCursor.Engine.Rules
{
    /// <summary>
    /// Spec d'une règle YAML — brief v4 §2.1. POCO sérialisable directement
    /// par <c>YamlDotNet</c>.
    ///
    /// <para>Format :</para>
    /// <code>
    /// shape: "&lt;filler&gt;? $var &lt;to&gt;? $bound &lt;dir&gt;? ,? $body"
    /// emit:  "\\lim_{$var \\to $bound[^{$dir}]} $body"
    /// tests:
    ///   - 'lim x-&gt;0 f(x)' =&gt; '\lim_{x \to 0} f(x)'
    /// </code>
    /// </summary>
    public sealed class RuleSpec
    {
        /// <summary>Identifiant interne (= clé pour le dispatch). Optionnel
        /// dans le YAML, sinon dérivé du nom de fichier + index.</summary>
        public string Id { get; set; } = "";

        /// <summary>Ancre textuelle (= 1er mot nu du shape).</summary>
        public string Anchor { get; set; } = "";

        /// <summary>Shape brut tel que dans le YAML.</summary>
        public string Shape { get; set; } = "";

        /// <summary>Template d'emit LaTeX.</summary>
        public string Emit { get; set; } = "";

        /// <summary>Tests co-localisés — format <c>input =&gt; expected_latex</c>.</summary>
        public List<string> Tests { get; set; } = new List<string>();

        /// <summary>Boosters optionnels (= brief §2.3). Non utilisé en P11.9.</summary>
        public string? Boost { get; set; }
        public string? When { get; set; }
    }

    /// <summary>
    /// Conteneur d'un fichier YAML concept (= <c>data-v2/concepts/&lt;nom&gt;.yml</c>).
    /// Contient potentiellement plusieurs règles apparentées (brief §2.2).
    /// </summary>
    public sealed class ConceptFile
    {
        public string Concept { get; set; } = "";
        public List<RuleSpec> Rules { get; set; } = new List<RuleSpec>();
    }
}
