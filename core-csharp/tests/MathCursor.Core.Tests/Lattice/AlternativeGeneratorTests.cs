using System.Linq;
using MathCursor.Core.Lattice;
using MathCursor.Core.Lattice.Ast;
using Xunit;

namespace MathCursor.Core.Tests.Lattice
{
    public sealed class AlternativeGeneratorTests
    {
        [Fact]
        public void Null_ast_returns_empty()
            => Assert.Empty(AlternativeGenerator.Generate(null));

        // ---- Pattern AB : 2 majuscules adjacentes en mult implicite ----

        [Fact]
        public void Two_uppercase_letters_yields_vec_droite_segment()
        {
            var ast = new Bin("*", true, true,
                new Atom("ident", "A"), new Atom("ident", "B"));
            var alts = AlternativeGenerator.Generate(ast);
            Assert.Equal(3, alts.Count);
            Assert.Contains("\\vec{AB}", alts);
            Assert.Contains("\\left(AB\\right)", alts);
            Assert.Contains("\\left[AB\\right]", alts);
        }

        [Fact]
        public void Two_lowercase_letters_yields_no_alternatives()
        {
            // ab n'est pas un objet géométrique nommé : pas d'alternative
            var ast = new Bin("*", true, true,
                new Atom("ident", "a"), new Atom("ident", "b"));
            Assert.Empty(AlternativeGenerator.Generate(ast));
        }

        [Fact]
        public void Mixed_case_yields_no_alternatives()
        {
            // Aa n'est pas un cas géométrique standard
            var ast = new Bin("*", true, true,
                new Atom("ident", "A"), new Atom("ident", "b"));
            Assert.Empty(AlternativeGenerator.Generate(ast));
        }

        [Fact]
        public void Multi_char_first_yields_no_alternatives()
        {
            // "ABC" ou "Ab" ne match pas (longueur > 1)
            var ast = new Bin("*", true, true,
                new Atom("ident", "AB"), new Atom("ident", "C"));
            Assert.Empty(AlternativeGenerator.Generate(ast));
        }

        // ---- Pattern x2 → Sup → indice alternatif ----

        [Fact]
        public void Sup_letter_number_yields_subscript_alternative()
        {
            // x² → alternative x_2
            var ast = new Sup(new Atom("ident", "x"), new Atom("number", "2"));
            var alts = AlternativeGenerator.Generate(ast);
            Assert.Single(alts);
            Assert.Equal("x_{2}", alts[0]);
        }

        [Fact]
        public void Sup_multichar_letter_no_alternative()
        {
            // "abc²" n'a pas de version en indice (pas une variable indexée)
            var ast = new Sup(new Atom("ident", "abc"), new Atom("number", "2"));
            Assert.Empty(AlternativeGenerator.Generate(ast));
        }

        [Fact]
        public void Sup_letter_non_number_no_alternative()
        {
            // x^n n'est pas un cas indice (l'exposant n'est pas un chiffre)
            var ast = new Sup(new Atom("ident", "x"), new Atom("ident", "n"));
            Assert.Empty(AlternativeGenerator.Generate(ast));
        }

        // ---- Intégration avec LatticeEngine ----

        [Fact]
        public void Engine_returns_AB_with_3_alternatives()
        {
            var engine = new MathCursor.Core.LatticeEngine();
            var s = engine.Convert("AB");
            Assert.Equal(4, s.Count);
            Assert.Equal("AB", s[0].Latex); // top-1
            var alts = s.Skip(1).Select(x => x.Latex).ToList();
            Assert.Contains("\\vec{AB}", alts);
            Assert.Contains("\\left(AB\\right)", alts);
            Assert.Contains("\\left[AB\\right]", alts);
        }

        [Fact]
        public void Engine_returns_x2_with_subscript_alternative()
        {
            var engine = new MathCursor.Core.LatticeEngine();
            var s = engine.Convert("x2");
            Assert.Equal(2, s.Count);
            Assert.Equal("x^{2}", s[0].Latex);
            Assert.Equal("x_{2}", s[1].Latex);
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
            Assert.Contains("\\vec{CD}", r.Spot.Alternatives);
        }

        [Fact]
        public void Rightmost_finds_ambiguity_in_subexpression()
        {
            // "f(x2)" : ambiguïté en sous-AST (x²)
            var r = _engine.ConvertWithAmbiguity("f(x2)");
            Assert.NotNull(r.Spot);
            Assert.Equal("x^{2}", r.Spot!.DefaultLatex);
            Assert.Contains("x_{2}", r.Spot.Alternatives);
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
    }
}
