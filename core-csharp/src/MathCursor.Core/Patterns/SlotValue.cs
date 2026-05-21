namespace MathCursor.Core.Patterns
{
    /// <summary>
    /// Valeur courante d'un slot dans un <see cref="PatternMatch"/>. Hiérarchie
    /// scellée à 3 cas : vide, atome textuel, sous-pattern.
    ///
    /// <para>Cf. ADR <c>2026-05-21-Meta-pattern-templates-vs-ambig-closed</c>,
    /// étape P2.</para>
    /// </summary>
    public abstract class SlotValue
    {
        private protected SlotValue() { }
        public bool IsEmpty => this is EmptySlot;
    }

    /// <summary>Slot pas encore rempli. Singleton <see cref="Instance"/> pour
    /// éviter les allocations à chaque création de pattern partiel.</summary>
    public sealed class EmptySlot : SlotValue
    {
        public static readonly EmptySlot Instance = new EmptySlot();
        private EmptySlot() { }
    }

    /// <summary>Slot rempli par un fragment textuel de la source (identifier,
    /// nombre, mot-clé). <see cref="Start"/>/<see cref="End"/> bornent la
    /// position dans la source brute.</summary>
    public sealed class FilledSlotAtom : SlotValue
    {
        public string Text { get; }
        public int Start { get; }
        public int End { get; }

        public FilledSlotAtom(string text, int start, int end)
        {
            Text = text ?? throw new System.ArgumentNullException(nameof(text));
            Start = start;
            End = end;
        }
    }

    /// <summary>Slot rempli par un sous-pattern (compositionnalité). Le
    /// <see cref="Sub"/> peut être complet ou partiel.</summary>
    public sealed class FilledSlotSubPattern : SlotValue
    {
        public PatternMatch Sub { get; }

        public FilledSlotSubPattern(PatternMatch sub)
        {
            Sub = sub ?? throw new System.ArgumentNullException(nameof(sub));
        }
    }
}
