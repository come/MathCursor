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
        /// <summary>Engine = règles YAML chargées + primitives essentielles.</summary>
        private static RewriteEngine BuildEngineForConcept(string conceptName)
        {
            var vocab = LocaleVocabulary.LoadEmbedded("fr");
            var rules = new List<RewriteRule>();
            // Primitives nécessaires pour la composition bottom-up (cf. Phase B+).
            rules.AddRange(PrimitiveRules.All);
            rules.AddRange(RewriteRuleLoader.LoadConcept(conceptName));
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
    }

    /// <summary>Règles primitives essentielles pour la composition bottom-up.
    /// Sera intégré aux YAML quand on stabilise le format (= ajout d'un
    /// concept <c>primitives</c> dans <c>data-v2/concepts/</c>).</summary>
    internal static class PrimitiveRules
    {
        public static IReadOnlyList<RewriteRule> All { get; } = new RewriteRule[]
        {
            new RewriteRule(
                id: "prim-paren-group",
                pattern: Pattern.Of(
                    PatternElement.Lit("("),
                    PatternElement.Slot("inner", Category.Expr),
                    PatternElement.Lit(")")),
                produces: Category.Expr,
                emitTemplate: @"$inner"),

            new RewriteRule(
                id: "prim-add",
                pattern: Pattern.Of(
                    PatternElement.Slot("a", Category.Expr),
                    PatternElement.Lit("+"),
                    PatternElement.Slot("b", Category.Expr)),
                produces: Category.Expr,
                emitTemplate: @"$a+$b"),

            new RewriteRule(
                id: "prim-sub",
                pattern: Pattern.Of(
                    PatternElement.Slot("a", Category.Expr),
                    PatternElement.Lit("-"),
                    PatternElement.Slot("b", Category.Expr)),
                produces: Category.Expr,
                emitTemplate: @"$a-$b"),
        };
    }
}
