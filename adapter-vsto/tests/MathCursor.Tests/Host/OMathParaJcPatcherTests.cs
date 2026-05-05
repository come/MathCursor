using MathCursor.Host;
using Xunit;

namespace MathCursor.Tests.Host
{
    /// <summary>
    /// Tests pour <see cref="OMathParaJcPatcher"/>. Garantit que les OMaths
    /// qu'on insère sont en display avec <c>m:jc=left</c> dès l'origine.
    /// <para>
    /// Limite Word documentée : si l'utilisateur fusionne les ¶s via Backspace,
    /// Word strip activement le <c>&lt;m:oMathParaPr&gt;</c> et l'OMath repasse
    /// en centré. Aucun fix XML possible (le `<w:jc>` du paragraphe est ignoré
    /// par Word pour display math). Cf. ADR
    /// <c>2026-05-05-Limit-omath-jc-stripped-on-fusion</c>.
    /// </para>
    /// </summary>
    public sealed class OMathParaJcPatcherTests
    {
        // ═══════════════════════════════════════════════════════════════
        //  EnsureDisplayWithLeftJc : entry point principal utilisé par
        //  SuggestionService pour aligner les OMaths qu'on insère.
        // ═══════════════════════════════════════════════════════════════

        [Fact(DisplayName = "Ensure : XML inline standalone → enrobe en oMathPara+jc=left")]
        public void Ensure_InlineOMathStandalone_WrapsWithDisplayJcLeft()
        {
            // Captured XML d'une single-line `Y=2X+1` que Word produit en
            // inline. Sans pré-wrap, Word l'auto-promote en oMathPara sans
            // jc → centré par défaut. On enrobe nous-mêmes pour forcer left.
            string input = "<w:p><w:pPr/><m:oMath><m:r><m:t>Y=2X+1</m:t></m:r></m:oMath></w:p>";

            string ensured = OMathParaJcPatcher.EnsureDisplayWithLeftJc(input, out bool changed);

            Assert.True(changed);
            Assert.Contains("<m:oMathPara><m:oMathParaPr><m:jc m:val=\"left\"/></m:oMathParaPr><m:oMath>", ensured);
            Assert.Contains("</m:oMath></m:oMathPara>", ensured);
            Assert.Contains("Y=2X+1", ensured);
        }

        [Fact(DisplayName = "Ensure : XML déjà oMathPara sans jc → délègue à Patch (cas 4)")]
        public void Ensure_AlreadyOMathParaNoJc_PatchAddsJc()
        {
            string input = "<m:oMathPara><m:oMath>x</m:oMath></m:oMathPara>";

            string ensured = OMathParaJcPatcher.EnsureDisplayWithLeftJc(input, out bool changed);

            Assert.True(changed);
            Assert.Contains("<m:oMathParaPr><m:jc m:val=\"left\"/></m:oMathParaPr>", ensured);
        }

        [Fact(DisplayName = "Ensure : XML déjà oMathPara avec jc=left → no-op (idempotent)")]
        public void Ensure_AlreadyJcLeft_NoOp()
        {
            string input = "<m:oMathPara><m:oMathParaPr><m:jc m:val=\"left\"/></m:oMathParaPr><m:oMath>x</m:oMath></m:oMathPara>";

            string ensured = OMathParaJcPatcher.EnsureDisplayWithLeftJc(input, out bool changed);

            Assert.False(changed);
            Assert.Equal(input, ensured);
        }

        [Fact(DisplayName = "Ensure : oMathPara avec jc=center → remplace par left")]
        public void Ensure_JcCenter_ReplacesWithLeft()
        {
            string input = "<m:oMathPara><m:oMathParaPr><m:jc m:val=\"center\"/></m:oMathParaPr><m:oMath>x</m:oMath></m:oMathPara>";

            string ensured = OMathParaJcPatcher.EnsureDisplayWithLeftJc(input, out bool changed);

            Assert.True(changed);
            Assert.Contains("<m:jc m:val=\"left\"/>", ensured);
            Assert.DoesNotContain("<m:jc m:val=\"center\"/>", ensured);
        }

        [Fact(DisplayName = "Ensure : pas de math du tout → no-op")]
        public void Ensure_NoMath_NoOp()
        {
            string input = "<w:p><w:r><w:t>Soit f une fonction</w:t></w:r></w:p>";

            string ensured = OMathParaJcPatcher.EnsureDisplayWithLeftJc(input, out bool changed);

            Assert.False(changed);
            Assert.Equal(input, ensured);
        }

        [Fact(DisplayName = "Ensure : null/empty → no-op (no crash)")]
        public void Ensure_NullEmpty_NoOp()
        {
            Assert.Null(OMathParaJcPatcher.EnsureDisplayWithLeftJc(null, out bool c1));
            Assert.False(c1);
            Assert.Equal("", OMathParaJcPatcher.EnsureDisplayWithLeftJc("", out bool c2));
            Assert.False(c2);
        }

        // ═══════════════════════════════════════════════════════════════
        //  Patch : helper bas niveau utilisé par EnsureDisplayWithLeftJc
        //  quand le wrapper <m:oMathPara> existe déjà.
        // ═══════════════════════════════════════════════════════════════

        [Fact(DisplayName = "Patch : Cas 1 m:jc existe → remplace val")]
        public void Patch_Case1_ExistingJc_ReplacesVal()
        {
            string input = "<m:oMathPara><m:oMathParaPr><m:jc m:val=\"center\"/></m:oMathParaPr><m:oMath>...</m:oMath></m:oMathPara>";

            string patched = OMathParaJcPatcher.Patch(input, "left", out bool changed);

            Assert.True(changed);
            Assert.Contains("<m:jc m:val=\"left\"/>", patched);
            Assert.DoesNotContain("<m:jc m:val=\"center\"/>", patched);
        }

        [Fact(DisplayName = "Patch : Cas 1 déjà target → no-op (idempotent)")]
        public void Patch_Case1_AlreadyTargetVal_NoChange()
        {
            string input = "<m:oMathPara><m:oMathParaPr><m:jc m:val=\"left\"/></m:oMathParaPr><m:oMath>...</m:oMath></m:oMathPara>";

            string patched = OMathParaJcPatcher.Patch(input, "left", out bool changed);

            Assert.False(changed);
            Assert.Equal(input, patched);
        }

        [Fact(DisplayName = "Patch : Cas 2 oMathParaPr auto-fermant → remplace par bloc complet")]
        public void Patch_Case2_SelfClosingParaPr_ReplacesWithFull()
        {
            string input = "<m:oMathPara><m:oMathParaPr/><m:oMath>...</m:oMath></m:oMathPara>";

            string patched = OMathParaJcPatcher.Patch(input, "left", out bool changed);

            Assert.True(changed);
            Assert.Contains("<m:oMathParaPr><m:jc m:val=\"left\"/></m:oMathParaPr>", patched);
            Assert.DoesNotContain("<m:oMathParaPr/>", patched);
        }

        [Fact(DisplayName = "Patch : Cas 3 oMathParaPr ouvert sans m:jc → injecte m:jc en tête")]
        public void Patch_Case3_OpenParaPrNoJc_InjectsJc()
        {
            string input = "<m:oMathPara><m:oMathParaPr><m:someOtherProp/></m:oMathParaPr><m:oMath>...</m:oMath></m:oMathPara>";

            string patched = OMathParaJcPatcher.Patch(input, "left", out bool changed);

            Assert.True(changed);
            Assert.Contains("<m:oMathParaPr><m:jc m:val=\"left\"/><m:someOtherProp/></m:oMathParaPr>", patched);
        }

        [Fact(DisplayName = "Patch : Cas 4 sans oMathParaPr → injecte tout (cas par défaut Word)")]
        public void Patch_Case4_NoParaPr_Injects()
        {
            string input = "<m:oMathPara><m:oMath>...</m:oMath></m:oMathPara>";

            string patched = OMathParaJcPatcher.Patch(input, "left", out bool changed);

            Assert.True(changed);
            Assert.Contains("<m:oMathParaPr><m:jc m:val=\"left\"/></m:oMathParaPr>", patched);
        }

        [Fact(DisplayName = "Patch : null/empty → no-op")]
        public void Patch_NullEmpty_NoChange()
        {
            string r1 = OMathParaJcPatcher.Patch(null, "left", out bool c1);
            Assert.False(c1);
            Assert.Null(r1);

            string r2 = OMathParaJcPatcher.Patch("", "left", out bool c2);
            Assert.False(c2);
            Assert.Equal("", r2);
        }

        [Fact(DisplayName = "Patch : targetVal null → no-op")]
        public void Patch_NullTarget_NoChange()
        {
            string input = "<m:oMathPara><m:oMath>...</m:oMath></m:oMathPara>";
            string patched = OMathParaJcPatcher.Patch(input, null, out bool changed);
            Assert.False(changed);
            Assert.Equal(input, patched);
        }
    }
}
