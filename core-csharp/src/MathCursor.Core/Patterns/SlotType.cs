namespace MathCursor.Core.Patterns
{
    /// <summary>
    /// Type d'un <see cref="SlotSpec"/>. Hiérarchie ouverte : ajouter un
    /// nouveau type de slot = créer une sealed subclass dédiée, sans modifier
    /// les consommateurs existants (Open/Closed).
    ///
    /// <para>Cf. ADR <c>2026-05-21-Meta-pattern-templates-vs-ambig-closed</c>,
    /// étape P2.</para>
    /// </summary>
    public abstract class SlotType
    {
        private protected SlotType() { }
    }

    /// <summary>Slot qui attend un identifier unique (ex. <c>x</c>).</summary>
    public sealed class IdentifierSlot : SlotType
    {
        public static readonly IdentifierSlot Instance = new IdentifierSlot();
        private IdentifierSlot() { }
    }

    /// <summary>Slot qui attend une liste d'identifiers séparés par virgule
    /// (ex. <c>x,y,z</c> pour <c>∀x,y,z ∈ E</c>).</summary>
    public sealed class IdentifierListSlot : SlotType
    {
        public static readonly IdentifierListSlot Instance = new IdentifierListSlot();
        private IdentifierListSlot() { }
    }

    /// <summary>Slot qui attend une expression mathématique générique
    /// (rendue via l'AST + LaTeX pivot du Core).</summary>
    public sealed class ExpressionSlot : SlotType
    {
        public static readonly ExpressionSlot Instance = new ExpressionSlot();
        private ExpressionSlot() { }
    }

    /// <summary>
    /// Slot qui délègue à un autre <see cref="IPatternTemplate"/> par nom
    /// (compositionnalité). Le pipeline résout via <see cref="PatternRegistry"/>
    /// au moment de l'expansion.
    /// </summary>
    public sealed class PatternRefSlot : SlotType
    {
        /// <summary>Identifiant du pattern délégué (ex. <c>"ensemble"</c>).</summary>
        public string PatternId { get; }

        public PatternRefSlot(string patternId)
        {
            PatternId = patternId ?? throw new System.ArgumentNullException(nameof(patternId));
        }
    }
}
