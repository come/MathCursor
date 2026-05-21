using System.Collections.Generic;

namespace MathCursor.Core.Patterns
{
    /// <summary>
    /// État d'un pattern en cours de matching dans la source. Immuable :
    /// chaque modification via <see cref="WithSourceEnd"/> / <see cref="WithSlot"/>
    /// retourne une nouvelle instance.
    ///
    /// <para><see cref="SourceStart"/>/<see cref="SourceEnd"/> bornent la
    /// position consommée dans la source brute. <see cref="Slots"/> est
    /// indexé par <see cref="SlotSpec.Name"/>. Un slot non encore rempli
    /// est représenté par <see cref="EmptySlot.Instance"/>.</para>
    ///
    /// <para>Cf. ADR <c>2026-05-21-Meta-pattern-templates-vs-ambig-closed</c>,
    /// étape P2.</para>
    /// </summary>
    public sealed class PatternMatch
    {
        public string TemplateId { get; }
        public int SourceStart { get; }
        public int SourceEnd { get; }
        public IReadOnlyDictionary<string, SlotValue> Slots { get; }
        public bool IsComplete { get; }

        public PatternMatch(string templateId, int sourceStart, int sourceEnd,
            IReadOnlyDictionary<string, SlotValue> slots, bool isComplete)
        {
            TemplateId = templateId ?? throw new System.ArgumentNullException(nameof(templateId));
            SourceStart = sourceStart;
            SourceEnd = sourceEnd;
            Slots = slots ?? throw new System.ArgumentNullException(nameof(slots));
            IsComplete = isComplete;
        }

        public PatternMatch WithSourceEnd(int newSourceEnd)
            => new PatternMatch(TemplateId, SourceStart, newSourceEnd, Slots, IsComplete);

        public PatternMatch WithSlot(string slotName, SlotValue value)
        {
            if (slotName == null) throw new System.ArgumentNullException(nameof(slotName));
            if (value == null) throw new System.ArgumentNullException(nameof(value));
            var next = new Dictionary<string, SlotValue>(Slots.Count + 1);
            foreach (var kv in Slots) next[kv.Key] = kv.Value;
            next[slotName] = value;
            return new PatternMatch(TemplateId, SourceStart, SourceEnd, next, IsComplete);
        }

        public PatternMatch WithComplete(bool isComplete)
            => new PatternMatch(TemplateId, SourceStart, SourceEnd, Slots, isComplete);
    }
}
