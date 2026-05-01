using MathCursor.Core.Lattice;
using MathCursor.Core.Lattice.Ast;
using Xunit;

namespace MathCursor.Core.Tests.Lattice
{
    /// <summary>
    /// Tests du renderer LaTeX. On test deux niveaux :
    /// 1) Rendu direct d'AST construit à la main (isole le renderer)
    /// 2) Pipeline complet Lex → TopK → Parse → Render (intégration)
    /// </summary>
    [Collection(GlobalOptionsTestCollection.Name)]
    public sealed class LatexRendererTests : System.IDisposable
    {
        private readonly string _savedMultSymbol;

        public LatexRendererTests()
        {
            // Force le symbole de mult à `\times` pour déterminisme des tests
            // cross-culture (default culture-aware résout `\times` ou `\cdot`
            // selon CultureInfo, ce qui rendrait les tests fragiles).
            _savedMultSymbol = LatexRenderer.GlobalOptions.MultSymbol;
            LatexRenderer.GlobalOptions.MultSymbol = "\\times ";
        }

        public void Dispose()
        {
            LatexRenderer.GlobalOptions.MultSymbol = _savedMultSymbol;
        }

        private static string RenderTop(string input)
        {
            var edges = Lexer.Lex(input);
            var paths = LatticePathFinder.TopK(edges, input.Length, 3);
            var ast = new Parser(paths[0].Edges).Parse();
            return LatexRenderer.Render(ast);
        }

        // ------------------ Atomes / Holes / Const ------------------

        [Fact]
        public void Atom_ident()
            => Assert.Equal("x", LatexRenderer.Render(new Atom("ident", "x")));

        [Fact]
        public void Atom_number()
            => Assert.Equal("42", LatexRenderer.Render(new Atom("number", "42")));

        [Fact]
        public void Atom_greek_prefixed_with_backslash()
            => Assert.Equal("\\pi", LatexRenderer.Render(new Atom("greek", "pi")));

        [Fact]
        public void Hole_renders_square()
        {
            // Tous les Holes rendent \square (numérotation sacrifiée pour
            // avoir un rendu universel WpfMath ↔ Word OMath BuildUp).
            Assert.Equal("\\square ", LatexRenderer.Render(new Hole(1)));
            Assert.Equal("\\square ", LatexRenderer.Render(new Hole(2)));
            Assert.Equal("\\square ", LatexRenderer.Render(new Hole(4)));
        }

        [Fact]
        public void Const_renders_value_directly()
            => Assert.Equal("\\infty", LatexRenderer.Render(new Const("\\infty")));

        // ------------------ Bin / Unary ------------------

        [Fact]
        public void Bin_plus()
            => Assert.Equal("n+1", RenderTop("n+1"));

        [Fact]
        public void Bin_loose_plus()
            => Assert.Equal("n + 1".Replace(" ", ""), RenderTop("n + 1").Replace(" ", ""));

        [Fact]
        public void Bin_explicit_mult_uses_times()
        {
            // 2*x explicite → 2\times x (culture FR par défaut, cf. ADR
            // Feat-explicit-mult-times-vs-cdot). Le constructor du test force
            // GlobalOptions.MultSymbol à "\\times " pour déterminisme.
            var ast = new Bin("*", false, false, new Atom("number", "2"), new Atom("ident", "x"));
            Assert.Equal("2\\times x", LatexRenderer.Render(ast));
        }

        [Fact]
        public void Bin_implicit_mult_concatenates()
            => Assert.Equal("2x", RenderTop("2x"));

        [Fact]
        public void Unary_minus()
            => Assert.Equal("-x", RenderTop("-x"));

        // ------------------ Sup / Sub ------------------

        [Fact]
        public void Sup_x_pow_2()
            => Assert.Equal("x^{2}", RenderTop("x^2"));

        [Fact]
        public void Sub_u_n()
            => Assert.Equal("u_{n}", RenderTop("u_n"));

        [Fact]
        public void Sup_unwraps_group()
        {
            // x^(n+1) : le Group autour de n+1 doit être unwrappé dans les {}
            Assert.Equal("x^{n+1}", RenderTop("x^(n+1)"));
        }

        // ------------------ Group ------------------

        [Fact]
        public void Group_uses_left_right_parens()
            => Assert.Equal("\\left(x+y\\right)", RenderTop("(x+y)"));

        // ------------------ Func ------------------

        [Fact]
        public void Func_atom_arg_uses_space()
            => Assert.Equal("\\cos x", RenderTop("cos x"));

        [Fact]
        public void Func_implicit_tight_arg_uses_space()
            => Assert.Equal("\\cos 2x", RenderTop("cos2x"));

        [Fact]
        public void Func_group_arg_no_extra_parens()
            => Assert.Equal("\\cos\\left(x+y\\right)", RenderTop("cos(x+y)"));

        // ------------------ Frac / Sqrt / Vec ------------------

        [Fact]
        public void Frac_unwraps_args()
            => Assert.Equal("\\frac{a}{b}", RenderTop("frac a b"));

        [Fact]
        public void Frac_with_hole_for_den()
            => Assert.Equal("\\frac{a}{\\square }", RenderTop("frac a"));

        [Fact]
        public void Sqrt_unwraps_arg()
            => Assert.Equal("\\sqrt{x}", RenderTop("racine x"));

        [Fact]
        public void Sqrt_with_hole()
            => Assert.Equal("\\sqrt{\\square }", RenderTop("racine"));

        [Fact]
        public void Vec_with_name()
            => Assert.Equal("\\vec{AB}", RenderTop("vec AB"));

        [Fact]
        public void Vec_alone_renders_hole()
            => Assert.Equal("\\vec{\\square }", RenderTop("vec"));

        // ------------------ Sum / Lim / Int ------------------

        [Fact]
        public void Sum_complete()
            => Assert.Equal(
                "\\sum_{k=1}^{n+1} \\cos 2x",
                RenderTop("sum k=1 n+1 cos2x"));

        [Fact]
        public void Sum_partial_with_holes()
            => Assert.Equal("\\sum_{k=\\square }^{\\square } \\square ", RenderTop("sum k"));

        [Fact]
        public void Lim_with_arrow()
            => Assert.Equal("\\lim_{x \\to 0} f\\left(x\\right)", RenderTop("lim x -> 0 f(x)"));

        [Fact]
        public void Int_simple()
            => Assert.Equal("\\int_{0}^{1} x", RenderTop("int 0 1 x"));

        // ------------------ Composabilité ------------------

        [Fact]
        public void Sum_with_nested_racine()
            => Assert.Equal("\\sum_{k=1}^{n} \\sqrt{k}", RenderTop("sum k=1 n racine k"));

        [Fact]
        public void Lim_with_nested_frac_sin()
            => Assert.Equal(
                "\\lim_{x \\to 0} \\frac{\\sin x}{x}",
                RenderTop("lim x 0 frac sin x x"));

        [Fact]
        public void Nested_sum_within_sum()
            => Assert.Equal(
                "\\sum_{i=1}^{n} \\sum_{j=1}^{i} ij",
                RenderTop("sum i=1 n sum j=1 i ij"));

        [Fact]
        public void Sum_body_stops_at_loose_plus()
            // body = f(k), puis Sum + g(k) au top-level
            => Assert.Equal(
                "\\sum_{k=1}^{n} f\\left(k\\right)+g\\left(k\\right)",
                RenderTop("sum k 1 n f(k) + g(k)"));

        [Fact]
        public void Sum_body_consumes_tight_plus()
            => Assert.Equal(
                "\\sum_{k=1}^{n} f\\left(k\\right)+1",
                RenderTop("sum k 1 n f(k)+1"));

        // ------------------ Relations ------------------

        [Fact]
        public void Equals_renders_directly()
            => Assert.Equal("x=1", RenderTop("x = 1"));

        [Fact]
        public void Leq_renders_with_latex_command_and_spaces()
            => Assert.Equal("a \\leq b", RenderTop("a <= b"));

        [Fact]
        public void Geq_renders_with_latex_command_and_spaces()
            => Assert.Equal("a \\geq b", RenderTop("a >= b"));

        [Fact]
        public void Neq_renders_with_latex_command_and_spaces()
        {
            Assert.Equal("a \\neq b", RenderTop("a != b"));
            Assert.Equal("a \\neq b", RenderTop("a <> b"));
        }

        [Fact]
        public void Equation_with_complex_rhs_fully_rendered()
            // Régression : alpha + 1 = (a+x)/(b+x) ne doit plus être tronqué à α+1.
            // Le `/` est rendu en \frac empilé (typo math) — les Group autour des
            // opérandes sont déballés pour éviter les parens redondantes.
            => Assert.Equal(
                "\\alpha+1=\\frac{a+x}{b+x}",
                RenderTop("alpha + 1 = (a+x)/(b+x)"));

        [Fact]
        public void Slash_division_renders_as_frac()
            // Cas user : alpha=1/(x+1)^2 → fraction empilée, pas inline 1/(x+1)²
            => Assert.Equal(
                "\\alpha=\\frac{1}{\\left(x+1\\right)^{2}}",
                RenderTop("alpha=1/(x+1)^2"));

        // Règle générique "Number tight après nom non-Number = exposant" :
        // x² implicite à partir de x2, etc.
        [Fact]
        public void X2_renders_as_x_squared()
            => Assert.Equal("x^{2}", RenderTop("x2"));

        [Fact]
        public void Cos2_paren_renders_with_exp_after_arg()
            // Convention simple et compatible Word : exposant après l'expression
            // complète, pas sur le nom de la fonction. Pas de \cos^{2}(x) qui
            // créait un piège d'absorption "cos^{2(x)}" côté UnicodeMath.
            => Assert.Equal("\\cos\\left(x\\right)^{2}", RenderTop("cos2(x)"));

        [Fact]
        public void Cos2x_without_paren_keeps_implicit_mult()
            // Sans parens, on garde la convention "cos appliqué à 2x"
            => Assert.Equal("\\cos 2x", RenderTop("cos2x"));

        [Fact]
        public void Number_first_stays_multiplication()
        {
            // 2x reste 2*x (Number n'est pas le primary qui prend l'exposant)
            Assert.Equal("2x", RenderTop("2x"));
            // 2PIR reste un produit (P-I rendu en pi greek puis R ident)
            Assert.Equal("2\\piR", RenderTop("2PIR"));
        }

        [Fact]
        public void Two_numbers_adjacent_not_treated_as_exponent()
            // "23" n'est jamais "2 puissance 3" — la règle exclut Number primary
            => Assert.Equal("23", RenderTop("23"));

        // ------------------ Cas non-triviaux du brief ------------------

        [Fact]
        public void Racine_of_racine()
            => Assert.Equal("\\sqrt{\\sqrt{x}}", RenderTop("racine racine x"));

        [Fact]
        public void Int_with_frac_body()
            => Assert.Equal("\\int_{0}^{1} \\frac{x}{x+1}", RenderTop("int 0 1 frac x x+1"));

        // ------------------ Quantificateurs ------------------

        // Décomposition modulaire (cf. ADR 29-04 supersedes scope du 28-04) :
        // forall + var + dans/in/(- + set se composent naturellement par
        // juxtaposition, sans nœud Quant ni grammaire scope.

        [Fact]
        public void Forall_alone_renders_just_forall()
            // Trailing space pour la juxtaposition (cf. ADR 29-04).
            => Assert.Equal("\\forall ", RenderTop("forall"));

        [Fact]
        public void Exists_alone_renders_just_exists()
            => Assert.Equal("\\exists ", RenderTop("exists"));

        [Fact]
        public void Forall_x_dans_R_via_juxtaposition()
            // forall + x + dans (\in) + R → composition naturelle. Le \in
            // a des espaces autour (cf. Const " \\in ") qui se propagent.
            => Assert.Equal("\\forall x \\in R", RenderTop("forall x dans R"));

        [Fact]
        public void Forall_x_in_R_via_juxtaposition()
            => Assert.Equal("\\forall x \\in R", RenderTop("forall x in R"));

        [Fact]
        public void Forall_x_arrow_R_keyboard_alias()
            // forall x (- R : `(-` est l'alias clavier de \in
            => Assert.Equal("\\forall x \\in R", RenderTop("forall x (- R"));

        [Fact]
        public void Exists_y_dans_N_via_juxtaposition()
            => Assert.Equal("\\exists y \\in N", RenderTop("exists y dans N"));

        // ------------------ Intervalles français ------------------

        [Fact]
        public void Closed_interval_renders_brackets()
            => Assert.Equal("[0,1]", RenderTop("[0,1]"));

        [Fact]
        public void Closed_open_interval_renders_correctly()
            => Assert.Equal("[0,1[", RenderTop("[0,1["));

        [Fact]
        public void Open_closed_interval_renders_correctly()
            => Assert.Equal("]0,1]", RenderTop("]0,1]"));

        [Fact]
        public void Open_open_interval_renders_correctly()
            => Assert.Equal("]0,1[", RenderTop("]0,1["));

        [Fact]
        public void Interval_with_neg_infinity_renders()
            // ]-inf,1] avec inf keyword → ]-\infty,1]
            => Assert.Equal("]-\\infty,1]", RenderTop("]-inf,1]"));

        [Fact]
        public void Interval_in_forall_set_explicit_in()
        {
            // Décomposition modulaire (ADR 29-04) : il faut taper `dans`/`in`/`(-`
            // explicitement entre var et set. Sans, c'est juste une mult implicite.
            Assert.Equal("\\forall x \\in [0,1]", RenderTop("forall x dans [0,1]"));
        }

        [Fact]
        public void Interval_in_forall_no_in_keyword_is_juxtaposition()
        {
            // `forall x [0,1]` sans keyword `dans` = juxtaposition simple
            Assert.Equal("\\forall x[0,1]", RenderTop("forall x [0,1]"));
        }

        // ------------------ Union / Intersection d'intervalles ------------------

        [Fact]
        public void Union_keyword_renders_with_cup()
            => Assert.Equal("[0,1] \\cup [3,5]", RenderTop("[0,1] union [3,5]"));

        [Fact]
        public void U_between_intervals_renders_with_cup()
            => Assert.Equal("[0,1] \\cup [3,5]", RenderTop("[0,1] U [3,5]"));

        [Fact]
        public void U_between_intervals_no_space_renders_with_cup()
            => Assert.Equal("[0,1] \\cup [3,5]", RenderTop("[0,1]U[3,5]"));

        [Fact]
        public void Inter_keyword_renders_with_cap()
            // Test du keyword `inter`. On évite les décimales `0.5` car le `.`
            // est désormais un opérateur de mult (ADR Feat-dot-as-multiplier).
            // Pour les décimales en input, l'utilisateur tape `0,5` ou utilise
            // l'alt cascade RuleDecimalVsMultiplication.
            => Assert.Equal("[0,1] \\cap [1,2]", RenderTop("[0,1] inter [1,2]"));

        [Fact]
        public void Forall_with_union_set_explicit_in()
            // forall x dans [0,1]U[2,3] → \forall x \in [0,1] \cup [2,3]
            // (avec keyword `dans` explicite, cf. décomposition modulaire 29-04)
            => Assert.Equal("\\forall x \\in [0,1] \\cup [2,3]", RenderTop("forall x dans [0,1]U[2,3]"));

        // ------------------ ADR 30-04 tight-implicit-mult-grouping ------------------
        // (cf. 2026-04-30-Feat-tight-implicit-mult-grouping)
        // Règle : sur le rhs de `/`, `^`, `_` tight, on absorbe la CHAÎNE DE MULT
        // IMPLICITE tight (juxtaposition) UNIQUEMENT — les ops `+ - *` tight cassent
        // la chaîne (PEMDAS standard). L'élargissement aux ops tight est exposé en
        // alt de désambig (cf. AlternativeGeneratorTests).

        [Fact]
        public void AB_slash_DC_implicit_chain_grouped()
            // AB et DC sont des juxtapositions (mult implicite tight) → groupés
            // dans num/denom. Résultat : \frac{AB}{DC}.
            => Assert.Equal("\\frac{AB}{DC}", RenderTop("AB/DC"));

        [Fact]
        public void AB_slash_BC_implicit_chain_grouped()
            => Assert.Equal("\\frac{AB}{BC}", RenderTop("AB/BC"));

        [Fact]
        public void Slash_with_implicit_mult_2x_grouped_in_denom()
            // 1/2x : la chaîne implicite 2x est absorbée au dénom → \frac{1}{2x}.
            => Assert.Equal("\\frac{1}{2x}", RenderTop("1/2x"));

        [Fact]
        public void Slash_followed_by_plus_does_not_group_rhs_pemdas()
            // 1/x+1 collé : `+` op explicite casse la chaîne → \frac{1}{x}+1.
            // L'élargissement `\frac{1}{x+1}` est en alt désambig.
            => Assert.Equal("\\frac{1}{x}+1", RenderTop("1/x+1"));

        [Fact]
        public void Slash_with_space_before_plus_keeps_pemdas()
            // 1/x +1 (espace) : précédence standard, idem que collé après ADR 30-04.
            => Assert.Equal("\\frac{1}{x}+1", RenderTop("1/x +1"));

        [Fact]
        public void Slash_with_space_breaks_implicit_chain()
            // 1/2 x (espace) : chaîne implicite cassée → \frac{1}{2}x.
            => Assert.Equal("\\frac{1}{2}x", RenderTop("1/2 x"));

        [Fact]
        public void Simple_slash_unchanged()
            // 1/x simple : \frac{1}{x}
            => Assert.Equal("\\frac{1}{x}", RenderTop("1/x"));

        [Fact]
        public void Slash_chain_is_left_associative_pemdas()
            // A/B/C : gauche-associatif (PEMDAS). \frac{\frac{A}{B}}{C}, pas A/(B/C).
            => Assert.Equal("\\frac{\\frac{A}{B}}{C}", RenderTop("A/B/C"));

        [Fact]
        public void Slash_between_func_groups_unchanged()
            // cos(x)/sin(x) : groupes explicites → \frac{\cos(x)}{\sin(x)}
            => Assert.Equal(
                "\\frac{\\cos\\left(x\\right)}{\\sin\\left(x\\right)}",
                RenderTop("cos(x)/sin(x)"));

        [Fact]
        public void Sup_with_implicit_mult_grouped()
            // x^2n : chaîne implicite `2n` dans l'exposant → x^{2n}.
            => Assert.Equal("x^{2n}", RenderTop("x^2n"));

        [Fact]
        public void Sup_with_explicit_op_does_not_group_pemdas()
            // x^a+b : `+` op explicite casse la chaîne → x^{a}+b.
            // L'élargissement `x^{a+b}` est en alt désambig.
            => Assert.Equal("x^{a}+b", RenderTop("x^a+b"));

        [Fact]
        public void Sub_with_explicit_op_does_not_group_pemdas()
            // u_n+1 : `+` op explicite casse la chaîne → u_{n}+1.
            // L'élargissement `u_{n+1}` est en alt désambig.
            => Assert.Equal("u_{n}+1", RenderTop("u_n+1"));

        [Fact]
        public void Sup_with_paren_unchanged()
            // x^(a+b) : parens explicites → x^{a+b}.
            => Assert.Equal("x^{a+b}", RenderTop("x^(a+b)"));

        // ------------------ ADR 30-04 asterisk-tightness-associativity ------------------
        // `*` tight (collé) → gauche-assoc PEMDAS ; `*` loose (espace) → droite-récursive.
        // L'inverse est exposé en cascade de désambig (cf. AlternativeGeneratorTests).

        [Fact]
        public void Asterisk_tight_left_assoc_pemdas()
            // a*b/3 (tight) : `(a*b)/3` = \frac{a\times b}{3}
            => Assert.Equal("\\frac{a\\times b}{3}", RenderTop("a*b/3"));

        [Fact]
        public void Asterisk_loose_right_recursive_after_space()
            // a* b/3 (espace après `*`) : a*(b/3) = a\times \frac{b}{3}
            => Assert.Equal("a\\times \\frac{b}{3}", RenderTop("a* b/3"));

        [Fact]
        public void Asterisk_loose_right_recursive_before_space()
            // a *b/3 (espace avant `*`) : a*(b/3) = a\times \frac{b}{3}
            => Assert.Equal("a\\times \\frac{b}{3}", RenderTop("a *b/3"));

        [Fact]
        public void Asterisk_loose_right_recursive_both_spaces()
            // a * b/3 : idem, loose * → droite-récursive
            => Assert.Equal("a\\times \\frac{b}{3}", RenderTop("a * b/3"));

        [Fact]
        public void Asterisk_tight_two_fractions_pemdas()
            // 1/2*3/4 (tight) : `((1/2)*3)/4` = \frac{(1/2)\times 3}{4}
            => Assert.Equal("\\frac{\\frac{1}{2}\\times 3}{4}", RenderTop("1/2*3/4"));

        [Fact]
        public void Asterisk_loose_two_fractions_separated()
            // 1/2 * 3/4 (loose) : (1/2)*(3/4) = \frac{1}{2}\times \frac{3}{4}
            => Assert.Equal("\\frac{1}{2}\\times \\frac{3}{4}", RenderTop("1/2 * 3/4"));

        [Fact]
        public void Asterisk_implicit_mult_unchanged()
            // 2x/3 : mult IMPLICITE (juxtaposition) reste gauche-assoc tight,
            // pas affectée par la nouvelle règle (qui cible `*` explicite).
            => Assert.Equal("\\frac{2x}{3}", RenderTop("2x/3"));

        // ------------------ ADR 30-04 explicit-mult-times-vs-cdot ------------------
        // GlobalOptions.MultSymbol contrôle le rendu de `*`. Vec*Vec forcé `\cdot`
        // (convention produit scalaire). Number-Number juxtaposition utilise le
        // symbole explicit (fix du bug `2 3` → `23` collés).

        [Fact]
        public void Vec_times_vec_forces_cdot_independent_of_setting()
        {
            // vec u * vec v : Vec * Vec → toujours \cdot (produit scalaire),
            // même si setting = \times.
            // Le constructor du test set \times, mais Vec*Vec doit ignorer.
            var ast = new Bin("*", true, false, new Vec("u"), new Vec("v"));
            Assert.Equal("\\vec{u}\\cdot \\vec{v}", LatexRenderer.Render(ast));
        }

        [Fact]
        public void Vec_times_vec_renders_cdot_via_pipeline()
            // Pipeline complet : `vec u * vec v` → \vec{u}\cdot \vec{v}
            => Assert.Equal("\\vec{u}\\cdot \\vec{v}", RenderTop("vec u * vec v"));

        [Fact]
        public void Number_times_number_juxtaposition_uses_explicit_symbol()
        {
            // 2 3 (juxtaposition implicite avec espace) : doit rendre `2\times 3`
            // (selon GlobalOptions = \times pour les tests). Sans cette règle,
            // le rendu serait `23` collés (mathématiquement faux). Cf. brief
            // frère §5.ter.
            var ast = new Bin("*", false, true,  // tight=false (loose), implicit=true
                new Atom("number", "2"), new Atom("number", "3"));
            Assert.Equal("2\\times 3", LatexRenderer.Render(ast));
        }

        [Fact]
        public void Mult_setting_cdot_renders_with_cdot()
        {
            // Setting \cdot : a*b rendu `a\cdot b`.
            var prev = LatexRenderer.GlobalOptions.MultSymbol;
            LatexRenderer.GlobalOptions.MultSymbol = "\\cdot ";
            try
            {
                Assert.Equal("a\\cdot b", RenderTop("a*b"));
            }
            finally { LatexRenderer.GlobalOptions.MultSymbol = prev; }
        }

        // ------------------ ADR 30-04 dot-as-multiplier ------------------
        // `.` est un opérateur de multiplication, rendu TOUJOURS `\cdot`
        // (lecture littérale du point, indépendant du setting). Pour le décimal
        // anglo `3.4`, l'alt cascade RuleDecimalVsMultiplication propose `3{,}4`.

        [Fact]
        public void Dot_letter_letter_renders_cdot()
            => Assert.Equal("a\\cdot b", RenderTop("a.b"));

        [Fact]
        public void Dot_number_number_renders_cdot()
            // `3.4` → `3\cdot 4` par défaut. Cf. ADR Feat-dot-as-multiplier.
            => Assert.Equal("3\\cdot 4", RenderTop("3.4"));

        [Fact]
        public void Dot_number_letter_renders_cdot()
            => Assert.Equal("2\\cdot x", RenderTop("2.x"));

        [Fact]
        public void Dot_chain_left_assoc()
            => Assert.Equal("a\\cdot b\\cdot c", RenderTop("a.b.c"));

        [Fact]
        public void Dot_with_func_renders_cdot()
            => Assert.Equal(
                "\\cos\\left(x\\right)\\cdot \\sin\\left(x\\right)",
                RenderTop("cos(x).sin(x)"));

        [Fact]
        public void Dot_renders_cdot_independent_of_setting()
        {
            // Même avec setting \times, le `.` rend toujours \cdot.
            var prev = LatexRenderer.GlobalOptions.MultSymbol;
            LatexRenderer.GlobalOptions.MultSymbol = "\\times ";
            try
            {
                Assert.Equal("a\\cdot b", RenderTop("a.b"));
            }
            finally { LatexRenderer.GlobalOptions.MultSymbol = prev; }
        }

        [Fact]
        public void Dot_tightness_alignée_avec_asterisk()
            // `.` suit les mêmes règles tightness que `*` (cf. ADR
            // Feat-asterisk-tightness-associativity). `a.b/3` tight → \frac{a\cdot b}{3}.
            => Assert.Equal("\\frac{a\\cdot b}{3}", RenderTop("a.b/3"));

        [Fact]
        public void Dot_loose_right_recursive()
            // Espace avant `.` : loose → droite-récursive.
            // `a .b/3` → `a\cdot \frac{b}{3}`.
            => Assert.Equal("a\\cdot \\frac{b}{3}", RenderTop("a .b/3"));

        // ------------------ Diagnostic bug reporté 30-04 ------------------

        [Fact]
        public void Diagnostic_one_over_x_plus_2x2()
        {
            // Bug user 30-04 : `1/x+2x2` rendait `\frac{1}{x+2x^{2}}` (le `+2x²`
            // est absorbé au dénominateur). Avec ADR Feat-tight-implicit-mult-
            // grouping, default = PEMDAS : `\frac{1}{x} + 2x^{2}`.
            var result = RenderTop("1/x+2x2");
            // Loggé en clair au cas où ; le test pinne le PEMDAS.
            Assert.Equal("\\frac{1}{x}+2x^{2}", result);
        }

        [Fact]
        public void Diagnostic_g_of_x_equals_one_over_x_plus_2x2()
        {
            // Le cas exact de l'image utilisateur : `g(x)=1/x+2x2`.
            var result = RenderTop("g(x)=1/x+2x2");
            // Default attendu : g(x) = \frac{1}{x} + 2x^{2}
            Assert.DoesNotContain("\\frac{1}{x+", result);
            Assert.Contains("\\frac{1}{x}", result);
            Assert.Contains("2x^{2}", result);
        }

        // ============================================================
        // Brief 30-04 multiline-systems-equivalences — Phase 1 align*
        // ============================================================

        [Fact]
        public void MultiLine_equivalence_chain_renders_align_star()
        {
            // Chaîne d'équivalences : `2x+1=5\n<=> 2x=4\n<=> x=2` →
            // align* avec \Leftrightarrow en début de chaque ligne sauf la 1re,
            // et `&` qui aligne sur le `=` de chaque ligne.
            var result = RenderTop("2x+1=5\n<=> 2x=4\n<=> x=2");
            Assert.Contains("\\begin{align*}", result);
            Assert.Contains("\\end{align*}", result);
            Assert.Contains("\\Leftrightarrow", result);
            // Alignement sur `=` via `&`
            Assert.Contains("&=", result);
        }

        [Fact]
        public void MultiLine_equality_chain_renders_align_star_no_arrow()
        {
            // Chaîne d'égalités algébriques : `f(x)=2x+1\n= 2(x+0.5)` →
            // align* avec `&=` aligné, pas de \Leftrightarrow (chaîne `=` pure).
            var result = RenderTop("f(x)=2x+1\n= 2x");
            Assert.Contains("\\begin{align*}", result);
            Assert.DoesNotContain("\\Leftrightarrow", result);
            Assert.Contains("&=", result);
        }

        [Fact]
        public void MultiLine_implication_chain_renders_rightarrow()
        {
            // Implication : `x>0\n=> x^2>0` → align* avec \Rightarrow
            var result = RenderTop("x>0\n=> x^2>0");
            Assert.Contains("\\begin{align*}", result);
            Assert.Contains("\\Rightarrow", result);
        }

        [Fact]
        public void Single_line_no_marker_no_multilineblock()
        {
            // Pas de \n, pas de marqueur → pas de MultiLineBlock, AST normal
            var result = RenderTop("2x+1=5");
            Assert.DoesNotContain("\\begin{align*}", result);
            Assert.Equal("2x+1=5", result);
        }

        [Fact]
        public void Multiline_without_marker_falls_back_no_multilineblock()
        {
            // 2 lignes mais ligne 2 sans marqueur align → pas de MultiLineBlock
            // (Phase 1 = align uniquement, system Phase 2)
            var result = RenderTop("f(x)=1\ng(x)=2");
            Assert.DoesNotContain("\\begin{align*}", result);
        }

        [Fact]
        public void MultiLine_alignment_uses_two_ampersand_cols()
        {
            // Validation visuelle de l'alignement : préfixe à GAUCHE (colonne 1),
            // lhs au MILIEU (colonne 2), `=` aligné EN COLONNE (col 3).
            // Cf. brief 30-04 multiline-systems §2.1 + demande user 01-05
            // « il faut aligner à gauche les <=> et les = c'est nickel ».
            var result = RenderTop("2x+1=5\n<=> 2x=4");
            // Première ligne : col1 vide, col2 = `2x+1`, col3 = `= 5`
            Assert.Contains("& 2x+1 &= 5", result);
            // Seconde ligne : col1 = \Leftrightarrow, col2 = `2x`, col3 = `= 4`
            Assert.Contains("\\Leftrightarrow & 2x &= 4", result);
        }

        [Fact]
        public void Asterisk_tight_chain_left_assoc()
            // 1*2*3 tight : visuellement identique en gauche-assoc ou droite-assoc
            // (les parens transparentes pour times). On vérifie juste que ça parse.
            => Assert.Equal("1\\times 2\\times 3", RenderTop("1*2*3"));

        // ------------------ Ensembles canoniques (keyword bbR/bbN/etc.) ------------------

        [Fact]
        public void BbR_renders_mathbb_R()
            => Assert.Equal("\\mathbb{R}", RenderTop("bbR"));

        [Fact]
        public void BbN_renders_mathbb_N()
            => Assert.Equal("\\mathbb{N}", RenderTop("bbN"));

        [Fact]
        public void BbR_star_renders_with_exponent()
            // R* tight = réels non nuls
            => Assert.Equal("\\mathbb{R}^*", RenderTop("bbR*"));

        [Fact]
        public void BbR_plus_renders_with_exponent()
            => Assert.Equal("\\mathbb{R}^+", RenderTop("bbR+"));

        [Fact]
        public void BbR_minus_renders_with_exponent()
            // R- existe : réels négatifs
            => Assert.Equal("\\mathbb{R}^-", RenderTop("bbR-"));

        [Fact]
        public void BbR_star_plus_renders_strict_positive()
            // R*+ ou R+* = strictement positifs
            => Assert.Equal("\\mathbb{R}_+^*", RenderTop("bbR*+"));

        [Fact]
        public void BbR_plus_star_renders_strict_positive()
            => Assert.Equal("\\mathbb{R}_+^*", RenderTop("bbR+*"));

        [Fact]
        public void BbR_star_minus_renders_strict_negative()
            => Assert.Equal("\\mathbb{R}_-^*", RenderTop("bbR*-"));

        [Fact]
        public void Forall_x_in_bbR_via_pipeline_explicit_in()
            // forall x dans bbR → \forall x \in \mathbb{R}
            // Décomposition modulaire : keyword `dans` explicite.
            => Assert.Equal("\\forall x \\in \\mathbb{R}", RenderTop("forall x dans bbR"));

        // ------------------ ADR 29-04 implication / équivalence flèches ------------------

        [Fact]
        public void Implies_arrow_renders_Rightarrow()
            => Assert.Equal("A \\Rightarrow B", RenderTop("A => B"));

        [Fact]
        public void Iff_arrow_renders_Leftrightarrow()
            => Assert.Equal("P \\Leftrightarrow Q", RenderTop("P <=> Q"));

        [Fact]
        public void Long_iff_arrow_renders_Leftrightarrow()
            // <==> 4 chars : doit gagner sur <=> + = grâce au coût négatif greedy
            => Assert.Equal("A \\Leftrightarrow B", RenderTop("A <==> B"));

        [Fact]
        public void Double_implies_arrow_renders_Rightarrow()
            => Assert.Equal("A \\Rightarrow B", RenderTop("A ==> B"));

        [Fact]
        public void Left_arrow_renders_Leftarrow()
            => Assert.Equal("A \\Leftarrow B", RenderTop("A <== B"));

        [Fact]
        public void Unicode_implies_renders()
            => Assert.Equal("A \\Rightarrow B", RenderTop("A ⇒ B"));

        [Fact]
        public void Unicode_iff_renders()
            => Assert.Equal("A \\Leftrightarrow B", RenderTop("A ⇔ B"));

        // Anti-régression : <=, >= ne doivent PAS être cassés par l'ajout

        [Fact]
        public void Leq_unchanged_after_arrows()
            => Assert.Equal("x \\leq 5", RenderTop("x <= 5"));

        [Fact]
        public void Geq_unchanged_after_arrows()
            => Assert.Equal("x \\geq 5", RenderTop("x >= 5"));

        // ------------------ FuncDef (ADR 29-04 function-definition) ------------------

        [Fact]
        public void FuncDef_single_var_renders_with_mapsto()
            => Assert.Equal("f: x \\mapsto 2x+1", RenderTop("f:x->2x+1"));

        [Fact]
        public void FuncDef_multi_vars_renders_with_parens()
            => Assert.Equal("f: (x,y) \\mapsto x+y", RenderTop("f:x,y->x+y"));

        [Fact]
        public void FuncDef_with_cos_in_body()
            // \cos garde ses parens autour de l'arg (rendu standard LaTeX)
            => Assert.Equal("g: t \\mapsto \\cos\\left(t\\right)+1", RenderTop("g:t->cos(t)+1"));

        [Fact]
        public void FuncDef_with_space_before_colon()
            // f :x->x+1 (espace avant :) doit aussi marcher (les espaces sont
            // filtrés par le parser). Régression : voir logs où top="" pour
            // "f :x->x+1" alors que "f:x->x+1" rend correctement.
            => Assert.Equal("f: x \\mapsto x+1", RenderTop("f :x->x+1"));
    }
}
