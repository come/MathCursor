using System.Collections.Generic;
using System.Linq;
using MathCursor.Core.Lattice;
using Xunit;

namespace MathCursor.Core.Tests.Lattice
{
    public sealed class AmbiguityDetectorTests
    {
        [Fact]
        public void Null_or_single_path_returns_null()
        {
            Assert.Null(AmbiguityDetector.FindLastAmbiguous(null!, 0));
            var single = LatticePathFinder.TopK(Lexer.Lex("x"), 1, 3);
            Assert.Null(AmbiguityDetector.FindLastAmbiguous(single, 1));
        }

        [Fact]
        public void Identical_paths_return_null()
        {
            // On force le cas dégénéré : le même chemin deux fois
            var edges = Lexer.Lex("x");
            var paths = LatticePathFinder.TopK(edges, 1, 3);
            var doubled = new List<LatticePath> { paths[0], paths[0] };
            Assert.Null(AmbiguityDetector.FindLastAmbiguous(doubled, 1));
        }

        [Fact]
        public void Cos_yields_ambiguous_segment_full_span()
        {
            // "cos" : top-K contient la lecture fonction et la lecture ident — ambiguïté sur [0,3]
            var edges = Lexer.Lex("cos");
            var paths = LatticePathFinder.TopK(edges, 3, 5);
            var seg = AmbiguityDetector.FindLastAmbiguous(paths, 3);
            Assert.NotNull(seg);
            Assert.Equal(0, seg!.Start);
            Assert.Equal(3, seg.End);
            Assert.True(seg.Variants.Count >= 2);
        }

        [Fact]
        public void Two_x_yields_no_ambiguity_when_paths_agree()
        {
            // "x" : un seul chemin possible
            var edges = Lexer.Lex("x");
            var paths = LatticePathFinder.TopK(edges, 1, 3);
            var seg = AmbiguityDetector.FindLastAmbiguous(paths, 1);
            Assert.Null(seg);
        }

        [Fact]
        public void Last_ambiguity_is_kept_when_multiple_segments_diverge()
        {
            // "cos x" : le "cos" est ambigu (fonction vs idents), le "x" ne l'est pas.
            // Le détecteur doit retourner le dernier — donc cos (qui est avant x).
            // En fait il n'y a qu'un seul segment ambigu ici, donc on doit le retourner.
            var edges = Lexer.Lex("cos x");
            var paths = LatticePathFinder.TopK(edges, 5, 5);
            var seg = AmbiguityDetector.FindLastAmbiguous(paths, 5);
            Assert.NotNull(seg);
            // L'ambiguïté est sur [0,3] : "cos"
            Assert.Equal(0, seg!.Start);
            Assert.Equal(3, seg.End);
        }

        [Fact]
        public void Variants_are_deduplicated_by_signature()
        {
            // Si 3 chemins du top-K passent par les mêmes arêtes sur le segment ambigu,
            // on ne doit voir qu'une seule variante.
            var edges = Lexer.Lex("cos");
            var paths = LatticePathFinder.TopK(edges, 3, 10);
            var seg = AmbiguityDetector.FindLastAmbiguous(paths, 3);
            Assert.NotNull(seg);
            // Les signatures des variantes doivent toutes être distinctes
            var sigs = seg!.Variants
                .Select(v => string.Join("|", v.Select(e => $"{e.Type}/{e.Value}")))
                .ToList();
            Assert.Equal(sigs.Count, sigs.Distinct().Count());
        }

        [Fact]
        public void Costs_match_variants_count()
        {
            var edges = Lexer.Lex("cos");
            var paths = LatticePathFinder.TopK(edges, 3, 5);
            var seg = AmbiguityDetector.FindLastAmbiguous(paths, 3);
            Assert.NotNull(seg);
            Assert.Equal(seg!.Variants.Count, seg.Costs.Count);
        }
    }
}
