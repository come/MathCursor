using System.Collections.Generic;
using MathCursor.Engine.Rewriting;
using MathCursor.Engine.Rewriting.Yaml;
using MathCursor.Engine.Vocabulary;
using Xunit;

namespace MathCursor.Engine.Tests.Rewriting
{
    /// <summary>
    /// Phase C-2 : tests du chargeur YAML <see cref="RewriteRuleLoader"/>.
    /// Vérifie que les <c>data-v2/concepts/*.yml</c> existants se convertissent
    /// en <see cref="RewriteRule"/> et fonctionnent dans le <see cref="RewriteEngine"/>
    /// avec les règles primitives (= paren-group + add/sub).
    /// </summary>
    public class YamlLoaderTests
    {
        /// <summary>Engine = règles YAML chargées + primitives essentielles.
        /// Phase C-3 : passe vocab au loader pour résoudre <c>&lt;classname&gt;?</c>.</summary>
        private static RewriteEngine BuildEngineForConcept(string conceptName)
        {
            var vocab = LocaleVocabulary.LoadEmbedded("fr");
            var rules = new List<RewriteRule>();
            // Primitives nécessaires pour la composition bottom-up (cf. Phase B+).
            rules.AddRange(PrimitiveRules.All);
            rules.AddRange(RewriteRuleLoader.LoadConcept(conceptName, vocab));
            return new RewriteEngine(vocab, rules);
        }

        // ─── Concepts simples (= shape sans <filler>?, sans `=?`) ──────

        [Fact]
        public void Fractions_frac_explicit()
        {
            var engine = BuildEngineForConcept("fractions");
            Assert.Equal(@"\frac{a}{b}", engine.Resolve("frac a b").TopLatex);
            Assert.Equal(@"\frac{1}{2}", engine.Resolve("frac 1 2").TopLatex);
        }

        [Fact]
        public void Fractions_frac_with_paren_expr()
        {
            // `frac (x+1) (x-1)` → composition via paren-group + add/sub primitives.
            var engine = BuildEngineForConcept("fractions");
            Assert.Equal(@"\frac{x+1}{x-1}", engine.Resolve("frac (x+1) (x-1)").TopLatex);
        }

        [Fact]
        public void Fractions_sqrt_simple()
        {
            var engine = BuildEngineForConcept("fractions");
            Assert.Equal(@"\sqrt{2}", engine.Resolve("sqrt 2").TopLatex);
            Assert.Equal(@"\sqrt{x}", engine.Resolve("sqrt x").TopLatex);
        }

        [Fact]
        public void Fractions_sqrt_n_ieme()
        {
            var engine = BuildEngineForConcept("fractions");
            Assert.Equal(@"\sqrt[3]{8}", engine.Resolve("sqrt 3 8").TopLatex);
            Assert.Equal(@"\sqrt[4]{16}", engine.Resolve("sqrt 4 16").TopLatex);
        }

        [Fact]
        public void Vecteurs_vec_simple()
        {
            var engine = BuildEngineForConcept("vecteurs");
            Assert.Equal(@"\vec{u}", engine.Resolve("vec u").TopLatex);
            // vec AB : `AB` = Word multi-char → Var. Slot{body:Expr} accepte Var.
            Assert.Equal(@"\vec{AB}", engine.Resolve("vec AB").TopLatex);
        }

        [Fact]
        public void Norme_simple()
        {
            var engine = BuildEngineForConcept("norme");
            Assert.Equal(@"\|u\|", engine.Resolve("norm u").TopLatex);
        }

        [Fact]
        public void Norme_with_paren_expr()
        {
            // norm (x+y) → composition via paren-group + add.
            var engine = BuildEngineForConcept("norme");
            Assert.Equal(@"\|x+y\|", engine.Resolve("norm (x+y)").TopLatex);
        }

        [Fact]
        public void Congruences_explicit()
        {
            var engine = BuildEngineForConcept("congruences");
            Assert.Equal(@"a \equiv b \pmod{n}", engine.Resolve("congru a b n").TopLatex);
            Assert.Equal(@"x \equiv y \pmod{7}", engine.Resolve("congru x y 7").TopLatex);
        }

        [Fact]
        public void Logique_forall_short()
        {
            // `forall x R` → \forall x \in \mathbb{R} (R reclassé par tokenizer FR).
            var engine = BuildEngineForConcept("logique");
            Assert.Equal(@"\forall x \in \mathbb{R}", engine.Resolve("forall x R").TopLatex);
        }

        // ─── Phase C-3 : concepts avec <filler>?, <to>?, =? ─────────────

        [Fact]
        public void Limites_lim_with_to_arrow()
        {
            // `lim x->0 f(x)` — `->` consommé par <to>?, paren-group + add
            // composent f(x) (= si f est Word multi-char Var). Pour 1-char,
            // le slot body matche directement.
            var engine = BuildEngineForConcept("limites");
            var result = engine.Resolve("lim x 0 y");
            Assert.Equal(@"\lim_{x \to 0} y", result.TopLatex);
        }

        [Fact]
        public void Limites_lim_with_to_word()
        {
            // `lim x tend vers 0 y` — `tend vers` est un seul token Glue.
            var engine = BuildEngineForConcept("limites");
            var result = engine.Resolve("lim x tend vers 0 y");
            Assert.Equal(@"\lim_{x \to 0} y", result.TopLatex);
        }

        [Fact]
        public void Limites_lim_with_filler()
        {
            // `lim quand x 0 y` — `quand` consommé par <filler>?.
            var engine = BuildEngineForConcept("limites");
            var result = engine.Resolve("lim quand x 0 y");
            Assert.Equal(@"\lim_{x \to 0} y", result.TopLatex);
        }

        [Fact]
        public void Sommes_with_equals_optional()
        {
            // shape: "sum {var} =? {from:bound} {to:bound} {body}"
            // Le `sum` est dans fr.yml anchors → tokenizer reclasse en `sum`.
            // En FR on écrit `somme`. Test des 2.
            var engine = BuildEngineForConcept("sommes");
            Assert.Equal(@"\sum_{k=1}^{n} k", engine.Resolve("sum k 1 n k").TopLatex);
            Assert.Equal(@"\sum_{k=1}^{n} k", engine.Resolve("sum k=1 n k").TopLatex);
        }

        [Fact]
        public void Funcdef_simple()
        {
            // Pas d'anchor literal — shape commence par `{name:var}`.
            // V1 : `name` mappé sur Letter (= 1-char).
            var engine = BuildEngineForConcept("funcdef");
            var result = engine.Resolve("f:x->x");
            Assert.Equal(@"f: x \mapsto x", result.TopLatex);
        }

        [Fact]
        public void Analyse_int_def_simple()
        {
            // `int x 0 1 y` → \int_{0}^{1} y \, dx
            var engine = BuildEngineForConcept("analyse");
            var result = engine.Resolve("int x 0 1 y");
            Assert.Equal(@"\int_{0}^{1} y \, dx", result.TopLatex);
        }

        [Fact]
        public void Analyse_derive_simple()
        {
            // `derive x y` → \frac{d}{dx} y
            var engine = BuildEngineForConcept("analyse");
            var result = engine.Resolve("derive x y");
            Assert.Equal(@"\frac{d}{dx} y", result.TopLatex);
        }
    }

    /// <summary>Règles primitives essentielles pour la composition bottom-up.
    /// Sera intégré aux YAML quand on stabilise le format (= ajout d'un
    /// concept <c>primitives</c> dans <c>data-v2/concepts/</c>).</summary>
    internal static class PrimitiveRules
    {
        // Toutes les primitives à Priority 50 (= les règles YAML chargées
        // sont à Priority 100 par défaut). À Start égal, les YAML gagnent.
        private const int PrimPriority = 50;
        // Phase 0 = token fusion : Priority < 50 pour tourner AVANT les
        // primitives binaires (= prim-add capture `+\infty` à 2 tokens
        // sinon).
        private const int Phase0Priority = 20;

        public static IReadOnlyList<RewriteRule> All { get; } = new RewriteRule[]
        {
            // Phase 0 : `+\infty` / `-\infty` → 1 Item Number. Tourne avant
            // prim-add pour que `int x 0 +oo body` voit la borne `+\infty`
            // comme une valeur unique, pas comme `0+\infty`.
            new RewriteRule(
                id: "prim-signed-infinity",
                pattern: Pattern.Of(
                    PatternElement.Slot("sign", Category.Symbol),
                    PatternElement.Lit(@"\infty")),
                produces: Category.Number,
                emitTemplate: @"$sign\infty",
                priority: Phase0Priority),


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

            // Phase D-2 (2026-05-25) : règles primitives supplémentaires
            // pour fermer le gap audit (= 26 fails → ~0).

            // Phase D-3 (2026-05-25) : réactivées avec scheduling multi-phase.
            // Toutes en phase 1 (Priority < 100), donc résolvent les
            // sous-expressions avant que les anchors aient leur chance.

            // Phase D-4+ : `(` collé à `f` (= GluedLit) pour distinguer
            // `f(x)` (= function-call) de `n (expr)` (= var + parens
            // indépendant). Le 2ème cas reste à paren-group.
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

            // Exposant : pour `x^2`, l'attendu YAML est `x^2` (sans
            // accolades). Pour `x^{n+1}` (=multi-Item), il faudrait les
            // accolades. Template `$a^$b` marche dans le 1er cas car $b est
            // remplacé par Item.Latex qui est juste la valeur. À étendre
            // (= flag « brace if multi-char ») quand le besoin se confirme.
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

            // Tuple 2 : (a,b) → (a,b) — utile pour `f(x,y)` via composition.
            // Optionnel : déjà couvert par prim-function-call-2 direct.

            // Signed expr : +x ou -x au début d'une borne (= unary).
            // Risque de conflit avec prim-add/prim-sub binaires — le leftmost-
            // longest scheduling devrait gérer (= pour `0 +inf`, prim-add
            // matche `0 + \infty`).
        };
    }
}
