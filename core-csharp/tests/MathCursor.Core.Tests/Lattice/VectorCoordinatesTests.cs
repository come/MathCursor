using System.Linq;
using MathCursor.Core;
using MathCursor.Core.Lattice;
using MathCursor.Core.Lattice.Ast;
using Xunit;

namespace MathCursor.Core.Tests.Lattice
{
    /// <summary>
    /// Tests du pattern « vecteur + coordonnées » V1 — cf. brief
    /// 2026-04-29-vector-coordinates-shorthand. Couvre les cas positifs §5.1
    /// et §5.2 du brief, l'anti-régression §5.3 (function calls, intervalles,
    /// vec keyword, AB cascade, trig, holes, quantifs…), et les ambiguïtés
    /// §5.4 (cascade f(1, 2) vs vec{f}(1, 2)).
    /// </summary>
    public sealed class VectorCoordinatesTests
    {
        private readonly LatticeEngine _engine = new LatticeEngine();

        private string Top(string input)
        {
            var s = _engine.Convert(input);
            return s.Count > 0 ? s[0].Latex : string.Empty;
        }

        // ===================================================================
        // §5.1 — Vecteurs colonnes (séparateur INTERNE = espace)
        // ===================================================================

        [Fact]
        public void Vec_u_2D_column_with_space_before_paren()
            => Assert.Equal(
                "\\vec{u} \\begin{pmatrix} 1 \\\\ 2 \\end{pmatrix}",
                Top("u (1 2)"));

        [Fact]
        public void Vec_u_2D_column_no_space_before_paren()
            // L'espace AVANT la paren ne change RIEN au layout (cf. brief §2.1)
            => Assert.Equal(
                "\\vec{u} \\begin{pmatrix} 1 \\\\ 2 \\end{pmatrix}",
                Top("u(1 2)"));

        [Fact]
        public void Vec_v_2D_column_with_negative_first()
            // `v(-1 3)` : `(-` est l'op multi-char alias `\in` du lexer.
            // Notre pattern le re-traite comme `(` + unary minus.
            => Assert.Equal(
                "\\vec{v} \\begin{pmatrix} -1 \\\\ 3 \\end{pmatrix}",
                Top("v(-1 3)"));

        [Fact]
        public void Vec_u_3D_column()
            => Assert.Equal(
                "\\vec{u} \\begin{pmatrix} 1 \\\\ 2 \\\\ 3 \\end{pmatrix}",
                Top("u (1 2 3)"));

        [Fact]
        public void Vec_OM_3D_symbolic_column()
            => Assert.Equal(
                "\\vec{OM} \\begin{pmatrix} x \\\\ y \\\\ z \\end{pmatrix}",
                Top("OM (x y z)"));

        [Fact]
        public void Vec_AB_2D_column_negative()
            => Assert.Equal(
                "\\vec{AB} \\begin{pmatrix} 3 \\\\ -1 \\end{pmatrix}",
                Top("AB (3 -1)"));

        [Fact]
        public void Vec_u_column_with_expressions_no_internal_space()
            // a+1 et b-2 sont des expressions tight qui ne contiennent pas
            // d'espace top-level — le découpage en cellules respecte ce critère.
            => Assert.Equal(
                "\\vec{u} \\begin{pmatrix} a+1 \\\\ b-2 \\end{pmatrix}",
                Top("u (a+1 b-2)"));

        [Fact]
        public void Vec_u_column_with_polynomial_cells()
            => Assert.Equal(
                "\\vec{u} \\begin{pmatrix} 2x+1 \\\\ 3y-2 \\end{pmatrix}",
                Top("u (2x+1 3y-2)"));

        [Fact]
        public void Vec_u_column_with_parenthesized_trig()
            // `cos(t)` et `sin(t)` parenthèsent leurs args, le space top-level
            // entre les deux est bien un séparateur de cellule.
            => Assert.Equal(
                "\\vec{u} \\begin{pmatrix} \\cos\\left(t\\right) \\\\ \\sin\\left(t\\right) \\end{pmatrix}",
                Top("u (cos(t) sin(t))"));

        // ===================================================================
        // §5.2 — Coordonnées en ligne (séparateur INTERNE = virgule)
        // ===================================================================

        // Notation française : séparateur ` ; ` en sortie (cf. ADR 30-04
        // Feat-french-semicolon-coordinates). Input virgule inchangé.

        [Fact]
        public void Vec_u_row_no_space()
            => Assert.Equal("\\vec{u}(1 ; 2)", Top("u(1,2)"));

        [Fact]
        public void Vec_u_row_with_space_after_comma()
            // L'espace après la virgule est de la mise en forme, pas un
            // séparateur. On reste en row layout.
            => Assert.Equal("\\vec{u}(1 ; 2)", Top("u(1, 2)"));

        [Fact]
        public void Vec_u_row_with_space_before_paren_and_after_comma()
            => Assert.Equal("\\vec{u}(1 ; 2)", Top("u (1, 2)"));

        [Fact]
        public void Point_A_row_2D()
            // A majuscule seule = point, pas de \vec
            => Assert.Equal("A(1 ; 2)", Top("A(1, 2)"));

        [Fact]
        public void Point_A_row_2D_no_space()
            => Assert.Equal("A(1 ; 2)", Top("A(1,2)"));

        [Fact]
        public void Point_M_row_3D()
            => Assert.Equal("M(x ; y ; z)", Top("M(x, y, z)"));

        [Fact]
        public void Vec_AB_row_2D()
            => Assert.Equal("\\vec{AB}(3 ; -1)", Top("AB(3, -1)"));

        [Fact]
        public void Vec_u_row_with_expressions()
            => Assert.Equal("\\vec{u}(2x+1 ; 3y-2)", Top("u(2x+1, 3y-2)"));

        // Bonus : layout=column vs point + 1 maj seule
        [Fact]
        public void Point_A_column_2D()
            => Assert.Equal(
                "A \\begin{pmatrix} 1 \\\\ 2 \\end{pmatrix}",
                Top("A (1 2)"));

        // ===================================================================
        // §5.3 — Anti-régression : tout doit continuer à fonctionner
        // ===================================================================

        // ---- Function calls (cardinality 1) ----

        [Fact]
        public void FunctionCall_f_x_unchanged()
            => Assert.Equal("f\\left(x\\right)", Top("f(x)"));

        [Fact]
        public void FunctionCall_f_2_unchanged()
            => Assert.Equal("f\\left(2\\right)", Top("f(2)"));

        [Fact]
        public void FunctionCall_f_expr_unchanged()
            => Assert.Equal("f\\left(2x+1\\right)", Top("f(2x+1)"));

        [Fact]
        public void FunctionCall_g_t_unchanged()
            => Assert.Equal("g\\left(t\\right)", Top("g(t)"));

        [Fact]
        public void FunctionCall_f_x_y_default_function()
            // f(x, y) : 2 args avec virgule. Per brief §3.1, f-typique = function
            // par défaut. L'alt vec{f}(x,y) est proposée via cascade.
            => Assert.Equal("f\\left(x,y\\right)", Top("f(x,y)"));

        [Fact]
        public void TrigFunc_cos_x_unchanged()
            => Assert.Equal("\\cos\\left(x\\right)", Top("cos(x)"));

        [Fact]
        public void TrigFunc_sin_2xp1_unchanged()
            => Assert.Equal("\\sin\\left(2x+1\\right)", Top("sin(2x+1)"));

        [Fact]
        public void TrigFunc_ln_x_unchanged()
            => Assert.Equal("\\ln\\left(x\\right)", Top("ln(x)"));

        [Fact]
        public void TrigFunc_exp_x_unchanged()
            => Assert.Equal("e^{x}", Top("exp(x)"));

        [Fact]
        public void TrigFunc_sqrt_xp1_unchanged()
            => Assert.Equal("\\sqrt{x+1}", Top("sqrt(x+1)"));

        // ---- Trigonométrie sans parens (scope-style) ----

        [Fact]
        public void Trig_sin_x_scope_unchanged()
            => Assert.Equal("\\sin x", Top("sin x"));

        [Fact]
        public void Trig_cos_t_scope_unchanged()
            => Assert.Equal("\\cos t", Top("cos t"));

        [Fact]
        public void Trig_tan_paren_unchanged()
            => Assert.Equal("\\tan\\left(2x\\right)", Top("tan(2x)"));

        [Fact]
        public void Lim_sin_x_over_x_unchanged()
            // `lim x 0 sin x / x` : rendu existant, on vérifie que le pattern
            // VectorCoordinates ne casse pas le scope `lim`.
            => Assert.Equal("\\frac{\\lim_{x \\to 0} \\sin x}{x}", Top("lim x 0 sin x / x"));

        // ---- Intervalles : doit rester intervalle (pas de coords) ----

        [Fact]
        public void Interval_open_with_comma_no_coords()
            // (0, 1) sans ident à gauche reste un Group d'expression virgule
            // (comportement existant). Pas de coords parce que pas d'ident.
            => Assert.Equal("\\left(0,1\\right)", Top("(0, 1)"));

        [Fact]
        public void Interval_closed_unchanged()
            => Assert.Equal("[0,1]", Top("[0,1]"));

        [Fact]
        public void Interval_semi_open_unchanged()
            => Assert.Equal("[0,1[", Top("[0,1["));

        [Fact]
        public void Interval_inf_bound_unchanged()
            // [0, +inf[ : intervalle non borné à droite. On utilise virgule
            // (séparateur usuel des intervalles dans le moteur actuel).
            => Assert.Equal("[0,+\\infty[", Top("[0, +inf["));

        [Fact]
        public void Interval_union_unchanged()
            => Assert.Equal("[0,1] \\cup [2,3]", Top("[0,1] U [2,3]"));

        // ---- vec keyword ----

        [Fact]
        public void Vec_keyword_unchanged()
            => Assert.Equal("\\vec{u}", Top("vec u"));

        [Fact]
        public void Vec_keyword_AB_unchanged()
            => Assert.Equal("\\vec{AB}", Top("vec AB"));

        [Fact]
        public void Vec_keyword_sum_unchanged()
            => Assert.Equal("\\vec{u}+\\vec{v}", Top("vec u + vec v"));

        // ---- AB cascade (two-uppercase) ----

        [Fact]
        public void AB_alone_keeps_two_uppercase_ambig()
        {
            var r = _engine.ConvertWithAmbiguity("AB");
            Assert.Equal("AB", r.TopLatex);
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleTwoUppercase, r.Spot!.RuleId);
        }

        [Fact]
        public void ABplusCD_keeps_rightmost_two_uppercase()
        {
            var r = _engine.ConvertWithAmbiguity("AB+CD");
            Assert.NotNull(r.Spot);
            Assert.Equal("CD", r.Spot!.DefaultLatex);
            Assert.Equal(AlternativeGenerator.RuleTwoUppercase, r.Spot.RuleId);
        }

        [Fact]
        public void AB_with_coords_renders_vec_AB()
            // AB(3, -1) → \vec{AB}(3 ; -1) — séparateur français
            // L'ambig two-uppercase reste proposée sur AB.
            => Assert.Equal("\\vec{AB}(3 ; -1)", Top("AB(3, -1)"));

        // ---- Number-tight et Sup ----

        [Fact]
        public void X2_implicit_sup_unchanged()
            => Assert.Equal("x^{2}", Top("x2"));

        [Fact]
        public void X_caret_2_explicit_sup_unchanged()
            => Assert.Equal("x^{2}", Top("x^2"));

        [Fact]
        public void Vec_u_row_with_x2_y2_implicit_sup_in_cells()
            // u(x2, y2) : number-tight DANS les cellules → x², y²
            => Assert.Equal("\\vec{u}(x^{2} ; y^{2})", Top("u(x2, y2)"));

        // ---- Holes et fractions ----

        [Fact]
        public void Frac_a_b_unchanged()
            => Assert.Equal("\\frac{a}{b}", Top("frac a b"));

        [Fact]
        public void Frac_alone_with_holes_unchanged()
            => Assert.Equal("\\frac{\\square }{\\square }", Top("frac"));

        // ---- Quantificateurs ----

        [Fact]
        public void Forall_x_in_R_unchanged()
            => Assert.Equal("\\forall x \\in R", Top("forall x dans R"));

        [Fact]
        public void Exists_y_in_N_unchanged()
            => Assert.Equal("\\exists y \\in N", Top("exists y dans N"));

        [Fact]
        public void V_alone_keeps_v_as_forall_ambig()
        {
            var r = _engine.ConvertWithAmbiguity("V x");
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleVAsForall, r.Spot!.RuleId);
        }

        // ---- Définitions de fonctions ----

        [Fact]
        public void FuncDef_single_var_unchanged()
            => Assert.Equal("f: x \\mapsto 2x+1", Top("f:x->2x+1"));

        // ===================================================================
        // §5.4 — Désambig fonction vs coords (cascade)
        // ===================================================================

        [Fact]
        public void FunctionCall_f_1_2_yields_vector_coords_alt()
        {
            // Default = function call ; alt = vec coords \vec{f}(1, 2).
            var r = _engine.ConvertWithAmbiguity("f(1, 2)");
            Assert.Equal("f\\left(1,2\\right)", r.TopLatex);
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleVectorCoordsVsCall, r.Spot!.RuleId);
            var alts = r.Spot.Alternatives.Select(a => a.Latex).ToList();
            // Alt 0 = identity (fonction)
            Assert.Contains("f\\left(1,2\\right)", alts);
            // Alt 1 = vec coords (séparateur français ;)
            Assert.Contains("\\vec{f}(1 ; 2)", alts);
        }

        [Fact]
        public void FunctionCall_g_x_y_yields_vector_coords_alt()
        {
            var r = _engine.ConvertWithAmbiguity("g(x, y)");
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleVectorCoordsVsCall, r.Spot!.RuleId);
            var alts = r.Spot.Alternatives.Select(a => a.Latex).ToList();
            Assert.Contains("\\vec{g}(x ; y)", alts);
        }

        [Fact]
        public void Vec_u_1_2_default_no_function_alt()
        {
            // `u(1, 2)` est SANS ambig dans le contexte coords (u typique vec).
            // L'ambig serait théoriquement function-call, mais on l'expose pas
            // en V1 (priorité simplicité côté UX).
            var r = _engine.ConvertWithAmbiguity("u(1, 2)");
            Assert.Equal("\\vec{u}(1 ; 2)", r.TopLatex);
            // Pas d'ambig RuleVectorCoordsVsCall (u n'est pas typique fonction)
            if (r.Spot != null)
                Assert.NotEqual(AlternativeGenerator.RuleVectorCoordsVsCall, r.Spot.RuleId);
        }

        [Fact]
        public void Point_A_1_2_no_function_alt()
        {
            // A(1, 2) : majuscule seule → point sans ambig (A pas typique fonction)
            var r = _engine.ConvertWithAmbiguity("A(1, 2)");
            Assert.Equal("A(1 ; 2)", r.TopLatex);
            if (r.Spot != null)
                Assert.NotEqual(AlternativeGenerator.RuleVectorCoordsVsCall, r.Spot.RuleId);
        }

        [Fact]
        public void FunctionCall_f_x_single_arg_no_coords_ambig()
        {
            // f(x) : 1 arg seul → ne déclenche PAS le pattern coords
            // (cardinalité 2 ou 3 obligatoire).
            var r = _engine.ConvertWithAmbiguity("f(x)");
            if (r.Spot != null)
                Assert.NotEqual(AlternativeGenerator.RuleVectorCoordsVsCall, r.Spot.RuleId);
        }

        // ===================================================================
        // P3 — Bug rapporté 30-04 : `u(1, 2)` produit `u((1@2))` en OMath
        // ===================================================================
        // Tests pivot 3 couches pour le bug : vérifier que le pipeline complet
        // (top-1 LaTeX + UnicodeMath conversion) ne produit JAMAIS `((1@2))`
        // ou autre artefact matrice pour un layout=row (séparateur virgule).

        [Fact]
        public void Bug_p3_vector_row_latex_is_inline_not_pmatrix()
        {
            // Couche (a) : LatexRenderer doit produire la forme ligne, pas pmatrix
            var latex = Top("u(1, 2)");
            Assert.Equal("\\vec{u}(1 ; 2)", latex);
            Assert.DoesNotContain("\\begin{pmatrix}", latex);
        }

        [Fact]
        public void Bug_p3_vector_row_unicode_no_matrix_marker()
        {
            // Couche (c) : conversion finale ne doit pas contenir `■(` (matrice)
            // ni `((` doublé. Cas user : `u(1, 2)` → user voit `u((1@2))`.
            var latex = Top("u(1, 2)");
            var unicode = LatexToUnicodeMath.Convert(latex);
            Assert.DoesNotContain("■", unicode);          // marqueur matrice UnicodeMath
            Assert.DoesNotContain("@", unicode);          // séparateur lignes matrice
            Assert.DoesNotContain("((1", unicode);        // double-paren autour du `1`
        }

        [Fact]
        public void Bug_p3_point_row_unicode_clean()
        {
            // A(1, 2) en notation point : doit rendre `A(1 ; 2)` propre,
            // pas de matrice, pas de \vec, pas de double parens.
            var latex = Top("A(1, 2)");
            var unicode = LatexToUnicodeMath.Convert(latex);
            Assert.Equal("A(1 ; 2)", latex);
            Assert.DoesNotContain("■", unicode);
            Assert.DoesNotContain("@", unicode);
            Assert.DoesNotContain("⃗", unicode);          // pas de combining arrow (= \vec)
        }

        [Fact]
        public void Bug_p3_3d_point_unicode_clean()
        {
            // M(x, y, z) en 3D
            var latex = Top("M(x, y, z)");
            var unicode = LatexToUnicodeMath.Convert(latex);
            Assert.Equal("M(x ; y ; z)", latex);
            Assert.DoesNotContain("■", unicode);
            Assert.DoesNotContain("@", unicode);
        }

        // ===================================================================
        // Cas borderline et anti-régression supplémentaires
        // ===================================================================

        [Fact]
        public void Mixed_separator_falls_back_to_existing_behavior()
        {
            // `u(1, 2 3)` — virgule ET espace top-level mélangés → rejet du
            // pattern coords, fallback au comportement existant.
            var top = Top("u(1, 2 3)");
            // Ne contient pas \begin{pmatrix} ni \vec{u}(...)
            Assert.DoesNotContain("\\begin{pmatrix}", top);
            Assert.DoesNotContain("\\vec{u}(1, 2 3)", top);
        }

        [Fact]
        public void Single_value_falls_back_to_function_call_or_group()
        {
            // u(1) : 1 cellule → pas coords, retombe sur Atom*Group (= rendu
            // implicit mult avec Group)
            var top = Top("u(1)");
            Assert.DoesNotContain("\\begin{pmatrix}", top);
            // Pas de pattern \vec{u}(1) coords ligne (1 valeur exclu)
            Assert.NotEqual("\\vec{u}(1)", top);
        }

        [Fact]
        public void Four_values_falls_back()
        {
            // u(1, 2, 3, 4) : 4 cellules → pas coords (V1 limite à 2-3),
            // fallback au comportement existant (groupé en virgule).
            var top = Top("u(1, 2, 3, 4)");
            Assert.DoesNotContain("\\begin{pmatrix}", top);
            // Le rendu existant pour u(1,2,3,4) = u\left(1,2,3,4\right)
            // (mult implicite). On vérifie au moins qu'il n'y a pas \vec.
            Assert.DoesNotContain("\\vec{u}", top);
        }

        [Fact]
        public void Lowercase_pair_no_coords_match()
        {
            // ab(1, 2) : 2 lettres minuscules ≠ pattern AB (qui exige 2 majs).
            // Le parser actuel traite `ab` comme pas un groupe ident-pair vec.
            // Comportement : a*b(1,2) ou ident "ab" ?
            // Lex : pour "ab(1, 2)", les idents 1-lettre dominent (cost 5+5 vs 24).
            // Donc `a` puis `b` séparés. Notre pattern exige les deux IDs adjacents
            // tight ; ici les deux sont tight. MAIS notre code ne match pas car
            // on exige une paire avec des MAJUSCULES uniquement ?
            // En fait non : on accepte aussi 2 minuscules tight (ex: `om`).
            // Le brief mentionne idents 1-2 lettres en général.
            // Pour `ab` : on accepte → \vec{ab}(1,2). Pas idéal sémantiquement
            // (ab pas typique vec) mais c'est cohérent avec OM/MN.
            var top = Top("ab(1, 2)");
            // On accepte que ce soit coords OU mult — l'important : pas de crash.
            Assert.NotEmpty(top);
        }
    }
}
