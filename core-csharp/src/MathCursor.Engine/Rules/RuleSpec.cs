using System.Collections.Generic;

namespace MathCursor.Engine.Rules
{
    /// <summary>
    /// Spec d'une règle YAML, format natif RewriteEngine.
    ///
    /// <para>Format YAML :</para>
    /// <code>
    /// - id:       frac-explicit
    ///   pattern:  "frac {num} {den}"
    ///   produces: expr
    ///   emit:     "\frac{$num}{$den}"
    ///   tests:
    ///     - "frac a b => \frac{a}{b}"
    /// </code>
    ///
    /// <para>Le <c>pattern</c> est un shape string composé de :</para>
    /// <list type="bullet">
    ///   <item><b>literal</b> : un mot nu (= <c>frac</c>, <c>lim</c>).</item>
    ///   <item><b>literal optionnel</b> : <c>=?</c>, <c>quand?</c>.</item>
    ///   <item><b>slot</b> : <c>{name}</c> (= type <c>expr</c> par défaut).</item>
    ///   <item><b>slot typé</b> : <c>{name:type}</c> où type ∈
    ///     <c>letter</c>, <c>number</c>, <c>expr</c>, <c>set</c>, <c>interval</c>,
    ///     <c>function</c>, <c>vector</c>.</item>
    ///   <item><b>classe</b> : <c>&lt;classname&gt;?</c> (= résolu via
    ///     <c>locale/fr.yml</c> section <c>classes:</c>).</item>
    /// </list>
    ///
    /// <para><c>produces</c> = catégorie sémantique du <see cref="MathCursor.Engine.Rewriting.RewriteItem"/>
    /// produit par cette règle (= utilisée pour la composition bottom-up).</para>
    /// </summary>
    public sealed class RuleSpec
    {
        /// <summary>Identifiant interne (= clé pour le dispatch). Si vide,
        /// dérivé du nom de concept + index.</summary>
        public string Id { get; set; } = "";

        /// <summary>Shape string du pattern. Cf. format ci-dessus.</summary>
        public string Pattern { get; set; } = "";

        /// <summary>Catégorie sémantique produite (<c>expr</c>, <c>set</c>,
        /// <c>interval</c>, <c>function</c>, <c>vector</c>, etc.). Défaut :
        /// <c>expr</c>.</summary>
        public string Produces { get; set; } = "expr";

        /// <summary>Template LaTeX émis. Supporte <c>$name</c> (slot) et
        /// <c>$list | join: "SEP"</c> (repeat group).</summary>
        public string Emit { get; set; } = "";

        /// <summary>Tests co-localisés au format <c>"input =&gt; expected"</c>.</summary>
        public List<string> Tests { get; set; } = new List<string>();

        /// <summary>Priorité de match (= défaut 100). Cf. <see cref="MathCursor.Engine.Rewriting.RewriteRule.Priority"/>.</summary>
        public int Priority { get; set; } = 100;

        /// <summary>Autorise le match partiel (= slots manquants en
        /// <c>\square</c>). Réservé aux anchors mot-clé (sum, lim, …).
        /// YAML : <c>allow_partial: true</c>.</summary>
        public bool AllowPartial { get; set; } = false;
    }

    /// <summary>
    /// Conteneur d'un fichier YAML concept (= <c>data/concepts/&lt;nom&gt;.yml</c>).
    /// </summary>
    public sealed class ConceptFile
    {
        public string Concept { get; set; } = "";
        public List<RuleSpec> Rules { get; set; } = new List<RuleSpec>();
    }
}
