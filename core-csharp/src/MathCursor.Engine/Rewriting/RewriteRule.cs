using System;

namespace MathCursor.Engine.Rewriting
{
    /// <summary>
    /// Règle de rewriting : pattern à matcher → template LaTeX + catégorie
    /// produite. Lue depuis YAML (<c>pattern:</c> / <c>produces:</c> /
    /// <c>emit:</c> / <c>allow_partial:</c> / <c>priority:</c>).
    ///
    /// <para>Moteur V2 (2026-05-29).</para>
    /// </summary>
    public sealed class RewriteRule
    {
        public string Id { get; }
        public Pattern Pattern { get; }
        public Category Produces { get; }
        public string EmitTemplate { get; }

        /// <summary>Autorise le match partiel (= slots manquants rendus en
        /// <c>\square</c>). Réservé aux anchors mot-clé (sum, lim, …) pour la
        /// popup guidée typing-flow. Défaut false (= primitives, délimiteurs).</summary>
        public bool AllowPartial { get; }

        /// <summary>Priorité de match. 100 = anchors YAML (défaut),
        /// &lt; 100 = primitives. Départage à span égal.</summary>
        public int Priority { get; }

        public RewriteRule(string id, Pattern pattern, Category produces,
            string emitTemplate, bool allowPartial = false, int priority = 100)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
            Produces = produces;
            EmitTemplate = emitTemplate ?? throw new ArgumentNullException(nameof(emitTemplate));
            AllowPartial = allowPartial;
            Priority = priority;
        }

        public override string ToString()
            => $"Rule[{Id}: {Pattern} → {Produces}, P{Priority}{(AllowPartial ? ":partial" : "")}]";
    }
}
