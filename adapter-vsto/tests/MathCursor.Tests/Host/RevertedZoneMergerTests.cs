using System.Collections.Generic;
using MathCursor.Host;
using Xunit;

namespace MathCursor.Tests.Host
{
    /// <summary>
    /// Tests pour <see cref="RevertedZoneMerger"/> — logique de cascade Mode 2
    /// (revert d'un OMath multi-ligne, puis re-conversion).
    /// <para>
    /// Reproduit le bug user 05-05 : multi-ligne 3 lignes, revert, modifier
    /// ligne 1, commit ligne 1 → on doit produire "newLine1\nline2\nline3"
    /// (= ligne committée remplacée), pas "line1\nline2\nnewLine1" (= ancien
    /// comportement hardcodé sur dernier index, qui faisait un "merge
    /// descendant" en écrasant la dernière ligne avec le contenu de la 1re).
    /// </para>
    /// </summary>
    public sealed class RevertedZoneMergerTests
    {
        // ─────────────────────────────────────────────────────────────────
        //  Bug 05-05 : commit sur ligne 1 d'un revert 3-lignes
        //  Attendu : ligne 1 remplacée par currentSource, lignes 2-3 préservées
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "Bug 05-05 : commit ligne 1 doit replacer ligne 1, PAS la dernière")]
        public void CommitOnFirstLine_OfThreeLineRevert_ReplacesFirstLine()
        {
            // Scénario : multi-ligne créé via "X+1\n= X+2\n= X+3", revert,
            // modifier ligne 1 (de "X+1" à "X+5"), commit sur ligne 1.
            var paraTexts = new List<string> { "X+1", "= X+2", "= X+3" };
            var paraStarts = new List<int> { 0, 4, 11 };
            // User commit sur ligne 1 → absStart = 0 (au début de ligne 1)
            // currentSource = "X+5" (la modification user)
            var merged = RevertedZoneMerger.BuildMergedSource(
                paraTexts, paraStarts, absStart: 0, currentSource: "X+5");

            // Comportement attendu : ligne 1 remplacée, lignes 2 & 3 préservées
            Assert.Equal("X+5\n= X+2\n= X+3", merged);
        }

        [Fact]
        public void CommitOnMiddleLine_ReplacesMiddleLine()
        {
            var paraTexts = new List<string> { "X+1", "= X+2", "= X+3" };
            var paraStarts = new List<int> { 0, 4, 11 };
            // Commit sur ligne 2 (absStart entre 4 et 10)
            var merged = RevertedZoneMerger.BuildMergedSource(
                paraTexts, paraStarts, absStart: 5, currentSource: "= Y+9");

            Assert.Equal("X+1\n= Y+9\n= X+3", merged);
        }

        [Fact]
        public void CommitOnLastLine_ReplacesLastLine()
        {
            var paraTexts = new List<string> { "X+1", "= X+2", "= X+3" };
            var paraStarts = new List<int> { 0, 4, 11 };
            // Commit sur ligne 3 (absStart >= 11)
            var merged = RevertedZoneMerger.BuildMergedSource(
                paraTexts, paraStarts, absStart: 11, currentSource: "= 99");

            Assert.Equal("X+1\n= X+2\n= 99", merged);
        }

        [Fact]
        public void CommitOnLineBoundary_PicksRightLine()
        {
            // Edge case : absStart == paraStart d'une ligne donnée (= début de
            // cette ligne) → c'est cette ligne qu'on remplace, pas la précédente.
            var paraTexts = new List<string> { "A", "B", "C" };
            var paraStarts = new List<int> { 0, 2, 4 };
            // absStart = 2 = exactement le début de ligne 2 → remplace ligne 2
            var merged = RevertedZoneMerger.BuildMergedSource(
                paraTexts, paraStarts, absStart: 2, currentSource: "B'");

            Assert.Equal("A\nB'\nC", merged);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Cas 2-lignes (= revert classique)
        // ─────────────────────────────────────────────────────────────────

        [Fact]
        public void TwoLine_CommitOnLine1_ReplacesLine1()
        {
            var paraTexts = new List<string> { "X+1=2", "<=> x=1" };
            var paraStarts = new List<int> { 0, 6 };
            var merged = RevertedZoneMerger.BuildMergedSource(
                paraTexts, paraStarts, absStart: 0, currentSource: "X+1=10");
            Assert.Equal("X+1=10\n<=> x=1", merged);
        }

        [Fact]
        public void TwoLine_CommitOnLine2_ReplacesLine2()
        {
            var paraTexts = new List<string> { "X+1=2", "<=> x=1" };
            var paraStarts = new List<int> { 0, 6 };
            var merged = RevertedZoneMerger.BuildMergedSource(
                paraTexts, paraStarts, absStart: 6, currentSource: "<=> x=99");
            Assert.Equal("X+1=2\n<=> x=99", merged);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Edge cases
        // ─────────────────────────────────────────────────────────────────

        [Fact]
        public void EmptyParagraphList_ReturnsCurrentSource()
        {
            var merged = RevertedZoneMerger.BuildMergedSource(
                new List<string>(), new List<int>(), absStart: 0, currentSource: "abc");
            Assert.Equal("abc", merged);
        }

        [Fact]
        public void NullCurrentSource_TreatedAsEmpty()
        {
            var paraTexts = new List<string> { "A", "B" };
            var paraStarts = new List<int> { 0, 2 };
            var merged = RevertedZoneMerger.BuildMergedSource(
                paraTexts, paraStarts, absStart: 2, currentSource: null);
            Assert.Equal("A\n", merged);
        }

        [Fact]
        public void MismatchedListLengths_FallbackToCurrentSource()
        {
            var paraTexts = new List<string> { "A", "B" };
            var paraStarts = new List<int> { 0 }; // longueur 1, paraTexts longueur 2
            var merged = RevertedZoneMerger.BuildMergedSource(
                paraTexts, paraStarts, absStart: 0, currentSource: "X");
            Assert.Equal("X", merged);
        }
    }
}
