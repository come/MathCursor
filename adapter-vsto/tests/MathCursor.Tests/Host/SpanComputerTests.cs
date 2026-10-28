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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MathCursor.Host;
using Xunit;

namespace MathCursor.Tests.Host
{
    /// <summary>
    /// Span du Ctrl+Espace manuel (ADR 2026-06-18-Fix-input-autocorrect-fraction-
    /// factorial). Régression clé : `!` retiré des délimiteurs → un caret juste
    /// après `n!` capte bien `n!` au lieu d'une span vide (= pas de popup).
    ///
    /// Les cas vivent dans <c>Host/spancomputer-fixtures.txt</c> — SOURCE UNIQUE
    /// partagée avec le port JS (<c>web-demo/.../spancomputer.test.js</c>), pour
    /// que la parité C#↔JS ne puisse plus dériver en silence (ADR 2026-06-23).
    /// </summary>
    public sealed class SpanComputerTests
    {
        private static readonly IReadOnlyList<(int, int)> NoOMath = new List<(int, int)>();

        // Reproduit le flux de ConversionController.Trigger : span brute puis
        // trim des blancs aux deux bouts.
        private static string Span(string text, int caret)
        {
            int s = SpanComputer.ComputeSpanStart(text, caret, NoOMath);
            int e = SpanComputer.ComputeSpanEnd(text, caret, NoOMath);
            while (s < e && char.IsWhiteSpace(text[s])) s++;
            while (e > s && char.IsWhiteSpace(text[e - 1])) e--;
            return text.Substring(s, e - s);
        }

        public static IEnumerable<object[]> ParityCases()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "spancomputer-fixtures.txt");
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var p = line.Split('|');
                yield return new object[] { p[0], p[1], int.Parse(p[2], CultureInfo.InvariantCulture), p[3] };
            }
        }

        [Theory]
        [MemberData(nameof(ParityCases))]
        public void Parity(string name, string text, int caret, string expected)
        {
            var got = Span(text, caret);
            Assert.True(got == expected, $"{name} : attendu \"{expected}\", obtenu \"{got}\"");
        }

        // Ctrl+Espace : débuts candidats, du plus long au plus court ; le contrôleur
        // propose le premier que le moteur lit (ADR 2026-10-27-UX-ctrl-space-engine-driven-start).
        private static string Starts(string zone) =>
            string.Join("|", SpanComputer.WordBoundaryStarts(zone, 0, zone.Length)
                .Select(s => zone.Substring(s)));

        [Theory]
        [InlineData("courbe de f", "courbe de f|de f|f")]
        [InlineData("point d'abscisse a", "point d'abscisse a|d'abscisse a|a")]
        [InlineData("  entre  x", "entre  x|x")]                 // blancs en tête et multiples
        [InlineData("x", "x")]
        [InlineData("(n+1) + 1", "(n+1) + 1|+ 1|1")]
        [InlineData("abs(x - 2) <= 3", "abs(x - 2) <= 3|<= 3|3")]  // jamais dans la parenthèse
        [InlineData("[a ; b] x", "[a ; b] x|x")]
        [InlineData("   ", "")]
        public void WordBoundaryStarts(string zone, string expected)
        {
            Assert.Equal(expected, Starts(zone));
        }

        [Fact]
        public void WordBoundaryStarts_RespecteLesBornesDeLaSpan()
        {
            const string text = "est la distance entre x et 2";
            int start = text.IndexOf("distance", StringComparison.Ordinal);
            int end = text.IndexOf(" et", StringComparison.Ordinal);
            var starts = SpanComputer.WordBoundaryStarts(text, start, end);
            Assert.Equal(new[] { "distance entre x", "entre x", "x" }, starts.Select(s => text.Substring(s, end - s)));
        }

        // Début structurel : délimiteurs, OMath, ouvrante ; JAMAIS de mot-outil.
        [Theory]
        [InlineData("Déterminer la tangente à la courbe de f", "Déterminer la tangente à la courbe de f")]
        [InlineData("Soit f. On remplace a", " On remplace a")]
        [InlineData("y = 2x + 1", " 2x + 1")]
        [InlineData("la matrice (a b ; c d", "(a b ; c d")]
        public void ComputeStructuralSpanStart_SansMotsOutils(string text, string expected)
        {
            int s = SpanComputer.ComputeStructuralSpanStart(text, text.Length, NoOMath);
            Assert.Equal(expected, text.Substring(s));
        }

        // Ctrl+Espace de Word : = < > ne bornent plus (« P(A) = 0,4 » entier, 28/10) ;
        // la ponctuation, si.
        [Theory]
        [InlineData("On sait que P(A) = 0,4", "On sait que P(A) = 0,4")]
        [InlineData("Donc P(B) = 0,42", "Donc P(B) = 0,42")]
        [InlineData("y = 2x + 1", "y = 2x + 1")]
        [InlineData("Soit f. On a f(x) = 2", " On a f(x) = 2")]
        [InlineData("a < b ; c >= d", " c >= d")]
        [InlineData("Soit y ≤ 2x + 1", "Soit y ≤ 2x + 1")]           // ≤ ≥ ≠ Unicode : jamais bornants
        public void ComputeStructuralSpanStart_RelationsNonBornantes(string text, string expected)
        {
            int s = SpanComputer.ComputeStructuralSpanStart(text, text.Length, NoOMath, relationsBound: false);
            Assert.Equal(expected, text.Substring(s));
        }

        [Fact]
        public void ComputeStructuralSpanStart_RelationsBornantesParDefaut()
        {
            const string text = "y = 2x + 1";
            Assert.Equal(" 2x + 1", text.Substring(SpanComputer.ComputeStructuralSpanStart(text, text.Length, NoOMath)));
        }

        [Fact]
        public void ComputeSpanStart_GardeLesMotsOutils()
        {
            const string text = "courbe de f";
            Assert.Equal("f", text.Substring(SpanComputer.ComputeSpanStart(text, text.Length, NoOMath)).Trim());
        }

        // Garde prose du Ctrl+Espace : un mot de la saisie ressort tel quel dans la lecture.
        [Theory]
        [InlineData(@"\lim_{de\to la} suite", "limite de la suite", true)]
        [InlineData(@"\begin{pmatrix}100 & fois\end{pmatrix}", "(100 fois)", true)]
        [InlineData(@"\partial de", "part de", false)]              // mots-clés : non détectés (limite connue)
        [InlineData(@"\forall q\neq 1", "pour q ≠ 1", false)]
        [InlineData(@"ax^{2}+bx+c", "ax2 + bx + c", false)]           // mots de 2 lettres : produits
        [InlineData(@"4a^{2}x^{2}+4abx+b^{2}", "4a2x2 + 4abx + b2", false)] // « abx » touche un chiffre
        [InlineData(@"\sin(x)+\cos(x)", "sin(x) + cos(x)", false)]  // commandes
        [InlineData(@"n=2\,\mathrm{mol}", "n = 2 mol", false)]      // unité
        [InlineData(@"abc+d", "abc + d", true)]                       // produit de 3 lettres : écarté (limite connue)
        public void LatexLooksLikeProse(string latex, string source, bool expected)
        {
            Assert.Equal(expected, SpanComputer.LatexLooksLikeProse(latex, source));
        }
    }
}
