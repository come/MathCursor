using System.Collections.Generic;

namespace MathCursor.Core.Patterns
{
    /// <summary>
    /// Singleton interne d'un <see cref="IReadOnlyDictionary{TKey, TValue}"/>
    /// vide de slots, utilisé par les templates qui n'ont pas de slot ou qui
    /// initialisent un <see cref="PatternMatch"/> sans slot rempli. Évite les
    /// allocations à chaque <c>TryMatchHead</c> sur un template leaf.
    /// </summary>
    internal static class EmptySlots
    {
        public static readonly IReadOnlyDictionary<string, SlotValue> Instance =
            new Dictionary<string, SlotValue>(0);
    }
}
