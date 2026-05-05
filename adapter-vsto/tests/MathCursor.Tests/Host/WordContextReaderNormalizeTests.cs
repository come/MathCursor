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
        [InlineData("l’élève", "l'élève")] // l'élève
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

        [Theory(DisplayName = "Caractères non-ciblés (NBSP, accents, math) préservés")]
        [InlineData(" ", " ")]   // NBSP géré côté Lexer, pas ici
        [InlineData("éèê", "éèê")] // éèê
        [InlineData("∀x ∈ R", "∀x ∈ R")] // ∀ ∈ : math, pas autocorrect
        [InlineData("α+β=γ", "α+β=γ")] // α+β=γ
        public void NonTargetedChars_Preserved(string input, string expected)
        {
            Assert.Equal(expected, AutocorrectNormalizer.Normalize(input));
        }

        // ─── Mix réaliste ──────────────────────────────────────────────────

        [Fact(DisplayName = "Phrase mixte AutoCorrect (dash + apostrophe + guillemets)")]
        public void MixedAutocorrect_AllNormalized()
        {
            // « L'élève a écrit "a – b = c" dans son cahier. »
            const string input = "L’élève a écrit “a – b = c” dans son cahier.";
            const string expected = "L'élève a écrit \"a - b = c\" dans son cahier.";

            Assert.Equal(expected, AutocorrectNormalizer.Normalize(input));
        }
    }
}
