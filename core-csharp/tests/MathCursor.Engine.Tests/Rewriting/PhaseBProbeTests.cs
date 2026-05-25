using System.Collections.Generic;
using MathCursor.Engine.Rewriting;
using MathCursor.Engine.Vocabulary;
using Xunit;

namespace MathCursor.Engine.Tests.Rewriting
{
    /// <summary>
    /// Phase B — probe sur les concepts YAML existants. Mesure ce que le
    /// POC RewriteEngine couvre **tel quel** vs ce qui demande des extensions
    /// du matcher.
    ///
    /// <para>Stratégie : on duplique le pattern de quelques concepts simples
    /// en règles pilote (= sans modifier le format YAML actuel), on teste,
    /// on note les gaps. Le but n'est PAS de tout porter — c'est d'identifier
    /// les extensions nécessaires.</para>
    /// </summary>
    public class PhaseBProbeTests
    {
        /// <summary>Engine avec règles pilote V2 (= simulant les concepts YAML
        /// existants dans la mesure de ce que le matcher actuel supporte).</summary>
        private static RewriteEngine BuildEngine()
        {
            var vocab = LocaleVocabulary.LoadEmbedded("fr");
            return new RewriteEngine(vocab, PhaseBRules.All);
        }

        // ─── ✅ PASSE — concepts couverts par le matcher actuel ──────────

        [Fact]
        public void Frac_simple_1_2() => AssertEqual("frac 1 2", @"\frac{1}{2}");

        [Fact]
        public void Sqrt_simple() => AssertEqual("sqrt 2", @"\sqrt{2}");

        [Fact]
        public void Sqrt_n_ieme() => AssertEqual("sqrt 3 8", @"\sqrt[3]{8}");

        [Fact]
        public void Vec_letter() => AssertEqual("vec u", @"\vec{u}");

        [Fact]
        public void Derive_x_squared() => AssertEqual("derive x y", @"\frac{d}{dx} y");

        [Fact]
        public void Iint_simple()
        {
            // iint x y body — supporte que `body` soit 1 seul Letter.
            // Si body multi-Item (= "f(x,y)"), il faut extension greedy.
            var engine = BuildEngine();
            var result = engine.Resolve("iint x y z");
            Assert.Equal(@"\iint z \, dx \, dy", result.TopLatex);
        }

        [Fact]
        public void Forall_short() => AssertEqual("forall x R", @"\forall x \in \mathbb{R}");

        [Fact]
        public void Exists_short() => AssertEqual("exists y N", @"\exists y \in \mathbb{N}");

        [Fact]
        public void Norm_letter() => AssertEqual("norm u", @"\|u\|");

        [Fact]
        public void Congru_three_args() => AssertEqual("congru a b n", @"a \equiv b \pmod{n}");

        // ─── ❌ ÉCHEC ATTENDU — concepts qui demandent extension matcher ───

        [Fact]
        public void GAP_body_greedy_required_for_sum_with_expr_body()
        {
            // sum k 1 n (1/k) → body = "(1/k)". Le `(` et `1/k` et `)` sont
            // 3 Items distincts. Mon Slot{body:Expr} ne consomme que 1 Item.
            // Gap : il faut un slot `body` qui absorbe jusqu'à la fin (= greedy).
            var engine = BuildEngine();
            var result = engine.Resolve("somme k 1 n k");
            // Cas simple à 1 Item — passe.
            Assert.Equal(@"\sum_{k=1}^{n}k", result.TopLatex);

            // Cas multi-Item — échoue avec le matcher actuel.
            // Décommenter quand l'extension greedy est implémentée (Phase C).
            // var result2 = engine.Resolve("somme k 1 n k+1");
            // Assert.Equal(@"\sum_{k=1}^{n}k+1", result2.TopLatex);
        }

        [Fact]
        public void GAP_bound_with_precedence_required()
        {
            // int x 0 n+1 body — `n+1` est `{from:bound}` = expr addsub.
            // Mon Slot{Expr} ne capture que `n`. Le `+1` reste flottant.
            // Gap : `bound` doit consommer une expression composite avec
            // précédence (= n+1 capturé comme une seule borne).
            var engine = BuildEngine();
            var result = engine.Resolve("int x 0 n y");
            // Cas simple (= bornes à 1 Item) — passe.
            Assert.Equal(@"\int_{0}^{n} y \, dx", result.TopLatex);
        }

        [Fact]
        public void GAP_optional_element_required_for_sum_equals()
        {
            // sum k =? 1 n k — l'égal est optionnel. `sum k 1 n k` et
            // `sum k=1 n k` doivent matcher la même règle.
            // Gap : PatternElement optionnel (= flag .Optional ou ?).
            var engine = BuildEngine();
            var ok = engine.Resolve("somme k 1 n k");
            Assert.Equal(@"\sum_{k=1}^{n}k", ok.TopLatex);
            // `somme k=1 n k` actuellement échoue.
        }

        [Fact]
        public void GAP_filler_optional_required_for_lim_with_words()
        {
            // lim quand x tend vers 0 f(x) — les mots `quand`, `tend`, `vers`
            // sont des fillers optionnels. Le matcher doit pouvoir les
            // ignorer (= match optionnel d'un Word stopword).
            // Gap : <filler>? = match optionnel d'un Item de catégorie stopword.
            var engine = BuildEngine();
            var ok = engine.Resolve("lim x 0 y");
            Assert.Equal(@"\lim_{x \to 0}y", ok.TopLatex);
            // `lim quand x tend vers 0 y` actuellement échoue.
        }

        [Fact]
        public void GAP_paren_grouping_required_for_body_with_parens()
        {
            // frac (x+1) (x-1) → num="(x+1)", den="(x-1)". Les parenthèses
            // doivent être traitées comme un groupe atomique (= 1 Item Expr
            // après tokenization + 1 règle "paren-group").
            // Gap : règle générique `(<expr>)` → expr qui absorbe le groupe.
            var engine = BuildEngine();
            // Pour l'instant : frac 1 2 marche. Multi-Item parens demande
            // une règle paren-group qui synthétise un Expr.
            Assert.Equal(@"\frac{1}{2}", engine.Resolve("frac 1 2").TopLatex);
        }

        private static void AssertEqual(string source, string expectedLatex)
        {
            var engine = BuildEngine();
            var result = engine.Resolve(source);
            Assert.Equal(expectedLatex, result.TopLatex);
        }
    }

    /// <summary>Règles probe Phase B — équivalents pilote des concepts YAML
    /// existants, dans la limite de ce que le matcher actuel supporte.</summary>
    internal static class PhaseBRules
    {
        public static IReadOnlyList<RewriteRule> All { get; } = new RewriteRule[]
        {
            // ── fractions.yml ───
            new RewriteRule(
                id: "frac-explicit",
                pattern: Pattern.Of(
                    PatternElement.Lit("frac"),
                    PatternElement.Slot("num", Category.Expr),
                    PatternElement.Slot("den", Category.Expr)),
                produces: Category.Expr,
                emitTemplate: @"\frac{$num}{$den}"),

            new RewriteRule(
                id: "racine-niem",
                pattern: Pattern.Of(
                    PatternElement.Lit("sqrt"),
                    PatternElement.Slot("n", Category.Number),
                    PatternElement.Slot("body", Category.Expr)),
                produces: Category.Expr,
                emitTemplate: @"\sqrt[$n]{$body}"),

            new RewriteRule(
                id: "racine-carree",
                pattern: Pattern.Of(
                    PatternElement.Lit("sqrt"),
                    PatternElement.Slot("body", Category.Expr)),
                produces: Category.Expr,
                emitTemplate: @"\sqrt{$body}"),

            // ── sommes.yml ───
            new RewriteRule(
                id: "somme-k-from-to",
                pattern: Pattern.Of(
                    PatternElement.Lit("somme"),
                    PatternElement.Slot("var", Category.Letter),
                    PatternElement.Slot("from", Category.Expr),
                    PatternElement.Slot("to", Category.Expr),
                    PatternElement.Slot("body", Category.Expr)),
                produces: Category.Expr,
                emitTemplate: @"\sum_{$var=$from}^{$to}$body"),

            // ── analyse.yml ───
            new RewriteRule(
                id: "integrale-def",
                pattern: Pattern.Of(
                    PatternElement.Lit("int"),
                    PatternElement.Slot("var", Category.Letter),
                    PatternElement.Slot("from", Category.Expr),
                    PatternElement.Slot("to", Category.Expr),
                    PatternElement.Slot("body", Category.Expr)),
                produces: Category.Expr,
                emitTemplate: @"\int_{$from}^{$to} $body \, d$var"),

            new RewriteRule(
                id: "derivee",
                pattern: Pattern.Of(
                    PatternElement.Lit("derive"),
                    PatternElement.Slot("var", Category.Letter),
                    PatternElement.Slot("body", Category.Expr)),
                produces: Category.Expr,
                emitTemplate: @"\frac{d}{d$var} $body"),

            new RewriteRule(
                id: "iint",
                pattern: Pattern.Of(
                    PatternElement.Lit("iint"),
                    PatternElement.Slot("var1", Category.Letter),
                    PatternElement.Slot("var2", Category.Letter),
                    PatternElement.Slot("body", Category.Expr)),
                produces: Category.Expr,
                emitTemplate: @"\iint $body \, d$var1 \, d$var2"),

            // ── limites.yml ───
            new RewriteRule(
                id: "lim-classic",
                pattern: Pattern.Of(
                    PatternElement.Lit("lim"),
                    PatternElement.Slot("v", Category.Letter),
                    PatternElement.Slot("a", Category.Expr),
                    PatternElement.Slot("body", Category.Expr)),
                produces: Category.Expr,
                emitTemplate: @"\lim_{$v \to $a}$body"),

            // ── vecteurs.yml ───
            new RewriteRule(
                id: "vec-explicit",
                pattern: Pattern.Of(
                    PatternElement.Lit("vec"),
                    PatternElement.Slot("body", Category.Expr)),
                produces: Category.Vector,
                emitTemplate: @"\vec{$body}"),

            // ── logique.yml ───
            new RewriteRule(
                id: "forall-belongs-short",
                pattern: Pattern.Of(
                    PatternElement.Lit("forall"),
                    PatternElement.Slot("var", Category.Letter),
                    PatternElement.Slot("set", Category.Expr)),
                produces: Category.Expr,
                emitTemplate: @"\forall $var \in $set"),

            new RewriteRule(
                id: "exists-belongs-short",
                pattern: Pattern.Of(
                    PatternElement.Lit("exists"),
                    PatternElement.Slot("var", Category.Letter),
                    PatternElement.Slot("set", Category.Expr)),
                produces: Category.Expr,
                emitTemplate: @"\exists $var \in $set"),

            // ── congruences.yml ───
            new RewriteRule(
                id: "congruence-explicit",
                pattern: Pattern.Of(
                    PatternElement.Lit("congru"),
                    PatternElement.Slot("a", Category.Expr),
                    PatternElement.Slot("b", Category.Expr),
                    PatternElement.Slot("n", Category.Expr)),
                produces: Category.Expr,
                emitTemplate: @"$a \equiv $b \pmod{$n}"),

            // ── norme.yml ───
            new RewriteRule(
                id: "norme",
                pattern: Pattern.Of(
                    PatternElement.Lit("norm"),
                    PatternElement.Slot("arg", Category.Expr)),
                produces: Category.Expr,
                emitTemplate: @"\|$arg\|"),
        };
    }
}
