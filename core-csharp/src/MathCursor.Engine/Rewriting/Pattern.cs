using System;
using System.Collections.Generic;

namespace MathCursor.Engine.Rewriting
{
    /// <summary>
    /// Pattern d'une règle de rewriting : suite ordonnée de
    /// <see cref="PatternElement"/>. Le matcher consomme la séquence d'<see cref="Item"/>
    /// élément par élément.
    /// </summary>
    public sealed class Pattern
    {
        public IReadOnlyList<PatternElement> Elements { get; }

        public Pattern(IReadOnlyList<PatternElement> elements)
        {
            Elements = elements ?? throw new ArgumentNullException(nameof(elements));
        }

        public static Pattern Of(params PatternElement[] elements) => new Pattern(elements);
    }

    /// <summary>
    /// Élément d'un pattern. Polymorphe : soit un <see cref="Literal"/> (=
    /// texte exact à matcher dans <see cref="Item.SourceText"/>), soit un
    /// <see cref="Slot"/> (= match d'un Item d'une catégorie donnée, exposé
    /// comme variable <c>$name</c> dans le template emit).
    /// </summary>
    public abstract class PatternElement
    {
        public static PatternElement Lit(string text) => new Literal(text);
        public static PatternElement Slot(string name, Category category)
            => new Slot(name, category);
    }

    /// <summary>Texte littéral à matcher contre <see cref="Item.SourceText"/>.</summary>
    public sealed class Literal : PatternElement
    {
        public string Text { get; }
        public Literal(string text) { Text = text ?? throw new ArgumentNullException(nameof(text)); }
        public override string ToString() => $"'{Text}'";
    }

    /// <summary>Slot typé : capture un Item d'une catégorie donnée.</summary>
    public sealed class Slot : PatternElement
    {
        public string Name { get; }
        public Category Category { get; }
        public Slot(string name, Category category)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Category = category;
        }
        public override string ToString() => $"{{{Name}:{Category}}}";
    }
}
