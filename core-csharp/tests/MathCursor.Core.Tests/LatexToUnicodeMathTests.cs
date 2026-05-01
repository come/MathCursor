using MathCursor.Core;
using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Core.Tests
{
    /// <summary>
    /// Vérifie le convertisseur LaTeX → UnicodeMath : le format consommé par
    /// <c>OMaths.BuildUp()</c> côté Word. Notre LaTeX interne doit être traduit
    /// en UnicodeMath pour que Word construise une équation rendue, pas du texte
    /// brut avec des backslashes.
    /// </summary>
    public sealed class LatexToUnicodeMathTests
    {
        private readonly ITestOutputHelper _log;
        public LatexToUnicodeMathTests(ITestOutputHelper log) { _log = log; }

        [Theory]
        // Single-char shortcut appliqué à \frac, ^, _ (ADR 30-04).
        [InlineData("\\frac{1}{2}", "1/2")]
        [InlineData("\\frac{a+b}{c-d}", "(a+b)/(c-d)")]
        [InlineData("\\sqrt{x}", "√(x)")]
        [InlineData("\\sqrt{x+1}", "√(x+1)")]
        [InlineData("x^{2}", "x^2")]
        [InlineData("x_{n}", "x_n")]
        [InlineData("\\forall x \\in \\mathbb{R}", "∀ x ∈ ℝ")]
        [InlineData("\\alpha + \\beta", "α + β")]
        [InlineData("\\lim_{x \\to 0}", "lim_(x → 0)")]
        // Single-char num (1) → nu, denom multi-char → parens. Cas mixte.
        [InlineData("\\lim_{x \\to 0^+} f(x) = \\frac{1}{(x+2)^2}", "lim_(x → 0^+) f(x) = 1/((x+2)^2)")]
        [InlineData("\\binom{n}{k}", "(n¦k)")]
        [InlineData("\\operatorname{tr}(A)", "tr(A)")]
        [InlineData("\\mathrm{mod}", "mod")]
        [InlineData("\\sin^2(x)", "sin^2(x)")]
        // Accents : émis comme char + combining unicode DÉCOMPOSÉ (ce que Word
        // reconnaît dans son BuildUp — menu Accent du ribbon). On utilise des
        // escape sequences \u pour lever toute ambiguïté avec les caractères
        // précomposés (ẍ précomposé U+1E8D ≠ x + U+0308 décomposé).
        [InlineData("\\vec{AB}", "(AB)⃗")]            // Combining Right Arrow Above
        [InlineData("\\vec{u}", "u⃗")]
        [InlineData("\\hat{x}", "x̂")]                // Combining Circumflex
        [InlineData("\\widehat{ABC}", "(ABC)̂")]      // angle ABC
        [InlineData("\\bar{y}", "y̅")]                // Combining Overline
        [InlineData("\\overline{z}", "z̅")]
        [InlineData("\\dot{x}", "ẋ")]                // Combining Dot Above
        [InlineData("\\ddot{x}", "ẍ")]               // Combining Diaeresis
        [InlineData("\\tilde{a}", "ã")]              // Combining Tilde
        public void Converts_expected(string latex, string expected)
        {
            var got = LatexToUnicodeMath.Convert(latex);
            _log.WriteLine($"LaTeX: \"{latex}\"\n→ \"{got}\"\nexpected: \"{expected}\"");
            Assert.Equal(expected, got);
        }

        // ---- Régressions historiques (filet anti-rechute) ----
        // Cas qui ont déjà été des bugs en prod. Si l'un d'entre eux casse,
        // on a re-régressé sur un bug connu — d'où le test isolé et nommé.

        [Theory]
        // v0.5.2 : `\int` était rendu `∈t` au lieu de `∫`. Cause : ordre des
        // règles dans LiteralReplacements appliquait `\in → ∈` avant `\int → ∫`.
        [InlineData("\\int", "∫")]
        [InlineData("\\int_0^1 x dx", "∫_0^1 x dx")]
        [InlineData("\\iint", "∬")]
        [InlineData("\\iiint", "∭")]
        // Même famille : collisions `\to` vs `\top`, `\subset` vs `\subseteq`.
        // Si un futur refactor ré-trie l'ordre, ces cas attrapent la régression.
        [InlineData("\\to", "→")]
        [InlineData("\\subset", "⊂")]
        [InlineData("\\subseteq", "⊆")]
        // \vec{AB} : décoration combine sur 2 chars (cf. v0.5.3 vector shorthand)
        [InlineData("\\vec{OM}", "(OM)⃗")]
        public void Regression_known_bugs(string latex, string expected)
        {
            var got = LatexToUnicodeMath.Convert(latex);
            _log.WriteLine($"REGRESSION\nLaTeX: \"{latex}\"\n=> \"{got}\"\nexpected: \"{expected}\"");
            Assert.Equal(expected, got);
        }

        // ---- Bugs reportés v0.5.3 (30-04) — anti-absorption + single-char ----
        // Cf. ADR 2026-04-30-Fix-latex-to-unicodemath-refactor. Tests qui
        // REPRODUISENT le comportement attendu post-refactor. Ils sont rouges
        // tant que LatexToUnicodeMath n'a pas été réécrit en parser → AST →
        // émetteur (l'ancienne approche regex n'a pas le contexte voisin droit).

        [Theory]
        // Single-char shortcut pour `^` / `_` : argument 1 char ASCII alphanum
        // émis nu, sans parens. Word BuildUp consomme nativement (le bug
        // "des parenthèses apparaissent constamment" disparaît).
        [InlineData("x^{2}", "x^2")]
        [InlineData("x_{n}", "x_n")]
        [InlineData("a^{2}+b^{2}", "a^2+b^2")]
        [InlineData("u_{0}=1", "u_0=1")]
        // Multi-char garde parens (single-char shortcut ne s'applique pas).
        [InlineData("x^{2n}", "x^(2n)")]
        [InlineData("x^{n+1}", "x^(n+1)")]
        [InlineData("x_{i+1}", "x_(i+1)")]
        public void Single_char_sup_sub_no_parens(string latex, string expected)
        {
            var got = LatexToUnicodeMath.Convert(latex);
            _log.WriteLine($"SINGLE-CHAR\nLaTeX: \"{latex}\"\n=> \"{got}\"\nexpected: \"{expected}\"");
            Assert.Equal(expected, got);
        }

        [Theory]
        // Single-char shortcut pour `\frac` : num ET denom 1 char → `n/d` sans
        // parens. C'est le cas typique 1/2, 1/3, etc. Word BuildUp empile.
        [InlineData("\\frac{1}{2}", "1/2")]
        [InlineData("\\frac{a}{b}", "a/b")]
        // Mixte : un single + un multi → parens uniquement sur le multi.
        [InlineData("\\frac{1}{n+1}", "1/(n+1)")]
        [InlineData("\\frac{a+b}{c}", "(a+b)/c")]
        // Multi des deux côtés → parens des deux.
        [InlineData("\\frac{a+b}{c-d}", "(a+b)/(c-d)")]
        public void Frac_uses_single_char_shortcut(string latex, string expected)
        {
            var got = LatexToUnicodeMath.Convert(latex);
            _log.WriteLine($"FRAC SHORTCUT\nLaTeX: \"{latex}\"\n=> \"{got}\"\nexpected: \"{expected}\"");
            Assert.Equal(expected, got);
        }

        [Theory]
        // Anti-absorption : token tight collé après une fraction multi-char
        // doit être séparé par un espace (sinon Word l'absorbe au dénom).
        // Bug image 30-04 : `\frac{1}{2}x` rendait `1/((2)x)` dans Word.
        [InlineData("\\frac{1}{2}x", "1/2 x")]                 // single-char num+denom + token tight
        [InlineData("\\frac{a}{b}c", "a/b c")]
        [InlineData("\\frac{1}{n+1}x", "1/(n+1) x")]            // mixte + token tight
        [InlineData("\\frac{a+b}{c-d}x", "(a+b)/(c-d) x")]      // multi-char + token tight
        // Pas de séparateur si rien ne suit.
        [InlineData("\\frac{1}{2}", "1/2")]
        // Pas de séparateur si un opérateur loose suit (Word ne fusionne pas).
        [InlineData("\\frac{1}{2}+x", "1/2+x")]
        [InlineData("\\frac{1}{2} = x", "1/2 = x")]
        public void Frac_inserts_separator_to_prevent_absorption(string latex, string expected)
        {
            var got = LatexToUnicodeMath.Convert(latex);
            _log.WriteLine($"ANTI-ABSORPTION\nLaTeX: \"{latex}\"\n=> \"{got}\"\nexpected: \"{expected}\"");
            Assert.Equal(expected, got);
        }

        [Theory]
        // Intégrale : bornes bénéficient du single-char shortcut. Bug 30-04 :
        // `\int_{0}^{1} f(x) dx` rendait `∫(0)^(1) f(x) dx` (le `_` perdu).
        // Avec single-char, sortie `∫_0^1 f(x) dx` reconnue par Word BuildUp.
        [InlineData("\\int_{0}^{1} f(x) dx", "∫_0^1 f(x) dx")]
        [InlineData("\\int_{a}^{b} f", "∫_a^b f")]
        // Multi-char : parens conservées des deux côtés.
        [InlineData("\\int_{-1}^{n+1} f", "∫_(-1)^(n+1) f")]
        // Sum / prod : même règle.
        [InlineData("\\sum_{k=1}^{n} k", "∑_(k=1)^n k")]        // k=1 multi-char, n single
        [InlineData("\\sum_{i}^{N} a_i", "∑_i^N a_i")]
        public void Integral_sum_bounds_use_single_char_shortcut(string latex, string expected)
        {
            var got = LatexToUnicodeMath.Convert(latex);
            _log.WriteLine($"INTEGRAL/SUM\nLaTeX: \"{latex}\"\n=> \"{got}\"\nexpected: \"{expected}\"");
            Assert.Equal(expected, got);
        }

        // Test pivot bug image utilisateur : `1/2 x` saisie utilisateur →
        // LaTeX `\frac{1}{2}x` (la mult implicite tight produit ça côté
        // LatexRenderer, vérifié séparément par LatexRendererTests). La
        // conversion ne doit PAS placer le `x` au dénominateur.
        [Fact]
        public void Bug_image_frac_does_not_absorb_following_x()
        {
            var latex = "\\frac{1}{2}x";
            var got = LatexToUnicodeMath.Convert(latex);
            _log.WriteLine($"BUG IMAGE\nLaTeX: \"{latex}\"\n=> \"{got}\"");
            // La sortie ne doit PAS contenir `(2)x` (forme qui amène l'absorption).
            Assert.DoesNotContain("(2)x", got);
            // La sortie ne doit PAS contenir `2x` collé sans séparateur (le `x`
            // doit être indubitablement hors fraction).
            Assert.DoesNotContain("/2x", got);
            // Forme attendue post-refactor.
            Assert.Equal("1/2 x", got);
        }

        // Test pivot bug puissances : `x^2` saisie → `x^{2}` LaTeX → ne doit
        // PAS produire `(2)` visible dans Word.
        [Fact]
        public void Bug_puissances_simple_exposant_pas_de_parens()
        {
            var latex = "x^{2}";
            var got = LatexToUnicodeMath.Convert(latex);
            _log.WriteLine($"BUG PUISSANCES\nLaTeX: \"{latex}\"\n=> \"{got}\"");
            Assert.DoesNotContain("(2)", got);
            Assert.Equal("x^2", got);
        }

        // Test pivot bug intégrale : `int 0 1 f(x) dx` saisie → `\int_{0}^{1}
        // f(x) dx` LaTeX → ne doit PAS produire `∫(0)` (perte du `_`).
        [Fact]
        public void Bug_integrale_bornes_attachees_au_symbole()
        {
            var latex = "\\int_{0}^{1} f(x) dx";
            var got = LatexToUnicodeMath.Convert(latex);
            _log.WriteLine($"BUG INTEGRALE\nLaTeX: \"{latex}\"\n=> \"{got}\"");
            // La sortie doit avoir `∫_` (bornes attachées via underscore), pas `∫(`.
            Assert.Contains("∫_", got);
            Assert.DoesNotContain("∫(", got);
            Assert.Equal("∫_0^1 f(x) dx", got);
        }

        // ---- ADR 30-04 explicit-mult + dot-as-multiplier ----

        [Theory]
        // `\times` → `×` (présent dans LiteralReplacements depuis longtemps mais
        // exercé concrètement par le brief frère qui change le rendu de `*`).
        // Le LaTeX rendu inclut un espace après `\times` (cf. LatexRenderer)
        // pour la lisibilité — préservé tel quel dans la sortie OMath.
        [InlineData("a\\times b", "a× b")]
        [InlineData("3\\times 4", "3× 4")]
        [InlineData("a\\times b\\times c", "a× b× c")]
        // `\cdot` → `⋅` (U+22C5 DOT OPERATOR, idem, espace après préservé)
        [InlineData("a\\cdot b", "a⋅ b")]
        [InlineData("\\vec{u}\\cdot \\vec{v}", "u⃗⋅ v⃗")]
        public void Multiplication_symbols_convert(string latex, string expected)
        {
            var got = LatexToUnicodeMath.Convert(latex);
            _log.WriteLine($"MULT SYMBOL\nLaTeX: \"{latex}\"\n=> \"{got}\"\nexpected: \"{expected}\"");
            Assert.Equal(expected, got);
        }

        // ---- ADR 30-04 tight-implicit-mult-grouping (P5) couche (c) OMath ----

        [Theory]
        // `\frac{AB}{BC}` (P5) → après conversion : single-char shortcut ne
        // s'applique pas (AB et BC sont multi-char), parens conservées.
        // Word BuildUp lit `(AB)/(BC)` comme fraction empilée.
        [InlineData("\\frac{AB}{BC}", "(AB)/(BC)")]
        [InlineData("\\frac{AB}{B}C", "(AB)/B C")]      // AB multi-char num, B single denom + séparateur anti-absorption
        [InlineData("\\frac{1}{2x}", "1/(2x)")]          // single num, multi denom (chaîne implicite)
        [InlineData("\\frac{1}{x}+1", "1/x+1")]         // single num + single denom + op explicite
        [InlineData("\\frac{\\frac{A}{B}}{C}", "(A/B)/C")] // chaîne / / / gauche-assoc
        public void P5_tight_implicit_mult_grouping_converts_to_omath(string latex, string expected)
        {
            var got = LatexToUnicodeMath.Convert(latex);
            _log.WriteLine($"P5\nLaTeX: \"{latex}\"\n=> \"{got}\"\nexpected: \"{expected}\"");
            Assert.Equal(expected, got);
        }

        // ---- ADR 30-04 asterisk-tightness-associativity (P6) couche (c) ----

        [Theory]
        // `\frac{(1/2) \cdot 3}{4}` (P6 default tight `*`) → conversion
        // produit la fraction imbriquée Word OMath. Vérifier que les parens
        // de `(1/2)` sont conservées (sinon Word colle `1/2 \cdot 3` au num).
        [InlineData("\\frac{\\frac{1}{2}\\cdot 3}{4}", "(1/2⋅ 3)/4")]
        // `\frac{1}{2}\cdot \frac{3}{4}` (P6 alt loose `*`) → 2 fractions séparées.
        [InlineData("\\frac{1}{2}\\cdot \\frac{3}{4}", "1/2⋅ 3/4")]
        // Mêmes cas avec `\times` (setting culturel FR)
        [InlineData("\\frac{\\frac{1}{2}\\times 3}{4}", "(1/2× 3)/4")]
        [InlineData("\\frac{1}{2}\\times \\frac{3}{4}", "1/2× 3/4")]
        public void P6_asterisk_tightness_assoc_converts_to_omath(string latex, string expected)
        {
            var got = LatexToUnicodeMath.Convert(latex);
            _log.WriteLine($"P6\nLaTeX: \"{latex}\"\n=> \"{got}\"\nexpected: \"{expected}\"");
            Assert.Equal(expected, got);
        }

        // ---- ADR 30-04 french-semicolon-coordinates (P4) couche (c) ----

        [Theory]
        // `\vec{u}(1 ; 2)` (P4 séparateur français) → conversion. Le `;` est
        // juste un caractère, passe tel quel. Le `\vec{u}` devient `u⃗`
        // (combining arrow accent, single-char inner u).
        [InlineData("\\vec{u}(1 ; 2)", "u⃗(1 ; 2)")]
        [InlineData("A(1 ; 2)", "A(1 ; 2)")]
        [InlineData("M(x ; y ; z)", "M(x ; y ; z)")]
        // Vec multi-char : `(AB)⃗` (parens conservées à l'intérieur du vec)
        [InlineData("\\vec{AB}(3 ; -1)", "(AB)⃗(3 ; -1)")]
        // Avec décimal anglo dans une cellule (le `.` reste tel quel dans la
        // string LaTeX, mais en pratique le pipeline le convertit en mult avant)
        public void P4_french_semicolon_coordinates_converts_to_omath(string latex, string expected)
        {
            var got = LatexToUnicodeMath.Convert(latex);
            _log.WriteLine($"P4\nLaTeX: \"{latex}\"\n=> \"{got}\"\nexpected: \"{expected}\"");
            Assert.Equal(expected, got);
        }

        // ---- Pipeline complet : bug image (1/2 x → ½ x) ----

        // ---- ADR/brief 30-04 multiline-systems Phase 1 — align* ----

        [Theory]
        // \begin{align*} ... \end{align*} → █(...) avec & et @ pour alignement.
        // Format à 2 `&` par ligne (col1=préfixe, col2=lhs, col3=`=` rhs)
        // pour alignement gauche des flèches logiques + alignement en
        // colonne du `=`. Cf. brief 30-04 §2.1 + demande user 01-05.
        [InlineData(
            "\\begin{align*}  & 2x+1 &= 5 \\\\ \\Leftrightarrow & 2x &= 4 \\end{align*}",
            "█(&2x+1&= 5@⇔&2x&= 4)")]
        [InlineData(
            "\\begin{align*}  & f(x) &= 2x+1 \\\\  & &= 2x \\end{align*}",
            "█(&f(x)&= 2x+1@&&= 2x)")]
        public void Align_star_environment_converts_to_unicodemath(string latex, string expected)
        {
            var got = LatexToUnicodeMath.Convert(latex);
            _log.WriteLine($"ALIGN*\nLaTeX: \"{latex}\"\n=> \"{got}\"\nexpected: \"{expected}\"");
            Assert.Equal(expected, got);
        }

        [Fact]
        public void End_to_end_bug_image_frac_does_not_absorb_x()
        {
            // Bug user 30-04 (image v0.5.3) : `1/2 x` saisie → `\frac{1}{2}x`
            // LaTeX → Word OMath rendait `1/((2)x)` (x absorbé au dénom).
            // Avec mon refactor P1, la conversion produit `1/2 x` (single-char
            // shortcut + séparateur anti-absorption) qui rend correctement.
            var latex = "\\frac{1}{2}x";
            var got = LatexToUnicodeMath.Convert(latex);
            // PAS de `(2)x` collé (qui aurait causé l'absorption)
            Assert.DoesNotContain("(2)x", got);
            Assert.DoesNotContain("/2x", got);  // pas de `2x` collé après division
            // Forme attendue : `1/2 x` (espace entre dénom et `x`)
            Assert.Equal("1/2 x", got);
        }
    }
}
