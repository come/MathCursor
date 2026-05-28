using System.Collections.Generic;

namespace MathCursor.Engine.Rewriting
{
    /// <summary>
    /// Règles pilote hardcoded pour le POC Phase A. Servent à valider
    /// l'algorithme de rewriting avant de connecter le chargement YAML.
    ///
    /// <para>Migration Chantier 4 Phase A (2026-05-25).</para>
    /// </summary>
    public static class PilotRules
    {
        public static IReadOnlyList<RewriteRule> All { get; } = new RewriteRule[]
        {
            // vec-letter : 1 lettre seule → \vec{x}
            // Non incluse par défaut (= collision avec le default emit, à
            // activer via collision-mode ultérieurement). Gardée commentée
            // pour mémo.
            // new RewriteRule(
            //     id: "vec-letter",
            //     pattern: Pattern.Of(PatternElement.Slot("x", Category.Letter)),
            //     produces: Category.Vector,
            //     emitTemplate: @"\vec{$x}"),

            // dot-vec : x.y → \vec{x}\cdot\vec{y}
            new RewriteRule(
                id: "dot-vec",
                pattern: Pattern.Of(
                    PatternElement.Slot("a", Category.Letter),
                    PatternElement.Lit("."),
                    PatternElement.Slot("b", Category.Letter)),
                produces: Category.Vector,
                emitTemplate: @"\vec{$a}\cdot\vec{$b}"),

            // frac-explicit : frac a b → \frac{a}{b}
            new RewriteRule(
                id: "frac-explicit",
                pattern: Pattern.Of(
                    PatternElement.Lit("frac"),
                    PatternElement.Slot("a", Category.Expr),
                    PatternElement.Slot("b", Category.Expr)),
                produces: Category.Expr,
                emitTemplate: @"\frac{$a}{$b}"),

            // interval-closed : [ a ; b ] → [a; b] avec catégorie Interval.
            // Sépare avec `;` car en locale FR, `,` est le séparateur décimal
            // (= "0,1" tokenize en un seul Number = 0.1).
            new RewriteRule(
                id: "interval-closed",
                pattern: Pattern.Of(
                    PatternElement.Lit("["),
                    PatternElement.Slot("a", Category.Expr),
                    PatternElement.Lit(";"),
                    PatternElement.Slot("b", Category.Expr),
                    PatternElement.Lit("]")),
                produces: Category.Interval,
                emitTemplate: @"[$a;$b]"),

            // interval-union : {x:interval} union {y:interval} → x ∪ y
            // Démontre la composition bottom-up : les 2 intervals sont
            // reconnus en passe 1, puis cette règle s'applique en passe 2.
            new RewriteRule(
                id: "interval-union",
                pattern: Pattern.Of(
                    PatternElement.Slot("a", Category.Interval),
                    PatternElement.Lit("union"),
                    PatternElement.Slot("b", Category.Interval)),
                produces: Category.Interval,
                emitTemplate: @"$a \cup $b"),

            // sum-classic : somme {v:letter} {lo:expr} {hi:expr} {body:expr}
            //   → \sum_{v=lo}^{hi} body
            new RewriteRule(
                id: "sum-classic",
                pattern: Pattern.Of(
                    PatternElement.Lit("somme"),
                    PatternElement.Slot("v", Category.Letter),
                    PatternElement.Slot("lo", Category.Expr),
                    PatternElement.Slot("hi", Category.Expr),
                    PatternElement.Slot("body", Category.Expr)),
                produces: Category.Expr,
                emitTemplate: @"\sum_{$v=$lo}^{$hi}$body"),

            // lim-classic : lim {v:letter} {a:expr} {body:expr}
            //   → \lim_{v \to a} body
            new RewriteRule(
                id: "lim-classic",
                pattern: Pattern.Of(
                    PatternElement.Lit("lim"),
                    PatternElement.Slot("v", Category.Letter),
                    PatternElement.Slot("a", Category.Expr),
                    PatternElement.Slot("body", Category.Expr)),
                produces: Category.Expr,
                emitTemplate: @"\lim_{$v \to $a}$body"),

            // funcdef : f : x -> body → f : x \mapsto body
            new RewriteRule(
                id: "funcdef",
                pattern: Pattern.Of(
                    PatternElement.Slot("name", Category.Letter),
                    PatternElement.Lit(":"),
                    PatternElement.Slot("arg", Category.Letter),
                    PatternElement.Lit("->"),
                    PatternElement.Slot("body", Category.Expr)),
                produces: Category.Function,
                emitTemplate: @"$name : $arg \mapsto $body"),

            // matrix-row : a , b , c → "a & b & c" avec catégorie Set
            // (= utilisée comme catégorie « ligne de matrice » pour composition
            // par la règle matrix ci-dessous). Sera renommée en `MatrixRow` quand
            // on étendra le `Category` enum.
            new RewriteRule(
                id: "matrix-row",
                pattern: Pattern.Of(
                    PatternElement.Repeat("cells", Category.Expr, separator: ",", minCount: 2)),
                produces: Category.Set,
                emitTemplate: @"$cells | join: "" & """),

            // matrix : { row ; row ; ... } → \begin{matrix} ... \end{matrix}.
            // Composition bottom-up : matrix-row reconnaît chaque ligne en
            // passe 1, matrix les compose en passe 2.
            new RewriteRule(
                id: "matrix",
                pattern: Pattern.Of(
                    PatternElement.Lit("{"),
                    PatternElement.Repeat("rows", Category.Set, separator: ";", minCount: 1),
                    PatternElement.Lit("}")),
                produces: Category.Expr,
                emitTemplate: @"\begin{matrix}$rows | join: "" \\ ""\end{matrix}"),

            // frac-slurp-num : N paires `a/b` séparées par + → grande fraction
            //   \frac{a1+a2+...+aN}{b1+b2+...+bN}
            // Démontre le `RepeatBlock` (= inner composite) : chaque
            // occurrence capture 2 slots `a` et `b`. Le template
            // `$pairs.a | join: "+"` itère les occurrences et extrait `a`.
            new RewriteRule(
                id: "frac-slurp-num",
                pattern: Pattern.Of(
                    PatternElement.RepeatBlock(
                        name: "pairs",
                        innerElements: new PatternElement[]
                        {
                            PatternElement.Slot("a", Category.Expr),
                            PatternElement.Lit("/"),
                            PatternElement.Slot("b", Category.Expr),
                        },
                        separator: "+",
                        minCount: 2)),
                produces: Category.Expr,
                emitTemplate: @"\frac{$pairs.a | join: ""+""}{$pairs.b | join: ""+""}"),
        };
    }
}
