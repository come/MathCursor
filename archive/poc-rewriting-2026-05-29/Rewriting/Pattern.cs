using System;
using System.Collections.Generic;
using System.Linq;

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
        public static PatternElement Lit(string text) => new Literal(text, optional: false);
        /// <summary>Literal optionnel : le matcher ne fait pas échouer si
        /// l'Item courant ne correspond pas — il passe à l'élément suivant
        /// sans avancer. Couvre <c>=?</c> du brief YAML (= égal optionnel
        /// après <c>sum k</c>) et les fillers simples (= mots de transition
        /// type <c>quand</c>, <c>tend</c>, <c>vers</c>). Phase C-1 (2026-05-25).</summary>
        public static PatternElement OptLit(string text) => new Literal(text, optional: true);
        /// <summary>Literal collé : exige absence de Sep avant. Utilisé pour
        /// <c>f(x)</c> (= function-call collé) vs <c>n (expr)</c> (= var +
        /// parens espacé). Phase D-4+ (2026-05-26).</summary>
        public static PatternElement GluedLit(string text) => new Literal(text, optional: false, noSepBefore: true);
        /// <summary>Literal optionnel multi-alternatives : match si l'Item
        /// courant est dans l'une des <paramref name="alternatives"/>. Sinon
        /// skip sans avancer. Utilisé pour résoudre <c>&lt;filler&gt;?</c>
        /// (= <c>['quand','lorsque']</c>) et <c>&lt;to&gt;?</c> (= <c>['-&gt;','→','tend vers']</c>).
        /// Phase C-3 (2026-05-25).</summary>
        public static PatternElement OptAnyLit(IReadOnlyList<string> alternatives)
            => new AnyLiteral(alternatives, optional: true);
        public static PatternElement Slot(string name, Category category)
            => new Slot(name, category);
        /// <summary>Slot collé : exige absence de Sep avant (= pas
        /// d'espace entre cet Item et le précédent). Pour produit
        /// implicite <c>2x</c>. Phase D-4++++ (2026-05-26).</summary>
        public static PatternElement GluedSlot(string name, Category category)
            => new Slot(name, category, noSepBefore: true);
        /// <summary>Slot répété N fois, séparé par <paramref name="separator"/>.
        /// Mode <b>1-slot</b> : capture une liste d'<see cref="Item"/> exposée
        /// comme <c>$name | join: "STRING"</c>.</summary>
        public static PatternElement Repeat(string name, Category category,
            string? separator = null, int minCount = 1, int maxCount = -1)
            => new RepeatGroup(name, category, innerElements: null, separator, minCount, maxCount);

        /// <summary>Slot composite répété N fois. Mode <b>inner composite</b> :
        /// chaque occurrence matche TOUS les <paramref name="innerElements"/>
        /// en séquence. Capture une liste de dicts, exposée comme
        /// <c>$name.slot | join: "STRING"</c> dans le template emit.
        /// Permet d'exprimer p.ex. « N paires <c>a/b</c> séparées par <c>+</c> »
        /// pour le slurp de fraction sur N termes.</summary>
        public static PatternElement RepeatBlock(string name,
            IReadOnlyList<PatternElement> innerElements,
            string? separator = null, int minCount = 1, int maxCount = -1)
            => new RepeatGroup(name, Category.Any, innerElements, separator, minCount, maxCount);
    }

    /// <summary>Texte littéral à matcher contre <see cref="Item.SourceText"/>.
    /// Si <see cref="Optional"/> est <c>true</c>, le matcher skip l'élément
    /// si l'Item courant ne correspond pas (= sans le consommer). Si
    /// <see cref="NoSepBefore"/> est <c>true</c>, le matcher exige que
    /// l'Item soit IMMÉDIATEMENT collé à l'élément précédent (= aucun Sep
    /// whitespace entre les deux). Utilisé pour distinguer <c>f(x)</c>
    /// (= function-call, collé) de <c>n (expr)</c> (= variable + parens
    /// indépendant, espacé).</summary>
    public sealed class Literal : PatternElement
    {
        public string Text { get; }
        public bool Optional { get; }
        public bool NoSepBefore { get; }
        public Literal(string text, bool optional = false, bool noSepBefore = false)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Optional = optional;
            NoSepBefore = noSepBefore;
        }
        public override string ToString() => Optional ? $"'{Text}'?" : $"'{Text}'";
    }

    /// <summary>Liste d'alternatives literales — match si l'Item courant est
    /// dans la liste. Si <see cref="Optional"/>, skip sans consommer en cas
    /// d'absence. Utilisé pour <c>&lt;filler&gt;?</c>, <c>&lt;to&gt;?</c>, etc.</summary>
    public sealed class AnyLiteral : PatternElement
    {
        public IReadOnlyList<string> Alternatives { get; }
        public bool Optional { get; }
        public AnyLiteral(IReadOnlyList<string> alternatives, bool optional = false)
        {
            Alternatives = alternatives ?? throw new ArgumentNullException(nameof(alternatives));
            Optional = optional;
        }
        public override string ToString() => Optional
            ? $"<{string.Join("|", Alternatives)}>?"
            : $"<{string.Join("|", Alternatives)}>";
    }

    /// <summary>Slot typé : capture un Item d'une catégorie donnée. Si
    /// <see cref="NoSepBefore"/>, exige absence de Sep avant (= collé à
    /// l'élément précédent du pattern). Utilisé pour produit implicite
    /// <c>2x</c> où <c>x</c> doit être collé à <c>2</c>.</summary>
    public sealed class Slot : PatternElement
    {
        public string Name { get; }
        public Category Category { get; }
        public bool NoSepBefore { get; }
        public Slot(string name, Category category, bool noSepBefore = false)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Category = category;
            NoSepBefore = noSepBefore;
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
        /// <summary>Mode 1-slot : catégorie de chaque Item répété.
        /// Ignoré si <see cref="InnerElements"/> non null.</summary>
        public Category InnerCategory { get; }
        /// <summary>Mode inner composite : sous-pattern qui se répète. Si non
        /// null, chaque occurrence matche tous ces éléments en séquence.</summary>
        public IReadOnlyList<PatternElement>? InnerElements { get; }
        public string? Separator { get; }
        public int MinCount { get; }
        public int MaxCount { get; }
        public bool IsComposite => InnerElements != null;

        public RepeatGroup(string name, Category innerCategory,
            IReadOnlyList<PatternElement>? innerElements,
            string? separator, int minCount, int maxCount)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            InnerCategory = innerCategory;
            InnerElements = innerElements;
            Separator = separator;
            MinCount = minCount;
            MaxCount = maxCount;
        }

        public override string ToString() => IsComposite
            ? $"{{{Name} : [{InnerElements!.Count} elem]}}+"
            : $"{{{Name}:{InnerCategory}}}+";
    }
}
