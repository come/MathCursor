namespace MathCursor.Core.Patterns
{
    /// <summary>
    /// Spec d'un slot d'un <see cref="IPatternTemplate"/>. Immuable, déclaré
    /// statiquement par chaque template.
    ///
    /// <para>Un slot optionnel a <see cref="Required"/> = <c>false</c> et un
    /// <see cref="Opener"/> non-null qui marque le token-tête à partir duquel
    /// le slot s'active (ex. <c>"app a"</c> pour le slot domain de
    /// <c>forall-belongs</c>). Sans opener taper, le pattern parent reste
    /// valide avec ce slot absent.</para>
    ///
    /// <para>Cf. ADR <c>2026-05-21-Meta-pattern-templates-vs-ambig-closed</c>,
    /// étape P2.</para>
    /// </summary>
    public sealed class SlotSpec
    {
        public string Name { get; }
        public SlotType Type { get; }
        public bool Required { get; }
        public string? Opener { get; }

        public SlotSpec(string name, SlotType type, bool required = true, string? opener = null)
        {
            Name = name ?? throw new System.ArgumentNullException(nameof(name));
            Type = type ?? throw new System.ArgumentNullException(nameof(type));
            Required = required;
            Opener = opener;
        }
    }
}
