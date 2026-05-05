namespace MathCursor.Host
{
    /// <summary>
    /// Plan d'injection du marker visible (ADR 05-05 visible).
    /// </summary>
    internal sealed class InjectionPlan
    {
        /// <summary>Texte exact à insérer à la position caret.</summary>
        public string TextToInsert { get; set; }

        /// <summary>
        /// Décalage du caret depuis la position d'insertion. Le caret doit
        /// finir juste après <c>marker + " "</c> pour que l'user puisse
        /// taper la suite.
        /// </summary>
        public int CaretOffset { get; set; }

        /// <summary>
        /// True si le plan crée un nouveau ¶ (= injecte un <c>\r</c>) pour
        /// que le marker ait son ¶ à lui sans manger un ¶ user.
        /// </summary>
        public bool CreatesNewParagraph { get; set; }
    }

    /// <summary>
    /// Décide comment injecter le marker visible post cross-merge sans
    /// corrompre le contenu utilisateur en aval (cf. ADR 05-05 visible +
    /// retour user 2026-05-05 : « verifie que l'auto nouvelle ligne ne
    /// vient pas manger un espace interparagraphe »).
    /// <para>
    /// Cas dangereux : si l'user a un ¶ vide séparateur (ou un ¶ avec
    /// contenu) juste après le bloc multi-ligne, écrire <c>marker + " "</c>
    /// directement dans ce ¶ détruirait son contenu / sa nature de
    /// séparateur. Solution : injecter <c>marker + " \r"</c> qui crée un
    /// nouveau ¶ pour le marker tout en préservant le ¶ existant.
    /// </para>
    /// <para>
    /// Le seul cas où on injecte sans <c>\r</c> est quand notre propre
    /// pipeline (<c>AppendEmptyParagraphAfterOMath</c>) vient juste de
    /// créer un ¶ vide pour accueillir le caret (= OMath était dernier ¶
    /// du doc). Là, le ¶ d'accueil est NEUF et VIDE, on peut taper dedans.
    /// </para>
    /// </summary>
    internal static class ListModeMarkerInjector
    {
        /// <summary>
        /// Calcule le plan d'injection.
        /// </summary>
        /// <param name="marker">Marker à injecter (`<=>`, `=>`, `=`...).</param>
        /// <param name="hostParaIsOursAndEmpty">
        /// True si le ¶ d'ancrage a été créé fraîchement par nous (cas OMath
        /// = dernier ¶ du doc, AppendEmptyParagraphAfterOMath a déclenché
        /// InsertParagraphAfter) — alors injection directe sans <c>\r</c>.
        /// False sinon (¶ pré-existant, vide ou pas) — on insère AVEC
        /// <c>\r</c> pour créer notre ¶ et préserver l'existant.
        /// </param>
        public static InjectionPlan Plan(string marker, bool hostParaIsOursAndEmpty)
        {
            string text = (marker ?? string.Empty) + " ";
            int caretOffset = text.Length;
            bool createsNew = !hostParaIsOursAndEmpty;
            if (createsNew)
            {
                // \r = ¶ break Word. Le marker se retrouve dans son propre ¶,
                // le contenu pré-existant reste dans son ¶ d'origine en aval.
                text = text + "\r";
            }
            return new InjectionPlan
            {
                TextToInsert = text,
                CaretOffset = caretOffset,
                CreatesNewParagraph = createsNew,
            };
        }
    }
}
