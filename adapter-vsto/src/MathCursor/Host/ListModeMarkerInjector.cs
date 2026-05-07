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
    /// <para>3 cas :</para>
    /// <list type="number">
    /// <item><b>¶ neuf vide créé par nous</b> (<c>AppendEmptyParagraphAfterOMath</c>
    /// vient de créer un ¶ pour accueillir le caret) → injection directe
    /// <c>marker + " "</c>, sans <c>\r</c>.</item>
    /// <item><b>¶ pré-existant qui contient déjà juste le marker</b> (cas
    /// bug user 2026-05-07 : edit de la ligne du dessus puis reconversion
    /// → le marker est resté en place dans le ¶ d'accueil) → ne rien insérer,
    /// positionner le caret à la suite du marker existant pour éviter la
    /// duplication marker + ¶ vide.</item>
    /// <item><b>¶ pré-existant ≠ marker</b> (vide séparateur ou avec contenu)
    /// → injection <c>marker + " \r"</c> qui crée un ¶ neuf au marker tout en
    /// préservant le ¶ existant en aval.</item>
    /// </list>
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
        /// InsertParagraphAfter) — injection directe sans <c>\r</c>.
        /// </param>
        /// <param name="existingParaContent">
        /// Contenu actuel du ¶ d'ancrage (typiquement <c>Paragraphs[1].Range.Text</c>,
        /// inclut le <c>\r</c> final du ¶). Si non null et qu'il vaut juste
        /// <c>marker + " "</c> (ou <c>marker</c> sans espace), on est dans
        /// le cas 2 (bug : le marker précédent est resté). Optionnel —
        /// callers historiques sans cette info gardent le comportement à 2 cas.
        /// </param>
        public static InjectionPlan Plan(string marker, bool hostParaIsOursAndEmpty,
            string existingParaContent = null)
        {
            string markerWithSpace = (marker ?? string.Empty) + " ";

            // Cas 1 : ¶ neuf vide créé par notre pipeline → on tape dedans.
            if (hostParaIsOursAndEmpty)
            {
                return new InjectionPlan
                {
                    TextToInsert = markerWithSpace,
                    CaretOffset = markerWithSpace.Length,
                    CreatesNewParagraph = false,
                };
            }

            // Cas 2 (bug user 2026-05-07) : ¶ pré-existant contient déjà juste
            // le marker (résidu d'une saisie précédente — l'user a edit la
            // ligne du dessus puis reconverti). Pas de double injection :
            // caret juste après le marker existant.
            if (existingParaContent != null)
            {
                string trimmed = existingParaContent.TrimEnd('\r', '', '\n');
                if (trimmed == markerWithSpace || trimmed == markerWithSpace.TrimEnd())
                {
                    return new InjectionPlan
                    {
                        TextToInsert = string.Empty,
                        CaretOffset = markerWithSpace.Length,
                        CreatesNewParagraph = false,
                    };
                }
            }

            // Cas 3 : ¶ pré-existant ≠ marker → \r pour créer un ¶ neuf au
            // marker, préserve le contenu user en aval.
            return new InjectionPlan
            {
                TextToInsert = markerWithSpace + "\r",
                CaretOffset = markerWithSpace.Length,
                CreatesNewParagraph = true,
            };
        }
    }
}
