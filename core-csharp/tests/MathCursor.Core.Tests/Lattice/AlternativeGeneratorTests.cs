using System.Collections.Generic;
using System.Linq;
using MathCursor.Core.Lattice;
using MathCursor.Core.Lattice.Ast;
using Xunit;

namespace MathCursor.Core.Tests.Lattice
{
    public sealed class AlternativeGeneratorTests
    {
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
            // simple, pas de \in automatique). L'utilisateur ajoute `dans`/`(-`
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
        public void V_times_x_no_ambig()
        {
            // V*x = produit V·x, pas un quantificateur (pas d'espace après V)
            var r = _engine.ConvertWithAmbiguity("V*x");
            Assert.Null(r.Spot);
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
    }
}
