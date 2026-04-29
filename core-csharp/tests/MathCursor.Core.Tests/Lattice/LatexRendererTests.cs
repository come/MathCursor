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
    public sealed class LatexRendererTests
    {
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
        public void Bin_explicit_mult_uses_cdot()
        {
            // 2*x explicite → 2\cdot x
            var ast = new Bin("*", false, false, new Atom("number", "2"), new Atom("ident", "x"));
            Assert.Equal("2\\cdot x", LatexRenderer.Render(ast));
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
            => Assert.Equal("[0,1] \\cap [0.5,2]", RenderTop("[0,1] inter [0.5,2]"));

        [Fact]
        public void Forall_with_union_set_explicit_in()
            // forall x dans [0,1]U[2,3] → \forall x \in [0,1] \cup [2,3]
            // (avec keyword `dans` explicite, cf. décomposition modulaire 29-04)
            => Assert.Equal("\\forall x \\in [0,1] \\cup [2,3]", RenderTop("forall x dans [0,1]U[2,3]"));

        // ------------------ ADR 29-04 tight-as-grouping pour / ------------------

        [Fact]
        public void AB_slash_DC_tight_renders_as_fraction_block()
            // AB/DC tight : rhs absorbe DC en bloc → \frac{AB}{DC}
            => Assert.Equal("\\frac{AB}{DC}", RenderTop("AB/DC"));

        [Fact]
        public void AB_slash_BC_tight_renders_as_fraction_block()
            // Cas explicitement listé dans l'ADR
            => Assert.Equal("\\frac{AB}{BC}", RenderTop("AB/BC"));

        [Fact]
        public void Slash_tight_with_plus_collé_groups_rhs()
            // 1/x+1 collé : rhs absorbe (x+1) → \frac{1}{x+1}
            => Assert.Equal("\\frac{1}{x+1}", RenderTop("1/x+1"));

        [Fact]
        public void Slash_with_space_before_plus_does_not_group_rhs()
            // 1/x +1 (espace) : rhs = juste x, le +1 reste hors fraction
            => Assert.Equal("\\frac{1}{x}+1", RenderTop("1/x +1"));

        [Fact]
        public void Simple_slash_unchanged()
            // 1/x simple : non-régression, \frac{1}{x}
            => Assert.Equal("\\frac{1}{x}", RenderTop("1/x"));

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
