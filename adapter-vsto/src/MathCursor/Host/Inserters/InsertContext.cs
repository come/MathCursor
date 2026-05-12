using System.Collections.Generic;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.Inserters
{
    /// <summary>
    /// Données d'entrée d'une stratégie d'insertion OMath. Construit
    /// une seule fois par <see cref="SuggestionService.InsertOMathAt"/>
    /// puis passé en lecture aux stratégies enchaînées. Cf. P2.7 du
    /// refactor archi (ADR <c>2026-05-12-Refactor-pure-merger-atomic-insert</c>).
    /// </summary>
    internal sealed class InsertContext
    {
        public Word.Document Doc { get; }
        public int AbsStart { get; }
        public int AbsEnd { get; }
        public string Latex { get; }
        public bool IsDisplayMath { get; }
        public int TargetCount { get; }
        public Word.Paragraph FirstPara { get; }
        public IReadOnlyList<string> AbsorbedHandles { get; }

        public InsertContext(
            Word.Document doc,
            int absStart, int absEnd,
            string latex,
            bool isDisplayMath,
            int targetCount,
            Word.Paragraph firstPara,
            IReadOnlyList<string> absorbedHandles)
        {
            Doc = doc;
            AbsStart = absStart;
            AbsEnd = absEnd;
            Latex = latex;
            IsDisplayMath = isDisplayMath;
            TargetCount = targetCount;
            FirstPara = firstPara;
            AbsorbedHandles = absorbedHandles;
        }
    }

    /// <summary>
    /// Résultat d'une tentative d'insertion. <see cref="Success"/> = false
    /// → la stratégie a été passée (skip ou échec) ; le caller essaie la
    /// suivante. Sur succès, <see cref="NewStart"/>/<see cref="NewEnd"/>
    /// donnent les bornes de l'OMath insérée.
    /// </summary>
    internal readonly struct InsertResult
    {
        public bool Success { get; }
        public int NewStart { get; }
        public int NewEnd { get; }

        private InsertResult(bool s, int ns, int ne) { Success = s; NewStart = ns; NewEnd = ne; }

        public static InsertResult Ok(int newStart, int newEnd)
            => new InsertResult(true, newStart, newEnd);

        public static InsertResult Skipped => default;
    }
}
