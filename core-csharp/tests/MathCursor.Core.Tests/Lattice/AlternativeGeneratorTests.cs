using System.Collections.Generic;
using System.Linq;
using MathCursor.Core.Lattice;
using MathCursor.Core.Lattice.Ast;
using Xunit;

namespace MathCursor.Core.Tests.Lattice
{
    [Collection(GlobalOptionsTestCollection.Name)]
    public sealed class AlternativeGeneratorTests : System.IDisposable
    {
        private readonly string _savedMultSymbol;

        public AlternativeGeneratorTests()
        {
            // Force le symbole de mult à `\times` (FR default) pour
            // déterminisme cross-culture. Cf. LatexRendererTests pour le
            // même pattern.
            _savedMultSymbol = LatexRenderer.GlobalOptions.MultSymbol;
            LatexRenderer.GlobalOptions.MultSymbol = "\\times ";
        }

        public void Dispose()
        {
            LatexRenderer.GlobalOptions.MultSymbol = _savedMultSymbol;
        }

        // Helper : projette les alternatives en liste de Latex pour simplifier
        // les Assert.Contains. Le refactor en `AmbiguityAlternative` ajoute
        // une indirection sur .Latex, ce helper la masque dans les tests.
        private static IReadOnlyList<string> Lat(IReadOnlyList<AmbiguityAlternative> alts)
            => alts.Select(a => a.Latex).ToList();

        [Fact]
        public void Null_ast_returns_empty()
            => Assert.Empty(AlternativeGenerator.Generate(null));

        // ---- Pattern AB : 2 majuscules adjacentes en mult implicite ----

        // Note : les tests AB / ABC passent par ConvertWithAmbiguity car les
        // règles "séquence de majuscules" sont scannées sur le topLatex rendu
        // (ScanUppercaseSequences), pas sur l'AST. Construire un AST à la main
        // et appeler Generate ne déclenche plus ces règles.

        [Fact]
        public void Two_uppercase_letters_yields_vec_droite_segment_via_engine()
        {
            var r = _engine.ConvertWithAmbiguity("AB");
            Assert.NotNull(r.Spot);
            var alts = Lat(r.Spot!.Alternatives);
            Assert.Equal(3, alts.Count);
            Assert.Contains("\\vec{AB}", alts);
            Assert.Contains("\\left(AB\\right)", alts);
            Assert.Contains("\\left[AB\\right]", alts);
        }

        [Fact]
        public void Two_lowercase_letters_yields_no_alternatives_via_engine()
        {
            var r = _engine.ConvertWithAmbiguity("ab");
            Assert.Null(r.Spot);
        }

        // ---- Pattern x2 → Sup → indice alternatif ----

        [Fact]
        public void Sup_implicit_letter_number_yields_subscript_alternative()
        {
            // Sup IMPLICITE (issu de la règle Number-tight, ex: x2) → ambig x_2
            var ast = new Sup(new Atom("ident", "x"), new Atom("number", "2"), isImplicit: true);
            var alts = AlternativeGenerator.Generate(ast);
            Assert.Single(alts);
            Assert.Equal("x_{2}", alts[0]);
        }

        [Fact]
        public void Sup_explicit_no_ambig()
        {
            // Sup EXPLICIT (^ tapé par l'utilisateur, ex: x^2) → PAS d'ambig.
            // L'utilisateur a déjà tranché en mettant le ^.
            var ast = new Sup(new Atom("ident", "x"), new Atom("number", "2"), isImplicit: false);
            Assert.Empty(AlternativeGenerator.Generate(ast));
        }

        [Fact]
        public void Sup_multichar_letter_no_alternative()
        {
            // "abc²" n'a pas de version en indice (pas une variable indexée)
            var ast = new Sup(new Atom("ident", "abc"), new Atom("number", "2"), isImplicit: true);
            Assert.Empty(AlternativeGenerator.Generate(ast));
        }

        [Fact]
        public void Sup_letter_non_number_no_alternative()
        {
            // x^n n'est pas un cas indice (l'exposant n'est pas un chiffre)
            var ast = new Sup(new Atom("ident", "x"), new Atom("ident", "n"), isImplicit: true);
            Assert.Empty(AlternativeGenerator.Generate(ast));
        }

        // ---- Intégration avec LatticeEngine ----

        [Fact]
        public void Engine_returns_AB_with_3_alternatives_via_ambiguity()
        {
            // L'API legacy `Convert` (IReadOnlyList<LatexSuggestion>) ne reçoit
            // plus les alts AB depuis que la détection est string-based dans
            // ConvertWithAmbiguity. Test équivalent via la nouvelle API.
            var engine = new MathCursor.Core.LatticeEngine();
            var r = engine.ConvertWithAmbiguity("AB");
            Assert.NotNull(r.Spot);
            Assert.Equal("AB", r.TopLatex);
            Assert.Equal(3, Lat(r.Spot!.Alternatives).Count);
            Assert.Contains("\\vec{AB}", Lat(r.Spot.Alternatives));
            Assert.Contains("\\left(AB\\right)", Lat(r.Spot.Alternatives));
            Assert.Contains("\\left[AB\\right]", Lat(r.Spot.Alternatives));
        }

        [Fact]
        public void Engine_returns_x2_with_subscript_alternative_via_ambiguity()
        {
            // x2 = Sup IMPLICIT (Number-tight) → ambig x_2 proposée
            var engine = new MathCursor.Core.LatticeEngine();
            var r = engine.ConvertWithAmbiguity("x2");
            Assert.Equal("x^{2}", r.TopLatex);
            Assert.NotNull(r.Spot);
            Assert.Contains("x_{2}", Lat(r.Spot!.Alternatives));
        }

        [Fact]
        public void Engine_x_caret_2_explicit_no_ambiguity()
        {
            // x^2 = Sup EXPLICIT → PAS d'ambig (régression user 2026-04-28).
            var engine = new MathCursor.Core.LatticeEngine();
            var r = engine.ConvertWithAmbiguity("x^2");
            Assert.Equal("x^{2}", r.TopLatex);
            Assert.Null(r.Spot);
        }

        // ---- FindRightmost : ambiguïté la plus à droite (phase 5b2) ----

        private static MathCursor.Core.LatticeEngine _engine = new MathCursor.Core.LatticeEngine();

        [Fact]
        public void Rightmost_returns_only_rightmost_when_two_ambiguities()
        {
            // "AB+CD" : 2 ambiguïtés, on garde CD (la plus à droite, plus
            // proche du caret). AB est validé mou par le `+` qui suit.
            var r = _engine.ConvertWithAmbiguity("AB+CD");
            Assert.NotNull(r.Spot);
            Assert.Equal("CD", r.Spot!.DefaultLatex);
            Assert.Equal(3, r.SpotStart);
            Assert.Equal(5, r.SpotEnd);
            Assert.Contains("\\vec{CD}", Lat(r.Spot.Alternatives));
        }

        [Fact]
        public void Rightmost_finds_ambiguity_in_subexpression()
        {
            // "f(x2)" : ambiguïté en sous-AST (x²)
            var r = _engine.ConvertWithAmbiguity("f(x2)");
            Assert.NotNull(r.Spot);
            Assert.Equal("x^{2}", r.Spot!.DefaultLatex);
            Assert.Contains("x_{2}", Lat(r.Spot.Alternatives));
        }

        [Fact]
        public void Rightmost_no_ambiguity_returns_null_spot()
        {
            var r = _engine.ConvertWithAmbiguity("ab");
            Assert.Null(r.Spot);
            Assert.Equal("ab", r.TopLatex);
        }

        [Fact]
        public void Rightmost_position_allows_recompose()
        {
            // Vérifie que TopLatex.Substring(0, start) + alt + TopLatex.Substring(end)
            // produit bien la formule recomposée
            var r = _engine.ConvertWithAmbiguity("f(x2)");
            Assert.NotNull(r.Spot);
            string recomposed = r.TopLatex.Substring(0, r.SpotStart!.Value)
                + "x_{2}"
                + r.TopLatex.Substring(r.SpotEnd!.Value);
            Assert.Equal("f\\left(x_{2}\\right)", recomposed);
        }

        [Fact]
        public void Empty_input_yields_empty_result()
        {
            var r = _engine.ConvertWithAmbiguity("");
            Assert.Equal("", r.TopLatex);
            Assert.Null(r.Spot);
        }

        // ========================================================
        //   Cas d'ambiguïté de bout en bout (regression suite)
        //   Une ligne par cas user pour ne plus casser à chaque
        //   refactor du moteur ou du générateur d'alternatives.
        // ========================================================

        // ---- AB (deux majuscules) ----

        [Fact]
        public void AB_yields_vec_droite_segment()
        {
            var r = _engine.ConvertWithAmbiguity("AB");
            Assert.Equal("AB", r.TopLatex);
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleTwoUppercase, r.Spot!.RuleId);
            Assert.Contains("\\vec{AB}", Lat(r.Spot.Alternatives));
            Assert.Contains("\\left(AB\\right)", Lat(r.Spot.Alternatives));
            Assert.Contains("\\left[AB\\right]", Lat(r.Spot.Alternatives));
        }

        [Fact]
        public void Lowercase_pair_yields_no_ambig()
        {
            var r = _engine.ConvertWithAmbiguity("ab");
            Assert.Equal("ab", r.TopLatex);
            Assert.Null(r.Spot);
        }

        [Fact]
        public void AB_in_subexpression_detected()
        {
            // f(AB) doit proposer l'ambig sur AB en sous-AST
            var r = _engine.ConvertWithAmbiguity("f(AB)");
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleTwoUppercase, r.Spot!.RuleId);
            Assert.Equal("AB", r.Spot.DefaultLatex);
        }

        // ---- ABC (trois majuscules) — règle plus large prioritaire ----

        [Fact]
        public void ABC_yields_widehat_and_triangle_not_partial_AB()
        {
            // Régression : ABC ne doit PAS proposer l'ambig partielle sur AB
            // (pattern le plus large prioritaire dans TraverseRightmost).
            var r = _engine.ConvertWithAmbiguity("ABC");
            Assert.Equal("ABC", r.TopLatex);
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleThreeUppercase, r.Spot!.RuleId);
            Assert.Equal("ABC", r.Spot.DefaultLatex);
            Assert.Contains("\\widehat{ABC}", Lat(r.Spot.Alternatives));
            Assert.Contains("\\triangle ABC", Lat(r.Spot.Alternatives));
            // Et SURTOUT pas d'ambig two-uppercase sur AB
            Assert.DoesNotContain("\\vec{AB}", Lat(r.Spot.Alternatives));
        }

        [Fact]
        public void ABC_in_subexpression_detected()
        {
            var r = _engine.ConvertWithAmbiguity("f(ABC)");
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleThreeUppercase, r.Spot!.RuleId);
            Assert.Equal("ABC", r.Spot.DefaultLatex);
        }

        // ---- Rightmost rule ----

        [Fact]
        public void Two_pairs_keep_only_rightmost()
        {
            var r = _engine.ConvertWithAmbiguity("AB+CD");
            Assert.NotNull(r.Spot);
            Assert.Equal("CD", r.Spot!.DefaultLatex);
            Assert.Equal(AlternativeGenerator.RuleTwoUppercase, r.Spot.RuleId);
        }

        [Fact]
        public void Two_triplets_keep_only_rightmost()
        {
            var r = _engine.ConvertWithAmbiguity("ABC+DEF");
            Assert.NotNull(r.Spot);
            Assert.Equal("DEF", r.Spot!.DefaultLatex);
            Assert.Equal(AlternativeGenerator.RuleThreeUppercase, r.Spot.RuleId);
        }

        // ---- x2 / x_2 / x² ----

        [Fact]
        public void X2_yields_subscript_alternative()
        {
            var r = _engine.ConvertWithAmbiguity("x2");
            Assert.Equal("x^{2}", r.TopLatex);
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleLetterSupNumber, r.Spot!.RuleId);
            Assert.Contains("x_{2}", Lat(r.Spot.Alternatives));
        }

        [Fact]
        public void Unicode_superscript_is_explicit_no_ambig()
        {
            // x² (Unicode super-2) = EXPLICITE typographique. Préprocess en
            // x^2 → Sup explicit → PAS d'ambig avec x_2 (cf. user 2026-04-28).
            var r = _engine.ConvertWithAmbiguity("x²");
            Assert.Equal("x^{2}", r.TopLatex);
            Assert.Null(r.Spot);
        }

        [Fact]
        public void X2_in_subexpression_detected()
        {
            var r = _engine.ConvertWithAmbiguity("f(x2)");
            Assert.Equal("f\\left(x^{2}\\right)", r.TopLatex);
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleLetterSupNumber, r.Spot!.RuleId);
        }

        // ---- V → forall / racine (3 alts : V identity / ∀ / √) ----

        [Fact]
        public void V_yields_three_alternatives()
        {
            // V suivi d'espace → 3 alts : V identity (no mutation), ∀ (mutation
            // V→forall), √ (mutation V→racine). L'utilisateur choisit dans la popup.
            // Source sans R/N/Z/Q/C à droite pour que le rightmost spot reste V
            // (sinon canonical-set sur R écrase, cf. ADR canonical sets).
            var r = _engine.ConvertWithAmbiguity("V x y");
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleVAsForall, r.Spot!.RuleId);
            Assert.Equal(3, r.Spot.Alternatives.Count);

            // Alt 0 : V identity (pas de mutation)
            Assert.Null(r.Spot.Alternatives[0].Mutation);

            // Alt 1 : ∀ (mutation V → forall)
            var forallAlt = r.Spot.Alternatives[1];
            Assert.NotNull(forallAlt.Mutation);
            Assert.Equal(0, forallAlt.Mutation!.Offset);
            Assert.Equal(1, forallAlt.Mutation.Length);
            Assert.Equal("forall", forallAlt.Mutation.Replacement);

            // Alt 2 : √ (mutation V → racine)
            var racineAlt = r.Spot.Alternatives[2];
            Assert.NotNull(racineAlt.Mutation);
            Assert.Equal("racine", racineAlt.Mutation!.Replacement);
        }

        [Fact]
        public void V_alone_yields_three_alternatives()
        {
            // V seul (suivi d'EOF) déclenche aussi : EOF est analogue à un
            // espace pour le pattern scope.
            var r = _engine.ConvertWithAmbiguity("V");
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleVAsForall, r.Spot!.RuleId);
            Assert.Equal(3, r.Spot.Alternatives.Count);
            Assert.Null(r.Spot.Alternatives[0].Mutation);
            Assert.Equal("forall", r.Spot.Alternatives[1].Mutation!.Replacement);
            Assert.Equal("racine", r.Spot.Alternatives[2].Mutation!.Replacement);
        }

        [Fact]
        public void V_alt_previews_render_real_post_mutation()
        {
            // Décomposition modulaire (ADR 29-04) : forall n'est plus un scope.
            // L'aperçu de l'alt ∀ pour `V x y` est juste `\forall xy` (juxtaposition
            // simple, pas de \in automatique). L'utilisateur ajoute `dans`/`in`
            // explicitement après s'il veut le \in.
            var r = _engine.ConvertWithAmbiguity("V x y");
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleVAsForall, r.Spot!.RuleId);
            // Alt 0 (V identity)
            Assert.Contains("V", r.Spot.Alternatives[0].Latex);
            // Alt 1 (∀) : rendu post-mutation V→forall, juxtaposition
            Assert.Contains("\\forall", r.Spot.Alternatives[1].Latex);
            // Alt 2 (√) : rendu post-mutation V→racine
            Assert.Contains("\\sqrt", r.Spot.Alternatives[2].Latex);
        }

        [Fact]
        public void Vx_collé_no_ambig()
        {
            // Vx (collé) = variable composée, pas un quantificateur
            var r = _engine.ConvertWithAmbiguity("Vx");
            Assert.Null(r.Spot);
        }

        [Fact]
        public void V_times_x_no_forall_ambig()
        {
            // V*x = produit V·x, pas un quantificateur (pas d'espace après V).
            // Depuis la cascade vec-dot-product (avril 2026), V*x propose
            // \vec{V} \cdot \vec{x} en alt — c'est volontaire. L'important
            // ici est que V→forall ne fire PAS sur ce pattern.
            var r = _engine.ConvertWithAmbiguity("V*x");
            Assert.NotEqual(AlternativeGenerator.RuleVAsForall, r.Spot?.RuleId);
            Assert.NotEqual(AlternativeGenerator.RuleEAsExists, r.Spot?.RuleId);
        }

        [Fact]
        public void Volume_no_ambig()
        {
            // Volume = mot, V suivi d'autre chose qu'un espace
            var r = _engine.ConvertWithAmbiguity("Volume");
            Assert.Null(r.Spot);
        }

        [Fact]
        public void Forall_x_dans_R_juxtaposition()
        {
            // Décomposition modulaire (ADR 29-04) : forall + x + dans + R
            // se composent par juxtaposition. Plus de scope avec \in auto.
            var r = _engine.ConvertWithAmbiguity("forall x dans R");
            Assert.Equal("\\forall x \\in R", r.TopLatex);
            // Ambig canonical-set proposée sur R isolé
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleCanonicalSet, r.Spot!.RuleId);
        }

        [Fact]
        public void Forall_alone_renders_just_forall()
        {
            // Décomposition modulaire : forall seul = juste "\forall " (avec
            // trailing space pour la juxtaposition future).
            var r = _engine.ConvertWithAmbiguity("forall");
            Assert.Equal("\\forall ", r.TopLatex);
        }

        [Fact]
        public void E_yields_two_alternatives()
        {
            // E : 2 alts (E identity / ∃). Pas de "racine" pour E (uniquement V).
            // Source sans R/N/Z/Q/C à droite pour que rightmost reste E.
            var r = _engine.ConvertWithAmbiguity("E y z");
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleEAsExists, r.Spot!.RuleId);
            Assert.Equal(2, r.Spot.Alternatives.Count);
            Assert.Null(r.Spot.Alternatives[0].Mutation);
            Assert.Equal("exists", r.Spot.Alternatives[1].Mutation!.Replacement);
        }

        // ---- Ensembles canoniques R/N/Z/Q/C ----

        [Fact]
        public void R_isolated_yields_two_alts_ensemble_default()
        {
            // R seul (suivi d'EOF) → popup avec 2 alts :
            // - alt 0 (focus défaut) = ensemble \mathbb{R} via mutation R→bbR
            // - alt 1 = R lettre identity (variable)
            var r = _engine.ConvertWithAmbiguity("R");
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleCanonicalSet, r.Spot!.RuleId);
            Assert.Equal(2, r.Spot.Alternatives.Count);
            // Alt 0 : mutation R → bbR
            Assert.NotNull(r.Spot.Alternatives[0].Mutation);
            Assert.Equal("bbR", r.Spot.Alternatives[0].Mutation!.Replacement);
            Assert.Contains("\\mathbb{R}", r.Spot.Alternatives[0].Latex);
            // Alt 1 : identity
            Assert.Null(r.Spot.Alternatives[1].Mutation);
        }

        [Fact]
        public void R_in_pi_R_squared_no_ambig()
        {
            // pi*R^2 : R suivi de ^ tight (opérateur math) → PAS isolé,
            // pas de popup. Préserve la formule de géométrie.
            var r = _engine.ConvertWithAmbiguity("pi*R^2");
            // Pas d'ambig sur R (peut y avoir une autre, ex sur sup/x², mais pas R)
            if (r.Spot != null)
                Assert.NotEqual(AlternativeGenerator.RuleCanonicalSet, r.Spot.RuleId);
        }

        [Fact]
        public void R_in_pi_R_squared_unicode_no_ambig()
        {
            // pi*R² : idem avec unicode super-2 (préprocessé en ^2)
            var r = _engine.ConvertWithAmbiguity("pi*R²");
            if (r.Spot != null)
                Assert.NotEqual(AlternativeGenerator.RuleCanonicalSet, r.Spot.RuleId);
        }

        [Fact]
        public void R_followed_by_op_no_ambig()
        {
            // 2R+1 : R suivi de + (op math) → pas isolé
            var r = _engine.ConvertWithAmbiguity("2R+1");
            if (r.Spot != null)
                Assert.NotEqual(AlternativeGenerator.RuleCanonicalSet, r.Spot.RuleId);
        }

        [Fact]
        public void R_followed_by_comma_yields_ambig()
        {
            // x dans R, x ≥ 0 : R suivi de , → isolé, popup
            var r = _engine.ConvertWithAmbiguity("x dans R, x");
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleCanonicalSet, r.Spot!.RuleId);
        }

        [Fact]
        public void N_isolated_yields_ambig()
        {
            var r = _engine.ConvertWithAmbiguity("N");
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleCanonicalSet, r.Spot!.RuleId);
            Assert.Equal("bbN", r.Spot.Alternatives[0].Mutation!.Replacement);
        }

        [Fact]
        public void Z_isolated_yields_ambig()
        {
            var r = _engine.ConvertWithAmbiguity("Z");
            Assert.NotNull(r.Spot);
            Assert.Equal("bbZ", r.Spot!.Alternatives[0].Mutation!.Replacement);
        }

        [Fact]
        public void Forall_x_dans_bbR_via_juxtaposition()
        {
            // Décomposition modulaire : forall + x + dans + bbR.
            var r = _engine.ConvertWithAmbiguity("forall x dans bbR");
            Assert.Equal("\\forall x \\in \\mathbb{R}", r.TopLatex);
        }

        [Fact]
        public void BbR_with_modifier_via_pipeline()
        {
            // bbR* → \mathbb{R}^*
            var r = _engine.ConvertWithAmbiguity("bbR*");
            Assert.Equal("\\mathbb{R}^*", r.TopLatex);
        }

        // ---- vec-dot-product : u*v → u·v (default) ou \vec{u}·\vec{v} (alt) ----
        // Cf. AlternativeGenerator.MatchAmbiguity, RuleVecDotProduct (mult
        // explicite entre deux idents 1-lettre).

        [Fact]
        public void U_times_V_yields_vec_dot_product_alternative()
        {
            var r = _engine.ConvertWithAmbiguity("u*v");
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleVecDotProduct, r.Spot!.RuleId);
            var alts = Lat(r.Spot.Alternatives);
            Assert.Contains("\\vec{u} \\cdot \\vec{v}", alts);
        }

        [Fact]
        public void Single_letter_uppercase_times_yields_vec_dot_product()
        {
            // A*B (lettres simples séparées par * explicite) → cascade vec dot.
            // Le pattern AB juxtaposé (sans `*`) tomberait sur RuleTwoUppercase.
            var r = _engine.ConvertWithAmbiguity("A*B");
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleVecDotProduct, r.Spot!.RuleId);
        }

        [Fact]
        public void Multichar_ident_times_no_vec_dot_product()
        {
            // ab*cd : pas de cascade vec — la règle ne fire que sur idents
            // 1-lettre (sinon ambig sur 'somme*delta', 'phi*psi'... = bruit).
            var r = _engine.ConvertWithAmbiguity("ab*cd");
            Assert.NotEqual(AlternativeGenerator.RuleVecDotProduct, r.Spot?.RuleId);
        }

        // ---- vector-layout-flip : col↔row (cf. brief vector-coordinates-shorthand) ----
        // Quand le top AST est un VectorCoordinates seul, propose le layout
        // opposé en alt. Espace entre valeurs = colonne ; virgule = ligne.

        [Fact]
        public void Vector_column_yields_row_flip_alternative()
        {
            // u (1 2) → colonne par défaut, ligne en alt
            var r = _engine.ConvertWithAmbiguity("u (1 2)");
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleVectorLayoutFlip, r.Spot!.RuleId);
            Assert.Equal(2, r.Spot.Alternatives.Count);
            // Les deux alts doivent rendre des LaTeX différents (sinon pas
            // d'ambig à proposer).
            Assert.NotEqual(r.Spot.Alternatives[0].Latex, r.Spot.Alternatives[1].Latex);
        }

        [Fact]
        public void Vector_row_yields_column_flip_alternative()
        {
            // u(1, 2) → ligne par défaut (virgule), colonne en alt
            var r = _engine.ConvertWithAmbiguity("u(1, 2)");
            Assert.NotNull(r.Spot);
            // Note : sur ce pattern f/g/h/F/G/H le rule prioritaire est
            // RuleVectorCoordsVsCall (function-call vs vec). Pour `u` ce
            // n'est PAS une lettre fonction-typique → RuleVectorLayoutFlip
            // est attendu.
            Assert.Equal(AlternativeGenerator.RuleVectorLayoutFlip, r.Spot!.RuleId);
        }

        // ---- vector-coords-vs-call : f(1, 2) → call (default) ou \vec{f}(1, 2) ----
        // Cf. AlternativeGenerator.ScanFunctionTypicalWithCommaCoords.
        // Déclenché uniquement sur f/g/h/F/G/H + paren contenant 2-3 segments
        // séparés par virgule au top-level.

        [Fact]
        public void Function_typical_2_args_yields_call_vs_vec_alternative()
        {
            var r = _engine.ConvertWithAmbiguity("f(1, 2)");
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleVectorCoordsVsCall, r.Spot!.RuleId);
            Assert.Equal(2, r.Spot.Alternatives.Count);
            // Alt 1 doit contenir \vec{f} (le nom original préservé)
            Assert.Contains("\\vec{f}", r.Spot.Alternatives[1].Latex);
        }

        [Fact]
        public void Function_typical_3_args_yields_call_vs_vec_alternative()
        {
            var r = _engine.ConvertWithAmbiguity("g(a, b, c)");
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleVectorCoordsVsCall, r.Spot!.RuleId);
        }

        [Fact]
        public void Function_typical_uppercase_yields_call_vs_vec_alternative()
        {
            // F/G/H sont aussi dans l'ensemble fonction-typique.
            var r = _engine.ConvertWithAmbiguity("F(1, 2)");
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleVectorCoordsVsCall, r.Spot!.RuleId);
        }

        [Fact]
        public void Function_typical_single_arg_no_call_vs_vec_ambig()
        {
            // f(x+1) : 1 segment seulement → règle ne fire pas (besoin 2-3 args)
            var r = _engine.ConvertWithAmbiguity("f(x+1)");
            Assert.NotEqual(AlternativeGenerator.RuleVectorCoordsVsCall, r.Spot?.RuleId);
        }

        [Fact]
        public void Non_typical_letter_with_comma_args_no_call_vs_vec_ambig()
        {
            // p(1, 2) : `p` n'est pas dans l'ensemble fonction-typique
            // (f/g/h/F/G/H), donc pas d'ambig call-vs-vec sur ce pattern.
            // Selon le parser, p(1, 2) peut être interprété en VectorCoordinates,
            // donc on n'asserte que sur l'absence du RuleVectorCoordsVsCall.
            var r = _engine.ConvertWithAmbiguity("p(1, 2)");
            Assert.NotEqual(AlternativeGenerator.RuleVectorCoordsVsCall, r.Spot?.RuleId);
        }

        // ===================================================================
        // ADR 30-04 Feat-tight-implicit-mult-grouping — élargissement chaîne tight
        // ===================================================================
        // Default = chaîne implicite uniquement (PEMDAS pour les ops `+ - *`).
        // Alt désambig = chaîne tight élargie aux ops (comportement V1 historique
        // mais maintenant accessible volontairement, pas par défaut).

        [Fact]
        public void Tight_chain_extension_slash_plus_proposes_extended_denom()
        {
            // 1/x+1 : default = \frac{1}{x}+1 (PEMDAS) ; alt = \frac{1}{x+1}.
            var r = _engine.ConvertWithAmbiguity("1/x+1");
            Assert.Equal("\\frac{1}{x}+1", r.TopLatex);
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleTightChainExtension, r.Spot!.RuleId);
            var alts = Lat(r.Spot.Alternatives);
            Assert.Contains("\\frac{1}{x}+1", alts);  // default
            Assert.Contains("\\frac{1}{x+1}", alts);  // élargi
        }

        [Fact]
        public void Tight_chain_extension_sup_plus_proposes_extended_exponent()
        {
            // x^a+b : default = x^{a}+b ; alt = x^{a+b}.
            var r = _engine.ConvertWithAmbiguity("x^a+b");
            Assert.Equal("x^{a}+b", r.TopLatex);
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleTightChainExtension, r.Spot!.RuleId);
            var alts = Lat(r.Spot.Alternatives);
            Assert.Contains("x^{a}+b", alts);
            Assert.Contains("x^{a+b}", alts);
        }

        [Fact]
        public void Tight_chain_extension_sub_plus_proposes_extended_subscript()
        {
            // u_n+1 : default = u_{n}+1 ; alt = u_{n+1}.
            var r = _engine.ConvertWithAmbiguity("u_n+1");
            Assert.Equal("u_{n}+1", r.TopLatex);
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleTightChainExtension, r.Spot!.RuleId);
            var alts = Lat(r.Spot.Alternatives);
            Assert.Contains("u_{n}+1", alts);
            Assert.Contains("u_{n+1}", alts);
        }

        [Fact]
        public void Tight_chain_extension_AB_BC_no_alt_already_grouped()
        {
            // AB/BC : default groupé en \frac{AB}{BC} (chaîne implicite).
            // Élargissement aux ops ne change rien (pas d'op explicite tight ici)
            // → pas d'ambig RuleTightChainExtension.
            // Mais peut y avoir d'autres ambigs (AB et BC majuscules).
            var r = _engine.ConvertWithAmbiguity("AB/BC");
            Assert.Equal("\\frac{AB}{BC}", r.TopLatex);
            // Spot peut être null OU une autre règle, mais PAS RuleTightChainExtension
            if (r.Spot != null)
                Assert.NotEqual(AlternativeGenerator.RuleTightChainExtension, r.Spot.RuleId);
        }

        [Fact]
        public void Tight_chain_extension_with_space_no_alt()
        {
            // 1/x +1 (espace) : `+` loose, pas d'élargissement tight possible.
            // Default et alt sont identiques → pas de RuleTightChainExtension.
            var r = _engine.ConvertWithAmbiguity("1/x +1");
            Assert.Equal("\\frac{1}{x}+1", r.TopLatex);
            if (r.Spot != null)
                Assert.NotEqual(AlternativeGenerator.RuleTightChainExtension, r.Spot.RuleId);
        }

        [Fact]
        public void Tight_chain_extension_simple_no_alt()
        {
            // 1/x simple : pas d'élargissement applicable → pas d'ambig.
            var r = _engine.ConvertWithAmbiguity("1/x");
            if (r.Spot != null)
                Assert.NotEqual(AlternativeGenerator.RuleTightChainExtension, r.Spot.RuleId);
        }

        // ===================================================================
        // ADR 30-04 Feat-asterisk-tightness-associativity — flip d'associativité
        // ===================================================================
        // Tight `*` = gauche-assoc PEMDAS ; loose `*` = droite-récursive.
        // L'inverse est exposé via la même cascade (RuleTightChainExtension).

        [Fact]
        public void Asterisk_tight_two_fractions_proposes_flip_alt()
        {
            // 1/2*3/4 (tight) : default \frac{(1/2)\times 3}{4} ;
            // alt flip = \frac{1}{2}\times \frac{3}{4}.
            var r = _engine.ConvertWithAmbiguity("1/2*3/4");
            Assert.Equal("\\frac{\\frac{1}{2}\\times 3}{4}", r.TopLatex);
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleTightChainExtension, r.Spot!.RuleId);
            var alts = Lat(r.Spot.Alternatives);
            Assert.Contains("\\frac{1}{2}\\times \\frac{3}{4}", alts);
        }

        [Fact]
        public void Asterisk_loose_two_fractions_proposes_flip_alt()
        {
            // 1/2 * 3/4 (loose) : default \frac{1}{2}\times \frac{3}{4} ;
            // alt flip = \frac{(1/2)\times 3}{4}.
            var r = _engine.ConvertWithAmbiguity("1/2 * 3/4");
            Assert.Equal("\\frac{1}{2}\\times \\frac{3}{4}", r.TopLatex);
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleTightChainExtension, r.Spot!.RuleId);
            var alts = Lat(r.Spot.Alternatives);
            Assert.Contains("\\frac{\\frac{1}{2}\\times 3}{4}", alts);
        }

        [Fact]
        public void Asterisk_tight_2_b_over_3_proposes_flip()
        {
            // 2*b/3 (tight) : default \frac{2\times b}{3} ; alt = 2\times \frac{b}{3}.
            // On évite a*b qui déclencherait RuleVecDotProduct (lettre*lettre)
            // de plus haute priorité ; le test cible spécifiquement la cascade flip.
            var r = _engine.ConvertWithAmbiguity("2*b/3");
            Assert.Equal("\\frac{2\\times b}{3}", r.TopLatex);
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleTightChainExtension, r.Spot!.RuleId);
            var alts = Lat(r.Spot.Alternatives);
            Assert.Contains("2\\times \\frac{b}{3}", alts);
        }

        [Fact]
        public void Asterisk_loose_a_b_over_3_proposes_flip()
        {
            // a *b/3 (loose `*`) : default a\times \frac{b}{3} ; alt = \frac{a\times b}{3}.
            var r = _engine.ConvertWithAmbiguity("a *b/3");
            Assert.Equal("a\\times \\frac{b}{3}", r.TopLatex);
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleTightChainExtension, r.Spot!.RuleId);
            var alts = Lat(r.Spot.Alternatives);
            Assert.Contains("\\frac{a\\times b}{3}", alts);
        }

        // ===================================================================
        // ADR 30-04 Feat-dot-as-multiplier — décimal vs mult + cascade vec
        // ===================================================================

        [Fact]
        public void Dot_number_pair_proposes_decimal_alt()
        {
            // `3.4` : default `3\cdot 4` ; alt décimal `3{,}4`.
            var r = _engine.ConvertWithAmbiguity("3.4");
            Assert.Equal("3\\cdot 4", r.TopLatex);
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleDecimalVsMultiplication, r.Spot!.RuleId);
            var alts = Lat(r.Spot.Alternatives);
            Assert.Contains("3\\cdot 4", alts);
            Assert.Contains("3{,}4", alts);
        }

        [Fact]
        public void Dot_number_pair_long_decimal_proposes_alt()
        {
            // `3.14` : default `3\cdot 14`, alt `3{,}14`.
            var r = _engine.ConvertWithAmbiguity("3.14");
            Assert.Equal("3\\cdot 14", r.TopLatex);
            var alts = Lat(r.Spot?.Alternatives ?? new System.Collections.Generic.List<AmbiguityAlternative>());
            Assert.Contains("3{,}14", alts);
        }

        [Fact]
        public void Dot_letter_pair_no_decimal_alt()
        {
            // `a.b` : pas d'ambig décimal possible (les côtés sont des lettres).
            var r = _engine.ConvertWithAmbiguity("a.b");
            if (r.Spot != null)
                Assert.NotEqual(AlternativeGenerator.RuleDecimalVsMultiplication, r.Spot.RuleId);
        }

        [Fact]
        public void Dot_mixed_no_decimal_alt()
        {
            // `2.x` : un côté lettre, pas d'ambig décimal.
            var r = _engine.ConvertWithAmbiguity("2.x");
            if (r.Spot != null)
                Assert.NotEqual(AlternativeGenerator.RuleDecimalVsMultiplication, r.Spot.RuleId);
        }

        [Fact]
        public void Dot_letter_letter_proposes_vec_dot_product_alt()
        {
            // `u.v` : la cascade RuleVecDotProduct étendue à `.` propose
            // `\vec{u} \cdot \vec{v}` en alt.
            var r = _engine.ConvertWithAmbiguity("u.v");
            Assert.Equal("u\\cdot v", r.TopLatex);
            Assert.NotNull(r.Spot);
            Assert.Equal(AlternativeGenerator.RuleVecDotProduct, r.Spot!.RuleId);
            var alts = Lat(r.Spot.Alternatives);
            Assert.Contains("\\vec{u} \\cdot \\vec{v}", alts);
        }
    }
}
