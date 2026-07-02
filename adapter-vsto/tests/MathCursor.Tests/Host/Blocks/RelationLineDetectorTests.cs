// MathCursor: capturing mathematical intent from linear keyboard input.
// Copyright (C) 2026  Côme de Percin
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using MathCursor.Host.Blocks;
using Xunit;

namespace MathCursor.Tests.Host.Blocks
{
    /// <summary>
    /// M1 du chantier multiligne (ADR 2026-06-10-Feat-multiline-chain-eqarr-
    /// architecture) : détection pure « ligne commençant par un marqueur de
    /// chaîne », plus-long-match, offsets exacts pour la couche Word.
    /// </summary>
    public sealed class RelationLineDetectorTests
    {
        [Theory]
        [InlineData("= 2x", "=", "=", "2x")]
        [InlineData("=2x", "=", "=", "2x")]
        [InlineData("<=> x = 3", "<=>", "\\Leftrightarrow ", "x = 3")]
        [InlineData("=> 2x = 6", "=>", "\\Rightarrow ", "2x = 6")]
        [InlineData("⟺ x=3", "⟺", "\\Leftrightarrow ", "x=3")]
        [InlineData("⇒ x", "⇒", "\\Rightarrow ", "x")]
        [InlineData("<= 5", "<=", "\\leq ", "5")]
        [InlineData(">= 0", ">=", "\\geq ", "0")]
        [InlineData("≠ 0", "≠", "\\neq ", "0")]
        [InlineData("< 1/x", "<", "<", "1/x")]
        [InlineData("> x+1", ">", ">", "x+1")]
        [InlineData("approx 3,14", "approx", "\\approx ", "3,14")]
        [InlineData("≈ 0,5", "≈", "\\approx ", "0,5")]
        // environ/env = synonymes de approx (ADR 2026-06-19). « env » abrégé.
        [InlineData("environ f(x)", "environ", "\\approx ", "f(x)")]
        [InlineData("env f(x)", "env", "\\approx ", "f(x)")]
        // Majuscule auto de Word en début de ligne → insensible à la casse.
        [InlineData("Env f(x)", "env", "\\approx ", "f(x)")]
        [InlineData("Environ 3,14", "environ", "\\approx ", "3,14")]
        [InlineData("Approx 3,14", "approx", "\\approx ", "3,14")]
        public void Detects_marker_and_rest(string line, string typed, string latex, string rest)
        {
            var m = RelationLineDetector.TryDetect(line);
            Assert.NotNull(m);
            Assert.Equal(typed, m.MarkerTyped);
            Assert.Equal(latex, m.MarkerLatex);
            Assert.Equal(rest, m.Rest);
        }

        [Fact]
        public void Longest_match_wins()
        {
            // « <=> » ne doit JAMAIS être lu « <= » + « > ».
            var m = RelationLineDetector.TryDetect("<=> y");
            Assert.Equal("<=>", m.MarkerTyped);
            // « => » ne doit pas être lu « = » + « > ».
            Assert.Equal("=>", RelationLineDetector.TryDetect("=> y").MarkerTyped);
        }

        [Fact]
        public void Leading_whitespace_is_skipped_offsets_are_exact()
        {
            var m = RelationLineDetector.TryDetect("   <=> 2x");
            Assert.NotNull(m);
            Assert.Equal(3, m.MarkerStart);
            Assert.Equal(7, m.RestStart);
            Assert.Equal("2x", m.Rest);
        }

        [Fact]
        public void Marker_alone_yields_empty_rest()
        {
            // Ligne en cours de frappe : « = » seul → match, reste vide
            // (la couche chaîne peut attendre la suite).
            var m = RelationLineDetector.TryDetect("= ");
            Assert.NotNull(m);
            Assert.Equal("", m.Rest);
        }

        [Theory]
        [InlineData("f(x) = 2x")]   // relation au MILIEU : pas une ligne de chaîne
        [InlineData("2x+1")]
        [InlineData("soit x")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("-> 1/x")]      // flèche de fonction : PAS un connecteur de chaîne
        [InlineData("approximation de pi")] // frontière de mot : « approx » ne matche pas
        [InlineData("environnement")]       // frontière de mot : « environ »/« env » ne matchent pas
        [InlineData("enveloppe convexe")]   // idem : « env » ne capture pas « enveloppe »
        public void Non_chain_lines_yield_null(string line)
            => Assert.Null(RelationLineDetector.TryDetect(line));

        [Fact]
        public void Trailing_paragraph_mark_is_stripped_from_rest()
        {
            var m = RelationLineDetector.TryDetect("= 2x\r");
            Assert.Equal("2x", m.Rest);
        }
    }
}
