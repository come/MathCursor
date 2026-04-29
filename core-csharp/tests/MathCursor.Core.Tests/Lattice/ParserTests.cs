using MathCursor.Core.Lattice;
using MathCursor.Core.Lattice.Ast;
using Xunit;

namespace MathCursor.Core.Tests.Lattice
{
    /// <summary>
    /// Tests du parser. Pipeline minimal : Lex(input) → top-1 path → Parser → AST.
    /// On vérifie la STRUCTURE de l'AST (types et champs), pas le rendu LaTeX
    /// (c'est la phase 3).
    /// </summary>
    public sealed class ParserTests
    {
        private static AstNode ParseTop(string input)
        {
            var edges = Lexer.Lex(input);
            var paths = LatticePathFinder.TopK(edges, input.Length, 3);
            return new Parser(paths[0].Edges).Parse();
        }

        // ------------------ Atomes & opérateurs simples ------------------

        [Fact]
        public void Empty_input_yields_hole()
        {
            // Pas d'arêtes → un seul chemin vide → Parse() doit retourner Hole(1)
            var edges = Lexer.Lex("");
            var paths = LatticePathFinder.TopK(edges, 0, 3);
            var ast = new Parser(paths[0].Edges).Parse();
            var h = Assert.IsType<Hole>(ast);
            Assert.Equal(1, h.Idx);
        }

        [Fact]
        public void Single_ident_yields_atom()
        {
            var ast = ParseTop("x");
            var a = Assert.IsType<Atom>(ast);
            Assert.Equal("ident", a.Kind);
            Assert.Equal("x", a.Value);
        }

        [Fact]
        public void Single_number_yields_atom()
        {
            var ast = ParseTop("42");
            var a = Assert.IsType<Atom>(ast);
            Assert.Equal("number", a.Kind);
            Assert.Equal("42", a.Value);
        }

        [Fact]
        public void Greek_yields_atom_greek()
        {
            var ast = ParseTop("pi");
            var a = Assert.IsType<Atom>(ast);
            Assert.Equal("greek", a.Kind);
            Assert.Equal("pi", a.Value);
        }

        // ------------------ Tight vs loose ------------------

        [Fact]
        public void N_plus_1_is_tight_bin()
        {
            var ast = ParseTop("n+1");
            var b = Assert.IsType<Bin>(ast);
            Assert.Equal("+", b.Op);
            Assert.True(b.Tight);
            Assert.False(b.Implicit);
        }

        [Fact]
        public void N_space_plus_space_1_is_loose_bin()
        {
            var ast = ParseTop("n + 1");
            var b = Assert.IsType<Bin>(ast);
            Assert.Equal("+", b.Op);
            Assert.False(b.Tight);
        }

        [Fact]
        public void Implicit_mult_2x_is_implicit_tight()
        {
            var ast = ParseTop("2x");
            var b = Assert.IsType<Bin>(ast);
            Assert.Equal("*", b.Op);
            Assert.True(b.Implicit);
            Assert.True(b.Tight);
        }

        [Fact]
        public void Implicit_mult_2_space_x_is_implicit_loose()
        {
            var ast = ParseTop("2 x");
            var b = Assert.IsType<Bin>(ast);
            Assert.True(b.Implicit);
            Assert.False(b.Tight);
        }

        // ------------------ Postfix ^ et _ ------------------

        [Fact]
        public void X_pow_2_is_sup()
        {
            var ast = ParseTop("x^2");
            var s = Assert.IsType<Sup>(ast);
            Assert.IsType<Atom>(s.Base);
            Assert.IsType<Atom>(s.Exp);
        }

        [Fact]
        public void U_underscore_n_is_sub()
        {
            var ast = ParseTop("u_n");
            var s = Assert.IsType<Sub>(ast);
            Assert.IsType<Atom>(s.Base);
            Assert.IsType<Atom>(s.Idx);
        }

        // ------------------ Group ------------------

        [Fact]
        public void Group_x_plus_y()
        {
            var ast = ParseTop("(x+y)");
            var g = Assert.IsType<Group>(ast);
            Assert.IsType<Bin>(g.Expr);
        }

        // ------------------ Func ------------------

        [Fact]
        public void Cos_x_is_func_with_atom_arg()
        {
            var ast = ParseTop("cos x");
            var f = Assert.IsType<Func>(ast);
            Assert.Equal("cos", f.Name);
            Assert.IsType<Atom>(f.Arg);
        }

        [Fact]
        public void Cos2x_is_func_with_implicit_tight_arg()
        {
            // "cos2x" : cos · (2x) avec 2x en TightChain (implicit + tight)
            var ast = ParseTop("cos2x");
            var f = Assert.IsType<Func>(ast);
            Assert.Equal("cos", f.Name);
            var b = Assert.IsType<Bin>(f.Arg);
            Assert.True(b.Implicit);
            Assert.True(b.Tight);
        }

        [Fact]
        public void Cos_paren_is_func_with_group_arg()
        {
            var ast = ParseTop("cos(x+y)");
            var f = Assert.IsType<Func>(ast);
            Assert.IsType<Group>(f.Arg);
        }

        // ------------------ Scopes : sum ------------------

        [Fact]
        public void Sum_complete_yields_full_ast()
        {
            // "sum k=1 n+1 cos2x"
            var ast = ParseTop("sum k=1 n+1 cos2x");
            var s = Assert.IsType<Sum>(ast);
            Assert.Equal("sum", s.Symbol);
            Assert.IsType<Atom>(s.Var);     // k
            Assert.IsType<Atom>(s.Start);   // 1
            // n+1 = TightChain → Bin
            Assert.IsType<Bin>(s.End);
            // cos2x = Func
            Assert.IsType<Func>(s.Body);
        }

        [Fact]
        public void Sum_partial_holes_for_missing_slots()
        {
            // "sum k" → var=k, mais start/end/body manquants = Holes ②③④
            var ast = ParseTop("sum k");
            var s = Assert.IsType<Sum>(ast);
            Assert.IsType<Atom>(s.Var);
            var h2 = Assert.IsType<Hole>(s.Start); Assert.Equal(2, h2.Idx);
            var h3 = Assert.IsType<Hole>(s.End); Assert.Equal(3, h3.Idx);
            var h4 = Assert.IsType<Hole>(s.Body); Assert.Equal(4, h4.Idx);
        }

        [Fact]
        public void Sum_body_stops_at_loose_binop()
        {
            // "sum k 1 n f(k) + g(k)" : body = f(k), puis (Σ…) + g(k) au top-level
            var ast = ParseTop("sum k 1 n f(k) + g(k)");
            // Top-level = Bin(+, loose, Sum, Func or implicit mult)
            var b = Assert.IsType<Bin>(ast);
            Assert.Equal("+", b.Op);
            Assert.False(b.Tight);
            Assert.IsType<Sum>(b.Lhs);
        }

        [Fact]
        public void Sum_body_consumes_tight_binop()
        {
            // "sum k 1 n f(k)+1" : body = f(k)+1 (tight), reste un seul Sum au top
            var ast = ParseTop("sum k 1 n f(k)+1");
            var s = Assert.IsType<Sum>(ast);
            // body est un Bin(+, tight)
            var b = Assert.IsType<Bin>(s.Body);
            Assert.Equal("+", b.Op);
            Assert.True(b.Tight);
        }

        // ------------------ Scopes : lim, int, sqrt, frac, vec ------------------

        [Fact]
        public void Lim_x_to_0_f_x_yields_lim()
        {
            var ast = ParseTop("lim x 0 f(x)");
            var l = Assert.IsType<Lim>(ast);
            Assert.IsType<Atom>(l.Var);
            Assert.IsType<Atom>(l.Target);
            // f(x) = f * (x) en implicit mult, ou Func si f reconnue. f n'est pas
            // dans Vocabulary.Functions, donc c'est implicit mult sur ident f.
            // On vérifie juste que ce n'est pas un Hole.
            Assert.IsNotType<Hole>(l.Body);
        }

        [Fact]
        public void Lim_with_arrow_consumes_arrow()
        {
            // "lim x -> 0 f(x)" : la flèche est consommée silencieusement
            var ast = ParseTop("lim x -> 0 f(x)");
            var l = Assert.IsType<Lim>(ast);
            var t = Assert.IsType<Atom>(l.Target);
            Assert.Equal("0", t.Value);
        }

        [Fact]
        public void Int_0_1_x_yields_int()
        {
            var ast = ParseTop("int 0 1 x");
            var i = Assert.IsType<Int>(ast);
            Assert.IsType<Atom>(i.Low);
            Assert.IsType<Atom>(i.High);
            Assert.IsType<Atom>(i.Body);
        }

        [Fact]
        public void Racine_x_yields_sqrt()
        {
            var ast = ParseTop("racine x");
            var s = Assert.IsType<Sqrt>(ast);
            Assert.IsType<Atom>(s.Arg);
        }

        [Fact]
        public void Racine_alone_yields_sqrt_with_hole()
        {
            var ast = ParseTop("racine");
            var s = Assert.IsType<Sqrt>(ast);
            var h = Assert.IsType<Hole>(s.Arg);
            Assert.Equal(1, h.Idx);
        }

        [Fact]
        public void Frac_a_b_yields_frac()
        {
            var ast = ParseTop("frac a b");
            var f = Assert.IsType<Frac>(ast);
            Assert.IsType<Atom>(f.Num);
            Assert.IsType<Atom>(f.Den);
        }

        [Fact]
        public void Frac_a_alone_yields_frac_with_hole_for_den()
        {
            var ast = ParseTop("frac a");
            var f = Assert.IsType<Frac>(ast);
            Assert.IsType<Atom>(f.Num);
            var h = Assert.IsType<Hole>(f.Den);
            Assert.Equal(2, h.Idx);
        }

        [Fact]
        public void Vec_AB_concatenates_idents()
        {
            var ast = ParseTop("vec AB");
            var v = Assert.IsType<Vec>(ast);
            Assert.Equal("AB", v.Name);
        }

        [Fact]
        public void Vec_alone_yields_null_name()
        {
            var ast = ParseTop("vec");
            var v = Assert.IsType<Vec>(ast);
            Assert.Null(v.Name);
        }

        // ------------------ Composabilité ------------------

        [Fact]
        public void Sum_with_nested_racine()
        {
            // "sum k=1 n racine k" : body = racine k
            var ast = ParseTop("sum k=1 n racine k");
            var s = Assert.IsType<Sum>(ast);
            Assert.IsType<Sqrt>(s.Body);
        }

        [Fact]
        public void Lim_with_nested_frac_sin()
        {
            // "lim x 0 frac sin x x" : body = frac (sin x) x
            var ast = ParseTop("lim x 0 frac sin x x");
            var l = Assert.IsType<Lim>(ast);
            var f = Assert.IsType<Frac>(l.Body);
            // num = sin x = Func
            Assert.IsType<Func>(f.Num);
            // den = x
            Assert.IsType<Atom>(f.Den);
        }

        [Fact]
        public void Nested_sum_within_sum()
        {
            // "sum i=1 n sum j=1 i ij" : sum extérieur, body = sum intérieur
            var ast = ParseTop("sum i=1 n sum j=1 i ij");
            var outer = Assert.IsType<Sum>(ast);
            var inner = Assert.IsType<Sum>(outer.Body);
            // body intérieur = ij = i * j (implicit)
            var b = Assert.IsType<Bin>(inner.Body);
            Assert.True(b.Implicit);
        }

        // ------------------ Constantes ------------------

        [Fact]
        public void Inf_yields_const_infty()
        {
            var ast = ParseTop("inf");
            var c = Assert.IsType<Const>(ast);
            Assert.Equal("\\infty", c.Value);
        }

        // ------------------ Quantificateurs (décomposition modulaire) ------------------
        //
        // Depuis l'ADR du 29-04, forall/exists ne sont plus des scopes mais
        // des Const composés naturellement avec var, in/dans/(- et set par
        // juxtaposition. Plus de nœud Quant.

        [Fact]
        public void Forall_yields_const()
        {
            var ast = ParseTop("forall");
            var c = Assert.IsType<Const>(ast);
            // Trailing space pour la juxtaposition propre avec le var qui suit.
            Assert.Equal("\\forall ", c.Value);
        }

        [Fact]
        public void Exists_yields_const()
        {
            var ast = ParseTop("exists");
            var c = Assert.IsType<Const>(ast);
            Assert.Equal("\\exists ", c.Value);
        }

        [Fact]
        public void In_arrow_keyboard_alias_yields_in_const()
        {
            // (- (clavier, multi-char) est un alias de `in` qui rend ` \in `
            var ast = ParseTop("(-");
            var c = Assert.IsType<Const>(ast);
            Assert.Equal(" \\in ", c.Value);
        }

        // ------------------ Intervalles français ------------------

        [Fact]
        public void Closed_interval_yields_interval_node()
        {
            var ast = ParseTop("[0,1]");
            var iv = Assert.IsType<Interval>(ast);
            Assert.True(iv.LeftClosed);
            Assert.True(iv.RightClosed);
            Assert.IsType<Atom>(iv.Low);
            Assert.IsType<Atom>(iv.High);
        }

        [Fact]
        public void Closed_open_interval_yields_correct_flags()
        {
            // [a,b[ : fermé à gauche, ouvert à droite
            var ast = ParseTop("[0,1[");
            var iv = Assert.IsType<Interval>(ast);
            Assert.True(iv.LeftClosed);
            Assert.False(iv.RightClosed);
        }

        [Fact]
        public void Open_closed_interval_yields_correct_flags()
        {
            // ]a,b] : ouvert à gauche, fermé à droite
            var ast = ParseTop("]0,1]");
            var iv = Assert.IsType<Interval>(ast);
            Assert.False(iv.LeftClosed);
            Assert.True(iv.RightClosed);
        }

        [Fact]
        public void Open_open_interval_yields_correct_flags()
        {
            var ast = ParseTop("]0,1[");
            var iv = Assert.IsType<Interval>(ast);
            Assert.False(iv.LeftClosed);
            Assert.False(iv.RightClosed);
        }

        [Fact]
        public void Interval_with_negative_infinity_low()
        {
            // ]-inf,1] : low = Unary(-, Const(\infty)), high = 1
            var ast = ParseTop("]-inf,1]");
            var iv = Assert.IsType<Interval>(ast);
            Assert.False(iv.LeftClosed);
            Assert.True(iv.RightClosed);
            Assert.IsType<Unary>(iv.Low);
        }

        // ------------------ Union / Intersection d'intervalles ------------------

        [Fact]
        public void Union_keyword_between_intervals()
        {
            var ast = ParseTop("[0,1] union [3,5]");
            var b = Assert.IsType<Bin>(ast);
            Assert.Equal("union", b.Op);
            Assert.IsType<Interval>(b.Lhs);
            Assert.IsType<Interval>(b.Rhs);
        }

        [Fact]
        public void U_letter_between_intervals_yields_union()
        {
            // U entre intervalles = union 100% (détection contextuelle)
            var ast = ParseTop("[0,1] U [3,5]");
            var b = Assert.IsType<Bin>(ast);
            Assert.Equal("union", b.Op);
        }

        [Fact]
        public void U_letter_between_intervals_no_space_yields_union()
        {
            // Sans espace : "[0,1]U[3,5]" → idem union
            var ast = ParseTop("[0,1]U[3,5]");
            var b = Assert.IsType<Bin>(ast);
            Assert.Equal("union", b.Op);
        }

        [Fact]
        public void U_letter_alone_stays_variable()
        {
            // U dans contexte non-intervalle → reste atom U (variable)
            var ast = ParseTop("U");
            var a = Assert.IsType<Atom>(ast);
            Assert.Equal("ident", a.Kind);
            Assert.Equal("U", a.Value);
        }

        [Fact]
        public void Inter_keyword_between_intervals()
        {
            var ast = ParseTop("[0,1] inter [0.5,2]");
            var b = Assert.IsType<Bin>(ast);
            Assert.Equal("inter", b.Op);
        }

        [Fact]
        public void Intersection_keyword_alias()
        {
            var ast = ParseTop("[0,1] intersection [0.5,2]");
            var b = Assert.IsType<Bin>(ast);
            Assert.Equal("inter", b.Op);
        }

        // ------------------ Relations (=, <, >, <=, ...) ------------------

        [Fact]
        public void Equals_relation_at_top_level()
        {
            // "x = 1" : Bin(=, lhs=x, rhs=1) au top-level (parseRelation)
            var ast = ParseTop("x = 1");
            var b = Assert.IsType<Bin>(ast);
            Assert.Equal("=", b.Op);
            Assert.IsType<Atom>(b.Lhs);
            Assert.IsType<Atom>(b.Rhs);
        }

        [Fact]
        public void Equals_with_complex_rhs_does_not_truncate()
        {
            // Régression : sans parseRelation, "f(x) = sin x" produisait f(x)
            // et abandonnait silencieusement "= sin x".
            var ast = ParseTop("f(x) = sin x");
            var b = Assert.IsType<Bin>(ast);
            Assert.Equal("=", b.Op);
            // RHS = Func("sin", x)
            Assert.IsType<Func>(b.Rhs);
        }

        [Fact]
        public void Less_than_tokenized_and_parsed()
        {
            var ast = ParseTop("a < b");
            var b = Assert.IsType<Bin>(ast);
            Assert.Equal("<", b.Op);
        }

        [Fact]
        public void Multichar_relations_recognized()
        {
            foreach (var op in new[] { "<=", ">=", "!=", "<>" })
            {
                var ast = ParseTop($"a {op} b");
                var b = Assert.IsType<Bin>(ast);
                Assert.Equal(op, b.Op);
            }
        }

        [Fact]
        public void Chained_equals_left_associative()
        {
            // "a = b = c" → Bin(=, Bin(=, a, b), c)
            var ast = ParseTop("a = b = c");
            var outer = Assert.IsType<Bin>(ast);
            Assert.Equal("=", outer.Op);
            var inner = Assert.IsType<Bin>(outer.Lhs);
            Assert.Equal("=", inner.Op);
            Assert.IsType<Atom>(outer.Rhs); // c
        }

        // ------------------ Unaire ------------------

        [Fact]
        public void Minus_x_is_unary()
        {
            var ast = ParseTop("-x");
            var u = Assert.IsType<Unary>(ast);
            Assert.Equal("-", u.Op);
            Assert.IsType<Atom>(u.Arg);
        }
    }
}
