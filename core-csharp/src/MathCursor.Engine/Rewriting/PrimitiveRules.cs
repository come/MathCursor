using System.Collections.Generic;

namespace MathCursor.Engine.Rewriting
{
    /// <summary>
    /// Règles primitives essentielles pour la composition bottom-up du
    /// <see cref="RewriteEngine"/>. Indispensables pour que les règles
    /// YAML chargées par <see cref="Yaml.RewriteRuleLoader"/> puissent
    /// fonctionner sur des entrées composites (= <c>n+1</c>, <c>(x+1)</c>,
    /// <c>1/k</c>, <c>x^2</c>, <c>a_i</c>, <c>2n</c>, <c>+\infty</c>).
    ///
    /// <para>Organisé en 3 phases selon le <see cref="RewriteRule.Priority"/> :</para>
    /// <list type="bullet">
    ///   <item><b>Phase 0</b> (Priority 20) : fusions token-level
    ///     (<c>+\infty</c>, <c>2x</c>).</item>
    ///   <item><b>Phase 1</b> (Priority 50) : primitives binaires
    ///     (paren-group, add, sub, function-call, frac, sup, sub).</item>
    /// </list>
    ///
    /// <para>Phase 2 (Priority &gt;= 100) est réservée aux règles YAML
    /// chargées depuis <c>data-v2/concepts/*.yml</c>.</para>
    ///
    /// <para>Phase D-6 (2026-05-26) : promu de tests à source pour la
    /// bascule prod.</para>
    /// </summary>
    public static class PrimitiveRules
    {
        // Phase 0 = fusions token-level (tourne avant les binaires).
        public const int Phase0Priority = 20;
        // Phase 1 = primitives binaires (tourne après phase 0, avant anchors).
        public const int PrimPriority = 50;

        public static IReadOnlyList<RewriteRule> All { get; } = new RewriteRule[]
        {
            // ─── Phase 0 ─────────────────────────────────────────────

            new RewriteRule(
                id: "prim-signed-infinity",
                pattern: Pattern.Of(
                    PatternElement.Slot("sign", Category.Symbol),
                    PatternElement.Lit(@"\infty")),
                produces: Category.Number,
                emitTemplate: @"$sign\infty",
                priority: Phase0Priority),

            new RewriteRule(
                id: "prim-implicit-product",
                pattern: Pattern.Of(
                    PatternElement.Slot("a", Category.Number),
                    PatternElement.GluedSlot("b", Category.Letter)),
                produces: Category.Expr,
                emitTemplate: @"$a$b",
                priority: Phase0Priority),

            // Phase 0 : Letter+Number collés → exposant. `x2` → `x^{2}`,
            // `e3` → `e^{3}`, `y12` → `y^{12}`. Symétrique à prim-implicit-
            // product (qui est Number+Letter → produit). Couvre la convention
            // LetterSupSubNum du moteur legacy. Phase D-6 (2026-05-26).
            new RewriteRule(
                id: "prim-letter-num-superscript",
                pattern: Pattern.Of(
                    PatternElement.Slot("a", Category.Letter),
                    PatternElement.GluedSlot("b", Category.Number)),
                produces: Category.Expr,
                emitTemplate: @"$a^{$b}",
                priority: Phase0Priority),

            // ─── Phase 1 ─────────────────────────────────────────────

            new RewriteRule(
                id: "prim-paren-group",
                pattern: Pattern.Of(
                    PatternElement.Lit("("),
                    PatternElement.Slot("inner", Category.Expr),
                    PatternElement.Lit(")")),
                produces: Category.Expr,
                emitTemplate: @"$inner",
                priority: PrimPriority),

            new RewriteRule(
                id: "prim-add",
                pattern: Pattern.Of(
                    PatternElement.Slot("a", Category.Expr),
                    PatternElement.Lit("+"),
                    PatternElement.Slot("b", Category.Expr)),
                produces: Category.Expr,
                emitTemplate: @"$a+$b",
                priority: PrimPriority),

            new RewriteRule(
                id: "prim-sub",
                pattern: Pattern.Of(
                    PatternElement.Slot("a", Category.Expr),
                    PatternElement.Lit("-"),
                    PatternElement.Slot("b", Category.Expr)),
                produces: Category.Expr,
                emitTemplate: @"$a-$b",
                priority: PrimPriority),

            new RewriteRule(
                id: "prim-function-call-1",
                pattern: Pattern.Of(
                    PatternElement.Slot("f", Category.Letter),
                    PatternElement.GluedLit("("),
                    PatternElement.Slot("a", Category.Expr),
                    PatternElement.Lit(")")),
                produces: Category.Expr,
                emitTemplate: @"$f($a)",
                priority: PrimPriority),

            new RewriteRule(
                id: "prim-function-call-2",
                pattern: Pattern.Of(
                    PatternElement.Slot("f", Category.Letter),
                    PatternElement.GluedLit("("),
                    PatternElement.Slot("a", Category.Expr),
                    PatternElement.Lit(","),
                    PatternElement.Slot("b", Category.Expr),
                    PatternElement.Lit(")")),
                produces: Category.Expr,
                emitTemplate: @"$f($a,$b)",
                priority: PrimPriority),

            new RewriteRule(
                id: "prim-function-call-fn-1",
                pattern: Pattern.Of(
                    PatternElement.Slot("f", Category.Function),
                    PatternElement.GluedLit("("),
                    PatternElement.Slot("a", Category.Expr),
                    PatternElement.Lit(")")),
                produces: Category.Expr,
                emitTemplate: @"$f($a)",
                priority: PrimPriority),

            new RewriteRule(
                id: "prim-frac-implicit",
                pattern: Pattern.Of(
                    PatternElement.Slot("a", Category.Expr),
                    PatternElement.Lit("/"),
                    PatternElement.Slot("b", Category.Expr)),
                produces: Category.Expr,
                emitTemplate: @"\frac{$a}{$b}",
                priority: PrimPriority),

            new RewriteRule(
                id: "prim-superscript",
                pattern: Pattern.Of(
                    PatternElement.Slot("a", Category.Expr),
                    PatternElement.Lit("^"),
                    PatternElement.Slot("b", Category.Expr)),
                produces: Category.Expr,
                emitTemplate: @"$a^$b",
                priority: PrimPriority),

            new RewriteRule(
                id: "prim-subscript",
                pattern: Pattern.Of(
                    PatternElement.Slot("a", Category.Expr),
                    PatternElement.Lit("_"),
                    PatternElement.Slot("b", Category.Expr)),
                produces: Category.Expr,
                emitTemplate: @"$a_$b",
                priority: PrimPriority),
        };
    }
}
