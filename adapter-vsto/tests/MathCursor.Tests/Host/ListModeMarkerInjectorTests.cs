using MathCursor.Host;
using Xunit;

namespace MathCursor.Tests.Host
{
    /// <summary>
    /// Tests pour <see cref="ListModeMarkerInjector"/>.
    /// <para>
    /// Bug user 05-05 : « verifie bien que l'auto nouvelle ligne ne vient
    /// pas manger un espace interparagraphe ». Quand l'user a un ¶ vide
    /// séparateur (ou un ¶ avec contenu) juste après le bloc multi-ligne,
    /// l'injection naïve <c>marker + " "</c> dans ce ¶ détruirait son
    /// contenu / sa nature de séparateur. Le planner doit décider :
    /// </para>
    /// <list type="bullet">
    /// <item><b>¶ host = ¶ neuf créé par nous</b> (OMath était dernier ¶
    /// du doc) → injection directe, pas de <c>\r</c>.</item>
    /// <item><b>¶ host = ¶ pré-existant</b> (vide ou pas) → injection AVEC
    /// <c>\r</c> pour créer un ¶ neuf au marker et préserver l'existant.</item>
    /// </list>
    /// </summary>
    public sealed class ListModeMarkerInjectorTests
    {
        // ─────────────────────────────────────────────────────────────────
        //  Cas safe : OMath était dernier ¶ → AppendEmptyParagraphAfterOMath
        //  vient de créer un ¶ vide neuf pour accueillir le caret.
        //  Comportement attendu : injection directe "marker " sans \r.
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "Host=¶ neuf vide créé par nous → injection directe (pas de \\r)")]
        public void Plan_OurFreshlyCreatedEmpty_DirectInjection_NoNewline()
        {
            var plan = ListModeMarkerInjector.Plan("<=>", hostParaIsOursAndEmpty: true);

            Assert.Equal("<=> ", plan.TextToInsert);
            Assert.Equal(4, plan.CaretOffset);
            Assert.False(plan.CreatesNewParagraph);
        }

        // ─────────────────────────────────────────────────────────────────
        //  CAS DU BUG : User a un ¶ pré-existant après le cross-merge cible.
        //  C'est le scénario que l'user veut protéger.
        //
        //  Doc avant cross-merge :
        //      [¶ X+1=2]
        //      [¶ <=> 2x=4]    ← cible
        //      [¶ ]            ← séparateur user
        //      [¶ Suite du cours]
        //
        //  Sans \r dans l'injection, on écrirait "<=> " DANS le séparateur
        //  user, le détruisant. Avec \r, on insère un ¶ neuf pour le marker
        //  AVANT le séparateur, qui reste intact.
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "Host=¶ user pré-existant → injection AVEC \\r (préserve le ¶ existant)")]
        public void Plan_UserPreExistingParagraph_InsertsParagraphBreak()
        {
            var plan = ListModeMarkerInjector.Plan("<=>", hostParaIsOursAndEmpty: false);

            Assert.Equal("<=> \r", plan.TextToInsert);
            // Caret après "<=> " (positions 0..3 = marker, 4 = espace,
            // 5 = \r). Le caret doit être à 4 = juste après l'espace.
            Assert.Equal(4, plan.CaretOffset);
            Assert.True(plan.CreatesNewParagraph);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Cas particulier du bug : ¶ host est VIDE mais pré-existant
        //  (= séparateur user). Le test vérifie qu'on ne se fait pas avoir
        //  par "ah le ¶ est vide donc on peut écrire dedans" — non, vide
        //  ou pas, s'il n'a pas été créé par nous, on insère un \r.
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "Host=¶ vide pré-existant (séparateur user) → quand même \\r")]
        public void Plan_UserEmptySeparator_StillInsertsParagraphBreak()
        {
            // Un ¶ vide pré-existant (hostParaIsOursAndEmpty=false) est un
            // séparateur intentionnel de l'user. On le préserve en insérant
            // un \r, qui pousse le séparateur d'une ligne plus bas.
            var plan = ListModeMarkerInjector.Plan("=>", hostParaIsOursAndEmpty: false);

            Assert.True(plan.CreatesNewParagraph,
                "Même quand le ¶ host est VIDE, s'il est pré-existant on doit "
                + "le préserver en injectant un \\r — sinon on mange le séparateur user.");
            Assert.Equal("=> \r", plan.TextToInsert);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Marker variants : `=` solo, `<=>` multi-char, Unicode `⇔`
        // ─────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("=", "= \r", 2)]
        [InlineData("<=>", "<=> \r", 4)]
        [InlineData("==>", "==> \r", 4)]
        [InlineData("<==>", "<==> \r", 5)]
        [InlineData("⇔", "⇔ \r", 2)]
        [InlineData("{", "{ \r", 2)]  // Phase 2 cases (ADR 05-05)
        public void Plan_VariousMarkers_PreservesUserContentByDefault(string marker, string expectedText, int expectedCaret)
        {
            var plan = ListModeMarkerInjector.Plan(marker, hostParaIsOursAndEmpty: false);

            Assert.Equal(expectedText, plan.TextToInsert);
            Assert.Equal(expectedCaret, plan.CaretOffset);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Null safety
        // ─────────────────────────────────────────────────────────────────

        [Fact(DisplayName = "marker=null → texte = \" \" (espace seul) sans crash")]
        public void Plan_NullMarker_DoesNotCrash()
        {
            var plan = ListModeMarkerInjector.Plan(null, hostParaIsOursAndEmpty: true);
            Assert.Equal(" ", plan.TextToInsert);
            Assert.Equal(1, plan.CaretOffset);
        }
    }
}
