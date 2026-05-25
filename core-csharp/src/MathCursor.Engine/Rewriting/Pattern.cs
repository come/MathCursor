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
        /// <summary>Slot répété N fois, séparé par <paramref name="separator"/>.
        /// Capture une liste d'<see cref="Item"/> exposée comme
        /// <c>$name | join: "STRING"</c> dans le template emit.</summary>
        public static PatternElement Repeat(string name, Category category,
            string? separator = null, int minCount = 1, int maxCount = -1)
            => new RepeatGroup(name, category, separator, minCount, maxCount);
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

    /// <summary>
    /// Slot répété N fois, séparé par <see cref="Separator"/> (= littéral
    /// dans le SourceText, ex. <c>","</c>). Capture une <see cref="System.Collections.Generic.IReadOnlyList{Item}"/>
    /// exposée dans le template via le filtre <c>$name | join: "STRING"</c>.
    ///
    /// <para>Utilisé pour les matrices, vecteurs, listes d'arguments :
    /// <c>repeat: { slot:{expr}, sep:",", min:1 }</c>.</para>
    ///
    /// <para>Pour les matrices 2D, composer 2 règles : <c>matrix-row</c>
    /// avec sep <c>,</c> produces <c>matrix-row</c>, puis <c>matrix</c> avec
    /// sep <c>;</c> qui consomme des <c>matrix-row</c>. Le moteur de
    /// rewriting compose naturellement bottom-up.</para>
    /// </summary>
    public sealed class RepeatGroup : PatternElement
    {
        public string Name { get; }
        public Category InnerCategory { get; }
        public string? Separator { get; }
        public int MinCount { get; }
        public int MaxCount { get; }

        public RepeatGroup(string name, Category innerCategory,
            string? separator, int minCount, int maxCount)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            InnerCategory = innerCategory;
            Separator = separator;
            MinCount = minCount;
            MaxCount = maxCount;
        }

        public override string ToString() => $"{{{Name}:{InnerCategory}}}+";
    }
}
