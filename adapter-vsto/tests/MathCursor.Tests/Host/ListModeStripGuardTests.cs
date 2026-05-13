using MathCursor.Host;
using Xunit;

namespace MathCursor.Tests.Host
{
    /// <summary>
    /// Tests RED puis GREEN pour la garde du strip list-mode.
    ///
    /// <para>Bug 2026-05-13 reproduit en log user : après cross-merge align*
    /// + Escape, <c>StripListModeMarkerFromCurrentLine</c> effaçait le ¶
    /// entier (OMath comprise) parce qu'il n'avait aucune garde sur la
    /// présence d'une formule dans le ¶. Le test <see cref="Strip_ParaContainsOMath_Refuses"/>
    /// verrouille l'invariant : le strip refuse tout ¶ qui contient une OMath.</para>
    /// </summary>
    public sealed class ListModeStripGuardTests
    {
        [Fact(DisplayName = "Bug 2026-05-13 : ¶ contient une OMath → strip refuse (jamais perdre une formule)")]
        public void Strip_ParaContainsOMath_Refuses()
        {
            // Scénario log user : cross-merge a produit OMath align* en
            // range [0,29], le ¶ contient 1 OMath de 30 chars. Le strip
            // ne doit PAS toucher cette ligne, sinon la formule disparait.
            bool can = ListModeStripGuard.CanStripMarkerFromLine(
                omathsInPara: 1, contentLength: 30);

            Assert.False(can,
                "Le strip ne doit JAMAIS effacer un ¶ contenant une formule — " +
                "cause racine bug 2026-05-13 perte formule après cross-merge + Escape.");
        }

        [Fact(DisplayName = "¶ marker-only court (`= `) → strip autorisé")]
        public void Strip_ShortMarkerOnlyPara_Allows()
        {
            // Cas légitime : list_mode_inject a posé "= " sur le ¶ suivant
            // l'OMath, user fait Enter sur cette ligne marker-only seule →
            // le strip doit nettoyer ce ¶ vide.
            bool can = ListModeStripGuard.CanStripMarkerFromLine(
                omathsInPara: 0, contentLength: 2);

            Assert.True(can,
                "Un ¶ qui ne contient qu'un marker court doit pouvoir être stripé.");
        }

        [Fact(DisplayName = "¶ contient du texte utilisateur (long) → strip refuse")]
        public void Strip_LongTextPara_Refuses()
        {
            // Garde supplémentaire : si le ¶ contient plus que le marker
            // attendu (= du texte utilisateur), refuser le strip pour ne
            // pas effacer ce que l'user a tapé.
            bool can = ListModeStripGuard.CanStripMarkerFromLine(
                omathsInPara: 0, contentLength: 50);

            Assert.False(can,
                "Un ¶ avec du contenu plus long qu'un marker doit être préservé.");
        }
    }
}
