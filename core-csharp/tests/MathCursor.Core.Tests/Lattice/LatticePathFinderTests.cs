using System.Collections.Generic;
using System.Linq;
using MathCursor.Core.Lattice;
using Xunit;

namespace MathCursor.Core.Tests.Lattice
{
    public sealed class LatticePathFinderTests
    {
        [Fact]
        public void Empty_input_returns_one_zero_cost_path()
        {
            // Pas d'arêtes, longueur 0 → un chemin vide reste valide
            var paths = LatticePathFinder.TopK(new List<LatticeEdge>(), 0, 3);
            Assert.Single(paths);
            Assert.Equal(0, paths[0].Cost);
            Assert.Empty(paths[0].Edges);
        }

        [Fact]
        public void Single_edge_yields_single_path()
        {
            var edges = Lexer.Lex("x");
            var paths = LatticePathFinder.TopK(edges, 1, 3);
            Assert.Single(paths);
            Assert.Equal("x", paths[0].Edges[0].Value);
        }

        [Fact]
        public void Cos_picks_function_over_idents()
        {
            // "cos" : top-1 = fonction (w=1), pas le triplet ident c·o·s (w=15)
            var edges = Lexer.Lex("cos");
            var paths = LatticePathFinder.TopK(edges, 3, 3);
            Assert.NotEmpty(paths);
            Assert.Equal(1, paths[0].Cost);
            Assert.Single(paths[0].Edges);
            Assert.Equal(EdgeType.Function, paths[0].Edges[0].Type);
            Assert.Equal("cos", paths[0].Edges[0].Value);
        }

        [Fact]
        public void Cos_topK_includes_alternative_ident_split()
        {
            // top-K doit aussi exposer la lecture en idents pour l'ambiguïté détectable
            var edges = Lexer.Lex("cos");
            var paths = LatticePathFinder.TopK(edges, 3, 5);
            // Le top-1 est cos fonction. On doit avoir au moins une autre lecture (cos ident, ou c·o·s)
            Assert.True(paths.Count >= 2);
            // Le chemin "cos ident" coûte 27 ; "c·o·s" coûte 15 ; les deux doivent apparaître dans top-5
            var costs = paths.Select(p => p.Cost).ToList();
            Assert.Contains(1, costs); // fonction
            Assert.Contains(15, costs); // c·o·s (3 × 5)
        }

        [Fact]
        public void Pi_function_path_preferred_over_idents()
        {
            // "pi" lettre grecque (w=1) vs "p·i" (w=10)
            var edges = Lexer.Lex("pi");
            var paths = LatticePathFinder.TopK(edges, 2, 3);
            Assert.Equal(1, paths[0].Cost);
            Assert.Equal(EdgeType.Greek, paths[0].Edges[0].Type);
        }

        [Fact]
        public void Number_plus_letter_yields_two_edge_path()
        {
            // "2x" : nombre + lettre = 2 arêtes en série
            var edges = Lexer.Lex("2x");
            var paths = LatticePathFinder.TopK(edges, 2, 3);
            var top = paths[0];
            Assert.Equal(2, top.Edges.Count);
            Assert.Equal(EdgeType.Number, top.Edges[0].Type);
            Assert.Equal("2", top.Edges[0].Value);
            Assert.Equal(EdgeType.Ident, top.Edges[1].Type);
            Assert.Equal("x", top.Edges[1].Value);
        }

        [Fact]
        public void Multichar_op_preferred_over_two_singles()
        {
            // "<=" coût négatif (-length = -2) garantit qu'il bat `<` (0) +
            // `=` (0) = 0. Cf. ADR 29-04 implication-equivalence-arrows qui
            // a introduit ce schéma de coût pour résoudre les ambiguïtés
            // multi-char (notamment `<=>` vs `<=` + `>`).
            var edges = Lexer.Lex("<=");
            var paths = LatticePathFinder.TopK(edges, 2, 3);
            Assert.Equal(-2, paths[0].Cost);
            // Top-1 : 1 seule arête multi-char
            Assert.Single(paths[0].Edges);
            Assert.Equal("<=", paths[0].Edges[0].Value);
        }

        [Fact]
        public void TopK_returns_paths_sorted_by_cost_ascending()
        {
            var edges = Lexer.Lex("cos");
            var paths = LatticePathFinder.TopK(edges, 3, 3);
            for (int i = 1; i < paths.Count; i++)
            {
                Assert.True(paths[i - 1].Cost <= paths[i].Cost,
                    $"Paths must be sorted by cost ascending: {paths[i - 1].Cost} > {paths[i].Cost}");
            }
        }
    }
}
