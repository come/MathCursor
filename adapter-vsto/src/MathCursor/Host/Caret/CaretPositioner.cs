using System;
using System.Reflection;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.Caret
{
    /// <summary>
    /// Positionnement du caret Word autour d'une OMath fraîchement insérée.
    /// Bounded context "caret post-commit" (P2.15 du refactor archi).
    ///
    /// <para>La logique pure (clamp position au paragraphe) reste dans
    /// <see cref="CaretPositionCalculator"/>. Cette classe ajoute la couche
    /// Word interop : trouver le ¶ contenant la position, exécuter
    /// <c>Selection.SetRange</c>, retomber sur EndKey si Word garde le
    /// caret dedans l'OMath malgré tout.</para>
    /// </summary>
    internal sealed class CaretPositioner
    {
        private readonly Word.Application _app;
        private readonly Action<string> _log;

        public CaretPositioner(Word.Application app, Action<string> log)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _log = log ?? (s => { });
        }

        /// <summary>
        /// Calcule la position du caret juste APRÈS un OMath, sans déborder
        /// dans le ¶ suivant. Quand l'OMath est en fin de ¶, <c>omEnd + 1</c>
        /// tomberait sur le ¶ mark (= start du ¶ suivant) — on clamp au
        /// content-end du ¶ courant.
        /// </summary>
        public int ComputeAfterOMath(Word.Document doc, int omEnd)
        {
            int paraContentEnd;
            try
            {
                var paraRange = doc.Range(omEnd, omEnd).Paragraphs[1].Range;
                paraContentEnd = Math.Max(paraRange.Start, paraRange.End - 1);
            }
            catch (Exception ex)
            {
                _log("compute_after_omath_para_error: " + ex.Message);
                paraContentEnd = omEnd;
            }
            return CaretPositionCalculator.ClampAfterOMathToParagraph(omEnd, paraContentEnd, doc.Content.End);
        }

        /// <summary>
        /// Force la sortie de l'éditeur OMath. Word a tendance à garder le
        /// caret en mode math quand il est pile à la fin d'une équation.
        /// 3 niveaux d'escalade :
        /// (1) SetRange(omEnd+1) clampé, (2) EndKey(wdLine) si toujours
        /// dans un OMath, (3) répétition jusqu'à <paramref name="maxAttempts"/>.
        /// </summary>
        public void NudgeOutOfMath(Word.Document doc, int maxAttempts)
        {
            for (int i = 0; i < maxAttempts; i++)
            {
                try
                {
                    var sel = _app.Selection;
                    if (sel.OMaths == null || sel.OMaths.Count == 0) return;

                    int omEnd = sel.OMaths[1].Range.End;
                    int target = ComputeAfterOMath(doc, omEnd);
                    if (target > sel.Start) _app.Selection.SetRange(target, target);

                    // wdLine = 5, wdMove = 0 — late-bind pour compat versions Word.
                    if (_app.Selection.OMaths != null && _app.Selection.OMaths.Count > 0)
                    {
                        try
                        {
                            _app.Selection.GetType().InvokeMember(
                                "EndKey", BindingFlags.InvokeMethod,
                                null, _app.Selection,
                                new object[] { 5, 0 });
                        }
                        catch { }
                    }
                }
                catch { return; }
            }
        }
    }
}
