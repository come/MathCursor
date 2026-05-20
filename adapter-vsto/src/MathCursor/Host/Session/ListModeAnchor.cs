namespace MathCursor.Host.Session
{
    /// <summary>
    /// État de l'ancre list-mode (multi-ligne visible, ADR
    /// 2026-05-05-Feat-multiline-list-mode-visible et
    /// 2026-05-05-Feat-cases-multiline-phase2). Quand un cross-merge a
    /// produit un bloc align*/cases, on mémorise le marker dominant et
    /// la position de l'ancre pour pré-injecter le marker à la frappe
    /// de la ligne suivante.
    /// Remplace <c>_lastListModeMarker</c> et <c>_listModeAnchorPara</c>
    /// dans <c>SuggestionService</c>.
    /// </summary>
    internal sealed class ListModeAnchor
    {
        /// <summary>Marker dominant (<c>=</c>, <c>&lt;=&gt;</c>, <c>=&gt;</c>,
        /// <c>&lt;=</c>, ou <c>{</c> pour cases).</summary>
        public string Marker { get; }

        /// <summary>Position absolue dans le doc du paragraphe d'ancre
        /// (= début du bloc align*/cases). Sert à invalider l'ancre dès
        /// que le caret quitte ce paragraphe.</summary>
        public int AnchorParagraphStart { get; }

        public ListModeAnchor(string marker, int anchorParagraphStart)
        {
            Marker = marker ?? string.Empty;
            AnchorParagraphStart = anchorParagraphStart;
        }
    }
}
