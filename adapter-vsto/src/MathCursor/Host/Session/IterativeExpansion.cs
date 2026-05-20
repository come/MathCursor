namespace MathCursor.Host.Session
{
    /// <summary>
    /// État de l'extension itérative de la zone via Ctrl+Espace répété
    /// (cf. ADR 2026-04-29-Feat-iterative-zone-expansion-ctrl-space).
    /// Remplace les champs <c>_iterativeSpanStart</c> /
    /// <c>_iterativeSpanEnd</c> / <c>_iterativeStops</c> dispersés dans
    /// <c>SuggestionService</c>.
    /// </summary>
    internal sealed class IterativeExpansion
    {
        public int SpanStart { get; }
        public int SpanEnd { get; }
        /// <summary>Indice du « stop » courant dans la séquence d'expansion
        /// (0 = zone NER initiale, 1+ = expansions Ctrl+Espace).</summary>
        public int StopIndex { get; }

        public IterativeExpansion(int spanStart, int spanEnd, int stopIndex)
        {
            SpanStart = spanStart;
            SpanEnd = spanEnd;
            StopIndex = stopIndex;
        }

        /// <summary>État vide : aucune zone détectée, pas d'expansion en cours.</summary>
        public static IterativeExpansion None => new IterativeExpansion(-1, -1, 0);

        public bool IsActive => SpanStart >= 0 && SpanEnd > SpanStart;
    }
}
