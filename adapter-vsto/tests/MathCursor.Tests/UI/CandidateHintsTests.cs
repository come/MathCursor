using MathCursor.UI;
using Xunit;

namespace MathCursor.Tests.UI
{
    /// <summary>
    /// Badge popup (cadrage user 2026-06-12) : l'aperçu WpfMath aplatit les
    /// matrices, « (1 2) » ligne ≈ « (1,2) » tuple — l'étiquette lève
    /// l'ambiguïté sans rien retirer de la popup.
    /// </summary>
    public class CandidateHintsTests
    {
        [Theory]
        [InlineData("\\begin{pmatrix}1 & 2\\end{pmatrix}", "matrice ligne")]
        [InlineData("\\begin{pmatrix}1 \\\\ 2\\end{pmatrix}", "colonne")]
        [InlineData("\\begin{pmatrix}a & b \\\\ c & d\\end{pmatrix}", "matrice")]
        [InlineData("A\\begin{pmatrix}1 & 2\\end{pmatrix}", "matrice ligne")]
        [InlineData("\\begin{pmatrix}x \\\\ y\\end{pmatrix}\\in R^{2}", "colonne")]
        public void Matrix_candidates_get_a_hint(string latex, string expected)
            => Assert.Equal(expected, CandidateHints.GetHint(latex));

        [Theory]
        [InlineData("(1,2)")]                       // tuple : les virgules se lisent
        [InlineData("(O,\\vec{i},\\vec{j})")]       // repère
        [InlineData("\\sum_{k=1}^{n} f(k)")]
        [InlineData("x=1,y=2")]
        [InlineData("")]
        [InlineData(null)]
        public void Plain_candidates_get_none(string latex)
            => Assert.Null(CandidateHints.GetHint(latex));
    }
}
