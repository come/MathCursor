using System.Collections.Generic;
using MathCursor.Host;
using Xunit;

namespace MathCursor.Tests.Host
{
    /// <summary>
    /// Tests pour <see cref="CasesCascadeMerger"/> — logique pure de cascade
    /// montante pour les systèmes d'équations <c>{</c> (Phase 2 cases, ADR
    /// 05-05). Reproduit la sémantique du brief 30-04 §2.1 et 3.4 :
    /// <list type="bullet">
    /// <item>Cascade absorbe tant que les ¶ précédents commencent par <c>{ </c></item>
    /// <item>Stop sur ¶ vide (barrier)</item>
    /// <item>Stop sur marker non-cases (pas de mix avec align)</item>
    /// <item>Pas de cascade si le current source n'est pas un cases</item>
    /// </list>
    /// </summary>
    public sealed class CasesCascadeMergerTests
    {
        // ─────────────────────────────────────────────────────────────────
        //  Cas négatifs : pas de cascade
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "Current source pas un cases → null (pas de cascade)")]
        public void CurrentSourceNotCases_ReturnsNull()
        {
            var result = CasesCascadeMerger.BuildCascade(
                paragraphsAbove: new List<string> { "{ x=1" },
                currentSource: "<=> z=0");

            Assert.Null(result);
        }

        [Fact(DisplayName = "Current source cases mais aucun ¶ au-dessus à absorber → null")]
        public void CurrentSourceCases_NothingToAbsorb_ReturnsNull()
        {
            var result = CasesCascadeMerger.BuildCascade(
                paragraphsAbove: new List<string>(),
                currentSource: "{ x=1");

            Assert.Null(result);
        }

        [Fact(DisplayName = "¶ au-dessus pas un cases (texte normal) → null")]
        public void ParagraphAboveNotCases_ReturnsNull()
        {
            var result = CasesCascadeMerger.BuildCascade(
                paragraphsAbove: new List<string> { "Soit x un réel" },
                currentSource: "{ x=1");

            Assert.Null(result);
        }

        [Fact(DisplayName = "¶ au-dessus marker align (mix interdit) → null")]
        public void ParagraphAboveIsAlignMarker_NoMix_ReturnsNull()
        {
            // Cf. brief 30-04 §3.4 : pas de mix cases ↔ align.
            var result = CasesCascadeMerger.BuildCascade(
                paragraphsAbove: new List<string> { "<=> z=0" },
                currentSource: "{ x=1");

            Assert.Null(result);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Cas positifs : cascade
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "2 lignes cases consécutives → cascade absorbe 1, mergedSource concat")]
        public void TwoCasesConsecutive_CascadeOfOne()
        {
            var result = CasesCascadeMerger.BuildCascade(
                paragraphsAbove: new List<string> { "{ x=1" },
                currentSource: "{ y=2");

            Assert.NotNull(result);
            Assert.Equal(1, result.AbsorbedCount);
            Assert.Equal("{ x=1\n{ y=2", result.MergedSource);
        }

        [Fact(DisplayName = "3 lignes cases consécutives → cascade absorbe 2")]
        public void ThreeCasesConsecutive_CascadeOfTwo()
        {
            var result = CasesCascadeMerger.BuildCascade(
                paragraphsAbove: new List<string> { "{ x=1", "{ y=2" },
                currentSource: "{ z=3");

            Assert.NotNull(result);
            Assert.Equal(2, result.AbsorbedCount);
            Assert.Equal("{ x=1\n{ y=2\n{ z=3", result.MergedSource);
        }

        [Fact(DisplayName = "Cascade s'arrête sur ¶ vide (barrier)")]
        public void CascadeStopsOnEmptyParagraph()
        {
            // Liste paragraphsAbove du HAUT vers le BAS (index 0 = topmost).
            // On walk de bas en haut : "{ y=2" absorbé, "" → stop.
            // L'ancien { x=1 (au-dessus du vide) n'est PAS absorbé.
            var result = CasesCascadeMerger.BuildCascade(
                paragraphsAbove: new List<string> { "{ x=1", "", "{ y=2" },
                currentSource: "{ z=3");

            Assert.NotNull(result);
            Assert.Equal(1, result.AbsorbedCount);
            Assert.Equal("{ y=2\n{ z=3", result.MergedSource);
        }

        [Fact(DisplayName = "Cascade s'arrête sur ¶ texte normal (non-cases, non-align)")]
        public void CascadeStopsOnRegularText()
        {
            var result = CasesCascadeMerger.BuildCascade(
                paragraphsAbove: new List<string> { "{ x=1", "Soit le système suivant", "{ y=2" },
                currentSource: "{ z=3");

            Assert.NotNull(result);
            Assert.Equal(1, result.AbsorbedCount);
            Assert.Equal("{ y=2\n{ z=3", result.MergedSource);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Edge cases
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "Leading whitespace dans ¶ cases → toléré (TrimStart)")]
        public void LeadingWhitespaceInCasesParagraph_Tolerated()
        {
            var result = CasesCascadeMerger.BuildCascade(
                paragraphsAbove: new List<string> { "  { x=1" },
                currentSource: "{ y=2");

            Assert.NotNull(result);
            Assert.Equal(1, result.AbsorbedCount);
            // mergedSource préserve la ligne TELLE QUELLE (TrimStart est juste
            // pour la détection, pas pour la transformation)
            Assert.Equal("  { x=1\n{ y=2", result.MergedSource);
        }

        [Fact(DisplayName = "{ sans espace dans ¶ → pas absorbé (set en extension)")]
        public void BraceWithoutSpace_NotAbsorbed()
        {
            // `{1,2}` ou `{x=1` ne sont PAS des systèmes (pas d'espace après {)
            var result = CasesCascadeMerger.BuildCascade(
                paragraphsAbove: new List<string> { "{1,2}" },
                currentSource: "{ y=2");

            Assert.Null(result);
        }

        [Fact(DisplayName = "Null safety : paragraphsAbove null → null")]
        public void NullParagraphsAbove_ReturnsNull()
        {
            var result = CasesCascadeMerger.BuildCascade(
                paragraphsAbove: null,
                currentSource: "{ x=1");

            Assert.Null(result);
        }

        [Fact(DisplayName = "Null safety : currentSource null → null")]
        public void NullCurrentSource_ReturnsNull()
        {
            var result = CasesCascadeMerger.BuildCascade(
                paragraphsAbove: new List<string> { "{ x=1" },
                currentSource: null);

            Assert.Null(result);
        }

        // ─────────────────────────────────────────────────────────────────
        //  StartsWithCasesMarker : helper de détection
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "StartsWithCasesMarker : `{ x=1` → true")]
        public void StartsWithCasesMarker_BraceWithSpace_True()
        {
            Assert.True(CasesCascadeMerger.StartsWithCasesMarker("{ x=1"));
            Assert.True(CasesCascadeMerger.StartsWithCasesMarker("{ "));
            Assert.True(CasesCascadeMerger.StartsWithCasesMarker("  { x=1"));  // leading ws
        }

        [Fact(DisplayName = "StartsWithCasesMarker : sans espace après { → false")]
        public void StartsWithCasesMarker_BraceWithoutSpace_False()
        {
            Assert.False(CasesCascadeMerger.StartsWithCasesMarker("{1,2}"));
            Assert.False(CasesCascadeMerger.StartsWithCasesMarker("{x=1"));
            Assert.False(CasesCascadeMerger.StartsWithCasesMarker("{}"));
            Assert.False(CasesCascadeMerger.StartsWithCasesMarker("{"));
        }

        [Fact(DisplayName = "StartsWithCasesMarker : null/empty → false")]
        public void StartsWithCasesMarker_NullEmpty_False()
        {
            Assert.False(CasesCascadeMerger.StartsWithCasesMarker(null));
            Assert.False(CasesCascadeMerger.StartsWithCasesMarker(""));
            Assert.False(CasesCascadeMerger.StartsWithCasesMarker("   "));
        }
    }
}
