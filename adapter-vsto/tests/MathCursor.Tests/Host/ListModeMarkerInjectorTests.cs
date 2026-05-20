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
        //  Bug user 2026-05-07 : ¶ d'accueil contient DÉJÀ le marker.
        //
        //  Scénario : user fait un système ou une chaîne d'équivalences.
        //  Le marker initial est injecté ("{ " ou "<=> "). User edit la
        //  ligne du dessus, voit une erreur, revert+reconvertit. Sans le
        //  fix, on rentre en cas 3 (¶ "non vide") et on injecte un nouveau
        //  marker + \r → marker dupliqué et ligne vide qui pollue le doc.
        //
        //  Fix : si le ¶ contient JUSTE le marker (avec ou sans espace
        //  trailing), on ne réinjecte rien et on positionne le caret à la
        //  suite du marker existant.
        // ─────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("<=>", "<=> \r")]   // marker + espace + \r de fin de ¶
        [InlineData("<=>", "<=> ")]     // marker + espace, sans \r
        [InlineData("<=>", "<=>\r")]    // marker sans espace + \r
        [InlineData("<=>", "<=>")]      // marker brut
        [InlineData("{",   "{ \r")]     // système (Phase 2)
        [InlineData("{",   "{")]
        [InlineData("=",   "= \r")]
        [InlineData("=>",  "=> ")]
        public void Plan_existingParaIsJustMarker_NoInsertCaretAfterMarker(
            string marker, string existingParaContent)
        {
            var plan = ListModeMarkerInjector.Plan(
                marker, hostParaIsOursAndEmpty: false, existingParaContent: existingParaContent);

            Assert.Equal(string.Empty, plan.TextToInsert);
            // CaretOffset doit positionner le caret juste après "marker + ' '"
            // (qu'il y ait ou non l'espace dans l'existing — dans le cas sans
            // espace, le caret tombe quand même à la position attendue
            // post-injection logique, càd qu'on aurait inséré "marker + ' '").
            Assert.Equal(marker.Length + 1, plan.CaretOffset);
            Assert.False(plan.CreatesNewParagraph);
        }

        [Fact(DisplayName = "¶ contient marker + contenu utilisateur → quand même \\r (cas 3 préservé)")]
        public void Plan_existingParaHasMarkerPlusUserContent_StillInsertsParagraphBreak()
        {
            // L'user a tapé "<=> 2x = 4" sur la ligne, puis reverted+reconverted.
            // Le ¶ d'accueil contient maintenant ce contenu utilisateur, qu'on
            // ne doit PAS écraser. → cas 3, on insère avec \r.
            var plan = ListModeMarkerInjector.Plan(
                "<=>", hostParaIsOursAndEmpty: false, existingParaContent: "<=> 2x = 4\r");

            Assert.Equal("<=> \r", plan.TextToInsert);
            Assert.True(plan.CreatesNewParagraph);
        }

        [Fact(DisplayName = "¶ vide pré-existant + existingParaContent fourni → cas 3 (\\r)")]
        public void Plan_existingParaEmpty_StillInsertsParagraphBreak()
        {
            // ¶ vide pré-existant (séparateur user) — pas le cas marker-only.
            // existingParaContent = "\r" (juste le marqueur de fin de ¶).
            var plan = ListModeMarkerInjector.Plan(
                "<=>", hostParaIsOursAndEmpty: false, existingParaContent: "\r");

            Assert.Equal("<=> \r", plan.TextToInsert);
            Assert.True(plan.CreatesNewParagraph);
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
