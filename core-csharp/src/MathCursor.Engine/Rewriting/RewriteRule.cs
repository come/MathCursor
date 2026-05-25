using System;

namespace MathCursor.Engine.Rewriting
{
    /// <summary>
    /// Règle de rewriting : pattern à matcher → template LaTeX à émettre +
    /// catégorie du <see cref="RewriteItem"/> résultant.
    ///
    /// <para>Le template <see cref="EmitTemplate"/> supporte <c>$slotName</c>
    /// qui sera substitué par le <see cref="Item.Latex"/> du slot capturé.</para>
    ///
    /// <para>Migration Chantier 4 Phase A (2026-05-25) — POC rewriting-based.</para>
    /// </summary>
    public sealed class RewriteRule
    {
        public string Id { get; }
        public Pattern Pattern { get; }
        public Category Produces { get; }
        public string EmitTemplate { get; }

        public RewriteRule(string id, Pattern pattern, Category produces, string emitTemplate)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
            Produces = produces;
            EmitTemplate = emitTemplate ?? throw new ArgumentNullException(nameof(emitTemplate));
        }

        public override string ToString() => $"Rule[{Id}: {Pattern.Elements.Count} elem → {Produces}]";
    }
}
