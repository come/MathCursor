using System;
using System.Collections.Generic;
using System.Linq;

namespace MathCursor.Engine.Rewriting
{
    /// <summary>
    /// Suite ordonnée d'<see cref="PatternElement"/> que le matcher consomme
    /// contre une séquence d'<see cref="Item"/>.
    /// </summary>
    public sealed class Pattern
    {
        public IReadOnlyList<PatternElement> Elements { get; }

        public Pattern(IReadOnlyList<PatternElement> elements)
        {
            Elements = elements ?? throw new ArgumentNullException(nameof(elements));
        }

        /// <summary>1er literal du pattern (= l'anchor), ou <c>null</c> si le
        /// pattern commence par un slot. Sert à l'index scan-keywords.</summary>
        public string? AnchorLiteral
            => Elements.Count > 0 && Elements[0] is Literal lit && !lit.Optional
                ? lit.Text
                : null;

        public override string ToString()
            => string.Join(" ", Elements.Select(e => e.ToString()));
    }

    /// <summary>Élément d'un pattern. Polymorphe.</summary>
    public abstract class PatternElement
    {
        /// <summary>True si le matcher exige l'absence de Sep AVANT cet
        /// élément (= collé à l'élément précédent). Pour <c>2x</c>, <c>f(x)</c>.</summary>
        public bool Glued { get; protected set; }
    }

    /// <summary>Texte littéral matché contre <see cref="Item.SourceText"/>.
    /// <see cref="Optional"/> : skip sans échec si absent.</summary>
    public sealed class Literal : PatternElement
    {
        public string Text { get; }
        public bool Optional { get; }

        public Literal(string text, bool optional = false, bool glued = false)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Optional = optional;
            Glued = glued;
        }

        public override string ToString()
            => (Glued ? "·" : "") + (Optional ? $"'{Text}'?" : $"'{Text}'");
    }

    /// <summary>Liste d'alternatives littérales (= <c>&lt;classname&gt;</c>
    /// résolu via le vocab). Match si l'Item est l'une des
    /// <see cref="Alternatives"/>. <see cref="Optional"/> : skip si absent.</summary>
    public sealed class AnyLiteral : PatternElement
    {
        public string ClassName { get; }
        public IReadOnlyList<string> Alternatives { get; }
        public bool Optional { get; }

        public AnyLiteral(string className, IReadOnlyList<string> alternatives,
            bool optional = false)
        {
            ClassName = className ?? throw new ArgumentNullException(nameof(className));
            Alternatives = alternatives ?? throw new ArgumentNullException(nameof(alternatives));
            Optional = optional;
        }

        public override string ToString()
            => $"<{ClassName}>" + (Optional ? "?" : "");
    }

    /// <summary>Slot typé : capture 1 Item de catégorie compatible
    /// (= subsumption). Exposé comme <c>$name</c> dans l'emit.</summary>
    public sealed class Slot : PatternElement
    {
        public string Name { get; }
        public Category Category { get; }

        public Slot(string name, Category category, bool glued = false)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Category = category;
            Glued = glued;
        }

        public override string ToString()
            => (Glued ? "·" : "") + $"{{{Name}:{Category}}}";
    }

    /// <summary>Slot répété 1D : N Items de même catégorie séparés par
    /// <see cref="Separator"/> (= literal). Capture une liste exposée via
    /// <c>$name | join: "SEP"</c>. Pour args de fonction, tuples.</summary>
    public sealed class RepeatGroup : PatternElement
    {
        public string Name { get; }
        public Category InnerCategory { get; }
        public string? Separator { get; }
        public int MinCount { get; }

        public RepeatGroup(string name, Category innerCategory,
            string? separator, int minCount = 1)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            InnerCategory = innerCategory;
            Separator = separator;
            MinCount = minCount;
        }

        public override string ToString() => $"{{{Name}:{InnerCategory}}}+";
    }

    /// <summary>Slot liste 1D : capture jusqu'au délimiteur fermant, découpe
    /// sur <see cref="Separator"/> (niveau 0), résout chaque élément (= en
    /// jetant les espaces de bord, comme le <see cref="GridSlot"/>), et joint
    /// avec <see cref="OutputSeparator"/>. Pour intervalles <c>[0;1]</c> et
    /// ensembles <c>{0;1}</c>. Même mécanique de split propre que le grid,
    /// en une dimension.</summary>
    public sealed class ListSlot : PatternElement
    {
        public string Name { get; }
        /// <summary>Séparateurs d'entrée acceptés (= `,` ET `;` : `[0,1[` ≡
        /// `[0;1[`). Le `,` est ici un séparateur de bornes, pas un décimal
        /// (= la règle structurelle tourne avant la règle décimale).</summary>
        public IReadOnlyList<string> Separators { get; }
        /// <summary>Séparateur de sortie (= `;` canonique, évite la confusion
        /// décimale en sortie).</summary>
        public string OutputSeparator { get; }

        public ListSlot(string name, IReadOnlyList<string>? separators = null,
            string outputSeparator = ";")
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Separators = separators ?? new[] { ",", ";" };
            OutputSeparator = outputSeparator;
        }

        public override string ToString() => $"{{{Name}:list}}";
    }

    /// <summary>Slot 2D (= matrice). Capture tout jusqu'au délimiteur
    /// fermant qui suit dans le pattern, découpe par <see cref="RowSeparator"/>
    /// (lignes) puis <see cref="CellSeparator"/> (cellules), résout chaque
    /// cellule via le moteur. Exposé via <c>$name</c> (= rendu
    /// <c>cell &amp; cell \\ cell &amp; cell</c>).
    ///
    /// <para>Borné par les délimiteurs de la règle (= pas de firing parasite).
    /// Lignes/colonnes variables. Cf. ADR angle mort #9/14.</para></summary>
    public sealed class GridSlot : PatternElement
    {
        public string Name { get; }
        /// <summary>Séparateur de cellule (= " " espace ou "," virgule).</summary>
        public string CellSeparator { get; }
        /// <summary>Séparateur de ligne (= ";" par défaut).</summary>
        public string RowSeparator { get; }

        public GridSlot(string name, string cellSeparator = " ", string rowSeparator = ";")
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            CellSeparator = cellSeparator;
            RowSeparator = rowSeparator;
        }

        public override string ToString() => $"{{{Name}:grid}}";
    }
}
