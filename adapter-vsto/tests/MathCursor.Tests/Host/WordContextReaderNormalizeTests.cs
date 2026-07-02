// MathCursor — capture d'intention mathématique depuis une saisie clavier linéaire.
// Copyright (C) 2026  Côme Percin
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

using MathCursor.Host;
using Xunit;

namespace MathCursor.Tests.Host
{
    /// <summary>
    /// Tests pour <see cref="AutocorrectNormalizer.Normalize"/>.
    ///
    /// Cadre le bug user 05-05 : Word AutoCorrect remplace `-` (hyphen-minus
    /// ASCII U+002D) par `–` (en-dash U+2013) entre 2 mots — résultat :
    /// `g(x) - g(x+1) = 1/x` devient `g(x) – g(x+1) = 1/x` dans le doc, ce
    /// que ni le NER ni le lexer ne reconnaissent comme math.
    ///
    /// Doctrine : la normalisation s'applique au lecteur de paragraphe, donc
    /// transparente pour tout le pipeline en aval (NER + lattice).
    ///
    /// **Invariant non négociable** : 1 char d'entrée → 1 char de sortie. Si
    /// la longueur change, les offsets entre Word doc et notre texte interne
    /// désynchronisent → le caret pointe au mauvais endroit, les zones NER
    /// sont décalées, etc.
    /// </summary>
    public sealed class WordContextReaderNormalizeTests
    {
        // ─── Cas spécifique du bug rapporté ──────────────────────────────

        [Fact(DisplayName = "Bug 05-05 : `g(x) – g(x+1) = 1/x` (en-dash) → ASCII")]
        public void Bug_EnDashBetweenFunctions_NormalizedToHyphen()
        {
            // Le `–` au milieu (U+2013) provient de l'AutoCorrect Word :
            // « space-hyphen-space → space-en-dash-space » entre 2 mots.
            const string input = "g(x) – g(x+1) = 1/x";
            const string expected = "g(x) - g(x+1) = 1/x";

            var output = AutocorrectNormalizer.Normalize(input);

            Assert.Equal(expected, output);
        }

        // ─── Catégorie 1 : dashes typographiques → hyphen-minus ──────────

        [Theory(DisplayName = "Dashes Unicode → hyphen-minus ASCII")]
        [InlineData("–", "-")] // en-dash
        [InlineData("—", "-")] // em-dash
        [InlineData("−", "-")] // minus sign mathématique
        [InlineData("a – b", "a - b")]
        [InlineData("a — b", "a - b")]
        [InlineData("x = −5", "x = -5")] // signe moins en notation math
        public void Dashes_NormalizedToHyphenMinus(string input, string expected)
        {
            Assert.Equal(expected, AutocorrectNormalizer.Normalize(input));
        }

        // ─── Catégorie 2 : guillemets simples → apostrophe ASCII ─────────

        [Theory(DisplayName = "Smart single quotes → apostrophe ASCII")]
        [InlineData("‘", "'")] // left single
        [InlineData("’", "'")] // right single (= apostrophe typo)
        [InlineData("‚", "'")] // low single
        [InlineData("′", "'")] // prime (')
        [InlineData("l’élève", "l'eleve")] // apostrophe + accents foldés (2026-06-11)
        public void SingleQuotes_NormalizedToApostrophe(string input, string expected)
        {
            Assert.Equal(expected, AutocorrectNormalizer.Normalize(input));
        }

        // ─── Catégorie 3 : guillemets doubles → quote ASCII ──────────────

        [Theory(DisplayName = "Smart double quotes → \" ASCII")]
        [InlineData("“", "\"")] // left double
        [InlineData("”", "\"")] // right double
        [InlineData("„", "\"")] // low double
        [InlineData("″", "\"")] // double prime
        [InlineData("“math”", "\"math\"")]
        public void DoubleQuotes_NormalizedToQuote(string input, string expected)
        {
            Assert.Equal(expected, AutocorrectNormalizer.Normalize(input));
        }

        // ─── Invariant : 1-pour-1 (longueur préservée) ───────────────────

        [Theory(DisplayName = "Longueur préservée char-pour-char (alignement offsets)")]
        [InlineData("a – b")]
        [InlineData("“hello”")]
        [InlineData("l’élève dit “ca va”")]
        [InlineData("plain ASCII")]
        [InlineData("")]
        public void LengthPreserved(string input)
        {
            var output = AutocorrectNormalizer.Normalize(input);
            Assert.Equal(input.Length, output.Length);
        }

        // ─── Pas de modification si déjà ASCII (fast-path) ───────────────

        [Theory(DisplayName = "Texte déjà ASCII → renvoyé tel quel")]
        [InlineData("g(x) - g(x+1) = 1/x")]
        [InlineData("forall x in R, x^2 >= 0")]
        [InlineData("")]
        [InlineData("Bonjour tout le monde")]
        public void AlreadyAscii_PassThrough(string input)
        {
            Assert.Equal(input, AutocorrectNormalizer.Normalize(input));
        }

        // ─── Cas null (robustesse) ───────────────────────────────────────

        [Fact(DisplayName = "Null input → string vide (jamais throw)")]
        public void NullInput_ReturnsEmptyString()
        {
            Assert.Equal(string.Empty, AutocorrectNormalizer.Normalize(null));
        }

        // ─── Caractères non-ciblés préservés ──────────────────────────────

        // (accents : foldés depuis le 2026-06-11 — « on peut les strip avant
        // d'envoyer au NER » — le lexer jette « caractère inattendu: é »)
        [Theory(DisplayName = "Caractères non-ciblés (NBSP, math, grec) préservés")]
        [InlineData(" ", " ")]   // NBSP géré côté Lexer, pas ici
        [InlineData("∀x ∈ R", "∀x ∈ R")] // ∀ ∈ : math, pas autocorrect
        [InlineData("α+β=γ", "α+β=γ")] // α+β=γ
        [InlineData("a×b", "a×b")] // × = opérateur du vocab moteur, pas foldé
        [InlineData("8÷2", "8÷2")] // ÷ idem
        public void NonTargetedChars_Preserved(string input, string expected)
        {
            Assert.Equal(expected, AutocorrectNormalizer.Normalize(input));
        }

        [Theory(DisplayName = "Accents foldés vers la lettre nue (1:1, avant NER/moteur)")]
        [InlineData("éèê", "eee")]
        [InlineData("Évaluer où ça converge", "Evaluer ou ca converge")]
        public void Accents_FoldedToBareLetter(string input, string expected)
        {
            var output = AutocorrectNormalizer.Normalize(input);
            Assert.Equal(expected, output);
            Assert.Equal(input.Length, output.Length);
        }

        // ─── Mix réaliste ──────────────────────────────────────────────────

        [Fact(DisplayName = "Phrase mixte AutoCorrect (dash + apostrophe + guillemets)")]
        public void MixedAutocorrect_AllNormalized()
        {
            // « L'élève a écrit "a – b = c" dans son cahier. »
            const string input = "L’élève a écrit “a – b = c” dans son cahier.";
            const string expected = "L'eleve a ecrit \"a - b = c\" dans son cahier.";

            Assert.Equal(expected, AutocorrectNormalizer.Normalize(input));
        }

        // ─── Chars de contrôle Word (bug 2026-05-11 cellule de tableau) ──

        [Theory(DisplayName = "Chars de contrôle Word internes → espace (NER en cellule)")]
        [InlineData("x = 1\a", "x = 1 ")]   // cell-end marker (U+0007)
        [InlineData("ligne1\vligne2", "ligne1 ligne2")] // <w:br/> Word
        [InlineData("a\bb", "a b")]          // backspace (rare)
        [InlineData("page1\fpage2", "page1 page2")]    // page break (rare)
        public void WordControlChars_NormalizedToSpace(string input, string expected)
        {
            // Sans normalisation : Range.Text en cellule retourne du
            // texte avec \a en queue → le NER, entraîné sur du texte
            // propre, ne tokenize plus la zone math → popup ne se
            // lève pas. Bug 2026-05-11.
            var output = AutocorrectNormalizer.Normalize(input);
            Assert.Equal(expected, output);
            // Invariant 1:1 : longueur préservée pour aligner
            // text[i] avec paraStart + i côté Word.
            Assert.Equal(input.Length, output.Length);
        }

        [Fact(DisplayName = "Tab et newline préservés (chars de contrôle légitimes)")]
        public void TabAndNewline_Preserved()
        {
            // \t et \r/\n sont des chars de contrôle valides dans
            // Range.Text (tab dans un ¶, séparateur de ¶). Ne pas
            // les normaliser sinon on casse la structure.
            const string input = "col1\tcol2\r\nligne2";
            Assert.Equal(input, AutocorrectNormalizer.Normalize(input));
        }
    }
}
