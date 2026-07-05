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

using MathCursor.UI;
using Xunit;

namespace MathCursor.Tests.UI
{
    /// <summary>
    /// Tests sur les substitutions cosmétiques LaTeX → LaTeX que WpfMathAdapter
    /// applique avant de passer au FormulaControl WpfMath. Cf. ADR
    /// 2026-04-24-Feat-popup-revert-wpfmath et le fichier source pour la liste
    /// des règles.
    ///
    /// Ces substitutions n'affectent QUE l'aperçu popup. Le LaTeX qui part en
    /// OMath final ne passe pas par Adapt — donc Word reçoit le LaTeX original
    /// avec \mathbb, \widehat, etc.
    /// </summary>
    public sealed class WpfMathAdapterTests
    {
        // ---- Cas trivials ----

        [Fact]
        public void Empty_returns_empty()
            => Assert.Equal("", WpfMathAdapter.Adapt(""));

        [Fact]
        public void Null_returns_empty()
            => Assert.Equal("", WpfMathAdapter.Adapt(null));

        [Fact]
        public void Plain_latex_unchanged()
            => Assert.Equal("x + 1", WpfMathAdapter.Adapt("x + 1"));

        // ---- \quad / \qquad : WpfMath 2.1 ne les connaît PAS (FormulaControl
        //      rend son erreur en ROUGE, bug user 2026-07-05). Émis par le moteur
        //      pour la séparation « (E)\quad équation » (·gap). Dégradés en espaces
        //      fines \, (supportées). Aperçu seulement ; Word garde le vrai U+2003. ----

        [Fact]
        public void Quad_is_substituted_no_red_bar()
        {
            var s = WpfMathAdapter.Adapt("(E)\\quad ax^{2}+by+c=0");
            Assert.DoesNotContain("\\quad", s);
            Assert.Contains("\\,", s);
        }

        [Fact]
        public void Qquad_is_substituted()
            => Assert.DoesNotContain("\\qquad", WpfMathAdapter.Adapt("A\\qquad B"));

        // ---- norme : \| → \Vert (WpfMath parse \| mais le rend en barre
        //      SIMPLE ; \Vert rend la double — probes PNG 70/71, user 2026-06-10) ----

        [Fact]
        public void Norm_backslash_pipe_becomes_Vert()
            => Assert.Equal("\\left\\Vert AB\\right\\Vert ",
                WpfMathAdapter.Adapt("\\left\\|AB\\right\\|"));

        // ---- \boxed{X} → X (WpfMath ne connaît pas \boxed ; aperçu cosmétique,
        //      Word reçoit le vrai cadre m:borderBox) ----

        [Theory]
        [InlineData("\\boxed{x=2}", "x=2")]
        [InlineData("a+\\boxed{x}+b", "a+x+b")]
        [InlineData("\\boxed{\\frac{1}{x}}", "\\frac{1}{x}")]          // imbrication
        [InlineData("\\boxed{\\boxed{x}}", "x")]                        // double
        public void Boxed_is_unwrapped_to_content(string input, string expected)
            => Assert.Equal(expected, WpfMathAdapter.Adapt(input));

        // ---- bornes d'intégrale À DROITE : {\int}_a^b (WpfMath empile sinon ;
        //      \nolimits non supporté — probe 52/55, user 2026-06-22) ----

        // Indice braced « _{…} » : groupé + kern \!\! (borne basse rentrée).
        [Theory]
        [InlineData("\\int_{a}^{b} f \\, dx", "{\\int}_{\\!\\!a}^{b} f \\, dx")]
        [InlineData("\\oint_{C} F", "{\\oint}_{\\!\\!C} F")]
        // \iint avec bornes est dégradé en \int\int AVANT le wrap → {\int\int}
        [InlineData("\\iint_{D} f", "{\\int\\int}_{\\!\\!D} f")]
        [InlineData("\\iiint_{V} f", "{\\int\\int\\int}_{\\!\\!V} f")]
        public void Integral_braced_sub_grouped_and_kerned(string input, string expected)
            => Assert.Equal(expected, WpfMathAdapter.Adapt(input));

        // Fallback (^ avant _ : indice pas en position « _{ » juste après ∫) :
        // groupage seul, pas de kern injecté.
        [Theory]
        [InlineData("\\int^{b}_{a} f", "{\\int}^{b}_{a} f")]
        public void Integral_supfirst_grouped_without_kern(string input, string expected)
            => Assert.Equal(expected, WpfMathAdapter.Adapt(input));

        [Theory]
        // intégrale SANS bornes : pas de _/^ → pas de wrap (rien à empiler).
        [InlineData("\\int f \\, dx")]
        [InlineData("\\oint F")]
        public void Integral_without_bounds_not_grouped(string input)
            => Assert.Equal(input, WpfMathAdapter.Adapt(input));

        // ---- \mathbb{X} → |X (pseudo-blackboard popup) ----

        [Theory]
        [InlineData("\\mathbb{R}", "|R")]
        [InlineData("\\mathbb{N}", "|N")]
        [InlineData("\\mathbb{Z}", "|Z")]
        [InlineData("\\mathbb{Q}", "|Q")]
        [InlineData("\\mathbb{C}", "|C")]
        public void Mathbb_replaced_with_pipe_prefix(string input, string expected)
            => Assert.Equal(expected, WpfMathAdapter.Adapt(input));

        [Fact]
        public void Mathbb_in_expression_replaced()
            => Assert.Equal("\\forall x \\in |R", WpfMathAdapter.Adapt("\\forall x \\in \\mathbb{R}"));

        // ---- \widehat → \hat (perte chapeau étendu) ----

        [Theory]
        [InlineData("\\widehat{x}", "\\hat{x}")]
        [InlineData("\\widehat{ABC}", "\\hat{ABC}")]
        public void Widehat_replaced_with_hat(string input, string expected)
            => Assert.Equal(expected, WpfMathAdapter.Adapt(input));

        // ---- \overline → \bar ----

        [Theory]
        [InlineData("\\overline{z}", "\\bar{z}")]
        [InlineData("\\overline{xyz}", "\\bar{xyz}")]
        public void Overline_replaced_with_bar(string input, string expected)
            => Assert.Equal(expected, WpfMathAdapter.Adapt(input));

        // ---- \begin{cases} → \{\matrix{} (empilement natif WpfMath) ----
        // Depuis 2026-05-21 (P9f+), l'empilement passe par \matrix (lignes même
        // taille, rendu natif) au lieu de \stackrel (tailles inégales). Cf.
        // WpfMathAdapter.cs §4.

        [Fact]
        public void Cases_two_lines_become_matrix()
        {
            var got = WpfMathAdapter.Adapt("\\begin{cases} a \\\\ b \\end{cases}");
            Assert.Contains("\\matrix", got);
            Assert.Contains("a", got);
            Assert.Contains("b", got);
            // Une accolade gauche est posée pour signaler la structure cases.
            Assert.Contains("\\{", got);
        }

        [Fact(DisplayName = "Cases avec `&` (alignement intra-bloc) : strip les `&` pour WpfMath")]
        public void Cases_with_ampersand_strips_for_wpfmath()
        {
            // Régression user 05-05 : depuis l'ajout de l'alignement intra-cases
            // (renderer émet `&` pour aligner sur `=`), WpfMath rejetait le LaTeX
            // car `&` est invalide hors environnement matrix → popup ne rendait
            // plus la formule. Fix : strip les `&` dans WpfMathAdapter (le
            // stacking ne supporte pas l'alignement de toute façon).
            var got = WpfMathAdapter.Adapt("\\begin{cases} v & = \\square \\end{cases}");
            // Plus de `&` dans la sortie envoyée à WpfMath
            Assert.DoesNotContain("&", got);
            // Le contenu est préservé
            Assert.Contains("v", got);
            Assert.Contains("\\square", got);
            // Toujours une accolade gauche
            Assert.Contains("\\{", got);
        }

        [Fact(DisplayName = "Cases multi-ligne avec `&` : strip + \\matrix")]
        public void Cases_multiline_with_ampersand_stacks_correctly()
        {
            // 2 lignes avec `&` d'alignement → empiler (via \matrix) sans les `&`.
            var got = WpfMathAdapter.Adapt("\\begin{cases} y & = x+2 \\\\ z & = x+2 \\end{cases}");
            Assert.DoesNotContain("&", got);
            Assert.Contains("\\matrix", got);
            Assert.Contains("y", got);
            Assert.Contains("z", got);
        }

        // ---- \begin{pmatrix}/bmatrix/vmatrix → \left( \matrix{} \right) ----
        // P9f+ (2026-05-21) : WpfMath supporte \matrix{...} avec & et \\
        // pour matrices 2D natives. Cf. doc :
        // https://github.com/sskodje/wpf-math/blob/master/docs/matrices.md

        [Fact]
        public void Pmatrix_becomes_left_paren_matrix_right_paren()
        {
            // 2 éléments verticaux : matrice 1 colonne
            var got = WpfMathAdapter.Adapt("\\begin{pmatrix} 1 \\\\ 2 \\end{pmatrix}");
            Assert.Contains("\\left( \\matrix{", got);
            Assert.Contains("\\right)", got);
        }

        [Fact]
        public void Pmatrix_2d_preserves_amp_and_doubleslash()
        {
            // Matrice 2D avec colonnes (P9f) : \matrix{} accepte
            // a & b \\ c & d natively (= cells & rows WpfMath syntax)
            var got = WpfMathAdapter.Adapt(
                "\\begin{pmatrix} a & b \\\\ c & d \\end{pmatrix}");
            Assert.Contains("a & b", got);
            Assert.Contains("c & d", got);
            Assert.Contains("\\left( \\matrix{", got);
        }

        [Fact]
        public void Bmatrix_replaced_with_brackets()
        {
            var got = WpfMathAdapter.Adapt("\\begin{bmatrix} 1 \\\\ 2 \\end{bmatrix}");
            // Pour bmatrix le fallback inline garde [ et ]
            Assert.Contains("[", got);
            Assert.Contains("]", got);
        }

        // ---- Substitutions littérales ----

        [Theory]
        [InlineData("\\setminus", "\\backslash")]
        [InlineData("\\mapsto", "\\to")]
        [InlineData("\\bmod", "\\,\\mathrm{mod}\\,")]
        public void Literal_substitution(string input, string expected)
            => Assert.Equal(expected, WpfMathAdapter.Adapt(input));

        // \iint/\iiint SANS bornes : gérés en amont par MixedLatexRenderer
        // (TextBlock Unicode ∬ / ∭), n'atteignent jamais Adapt. AVEC bornes
        // (\iint_{a}^{b}) ils RESTENT dans le segment WpfMath (le ∬ Unicode
        // ne porte pas de scripts) et Adapt les dégrade en intégrales simples
        // enchaînées (audit popup 2026-06-10), PUIS le run de ∫ est groupé pour
        // que les bornes passent à droite (subSup, user 2026-06-22). \oint nu :
        // pass-through natif (pas de bornes → pas de groupe).
        [Theory]
        [InlineData("\\iint_{0}^{1} f", "{\\int\\int}_{\\!\\!0}^{1} f")]
        [InlineData("\\iiint_{0}^{1} f", "{\\int\\int\\int}_{\\!\\!0}^{1} f")]
        [InlineData("\\oint", "\\oint")]
        public void Integrals_with_bounds_degrade_to_chained_ints(string input, string expected)
            => Assert.Equal(expected, WpfMathAdapter.Adapt(input));

        [Fact]
        public void Limsup_decomposes()
            => Assert.Equal("\\lim\\sup_{n \\to \\infty}",
                WpfMathAdapter.Adapt("\\limsup_{n \\to \\infty}"));

        // ---- Combinaisons (les substitutions doivent composer correctement) ----

        [Fact]
        public void Mathbb_inside_widehat_both_replaced()
        {
            // \widehat{\mathbb{R}} → \hat{|R}
            // Les deux substitutions sont indépendantes ; l'ordre dans Adapt
            // doit produire l'effet cumulé.
            var got = WpfMathAdapter.Adapt("\\widehat{\\mathbb{R}}");
            // Au minimum, plus de \mathbb et plus de \widehat.
            Assert.DoesNotContain("\\mathbb", got);
            Assert.DoesNotContain("\\widehat", got);
        }

        [Fact]
        public void Vec_passes_through_unchanged()
        {
            // \vec n'est pas dans la liste de substitutions — WpfMath le supporte
            // nativement. Doit être inchangé.
            Assert.Equal("\\vec{u}", WpfMathAdapter.Adapt("\\vec{u}"));
            Assert.Equal("\\vec{AB}", WpfMathAdapter.Adapt("\\vec{AB}"));
        }
    }
}
