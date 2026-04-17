using System;
using System.Threading.Tasks;
using MathCursor.HostContract;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host
{
    /// <summary>
    /// Implémentation VSTO de IDocumentHost. Accès direct mémoire Word, events
    /// natifs fiables. Pas de polling.
    ///
    /// Phase C1 : OnConversionRequested seul est utilisé activement (par le hook
    /// Tab + bouton ribbon). Les autres events (CaretMoved, EquationEntered/Exited)
    /// sont stub pour préparer la phase C2 (mode édition d'OMath via Content Control).
    /// </summary>
    public sealed class VstoDocumentHost : IDocumentHost
    {
        private readonly Word.Application _app;
        private event ConversionRequestedListener _conversionRequested;

        public VstoDocumentHost(Word.Application app)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
        }

        public Task<ContextText> ReadContextAroundCaretAsync(int charsBefore, int charsAfter)
        {
            var sel = _app.Selection;
            int caretPos = sel.Start;
            var doc = _app.ActiveDocument;
            int docStart = doc.Content.Start;
            int docEnd = doc.Content.End;

            int startOffset = Math.Max(docStart, caretPos - charsBefore);
            int endOffset = Math.Min(docEnd, caretPos + charsAfter);

            string textBefore = "";
            if (caretPos > startOffset)
            {
                textBefore = doc.Range(startOffset, caretPos).Text ?? "";
            }

            string textAfter = "";
            if (endOffset > caretPos)
            {
                textAfter = doc.Range(caretPos, endOffset).Text ?? "";
            }

            return Task.FromResult(new ContextText
            {
                TextBefore = textBefore,
                TextAfter = textAfter,
                CaretOffset = textBefore.Length,
                LanguageHint = null,
            });
        }

        public Task<EquationHandle> InsertEquationAsync(TextZone zone, EquationOutput equation)
        {
            var doc = _app.ActiveDocument;
            var sel = _app.Selection;

            int caretPos = sel.Start;
            int zoneStart = Math.Max(doc.Content.Start, caretPos - zone.Text.Length);

            var linearText = !string.IsNullOrEmpty(equation.UnicodeFallback)
                ? equation.UnicodeFallback
                : equation.Source;

            // Refus si la zone à remplacer contient déjà une OMath : on ne peut
            // pas la "deleter" via .Text=, et fusionner deux maths est délicat.
            var replaceRange = doc.Range(zoneStart, caretPos);
            if (replaceRange.OMaths.Count > 0)
            {
                throw new InvalidOperationException(
                    "Zone à convertir chevauche une OMath existante.");
            }

            var handleId = Guid.NewGuid().ToString("N");

            // Cas 1 : remplacement texte simple (symbol pattern alpha→α, >=→≥...).
            //         Pas de wrap OMath, pas d'espace trailing → curseur juste après.
            if (string.IsNullOrEmpty(equation.Omml))
            {
                replaceRange.Text = linearText;
                int cursor = zoneStart + linearText.Length;
                if (cursor > doc.Content.End) cursor = doc.Content.End;
                _app.Selection.SetRange(cursor, cursor);
                return Task.FromResult(new EquationHandle(handleId));
            }

            // Cas 2 : expression math complète. Texte_linéaire + ESPACE pour rester
            //         inline (sinon Word auto-convertit en display mode), puis
            //         OMaths.Add + BuildUp natif Word.
            replaceRange.Text = linearText + " ";

            var mathRange = doc.Range(zoneStart, zoneStart + linearText.Length);

            try
            {
                mathRange.OMaths.Add(mathRange);
                mathRange.OMaths.BuildUp();

                int afterPos;
                if (doc.OMaths.Count > 0)
                {
                    var lastMath = doc.OMaths[doc.OMaths.Count];
                    afterPos = lastMath.Range.End + 1; // +1 = espace trailing
                }
                else
                {
                    afterPos = zoneStart + linearText.Length + 1;
                }
                if (afterPos > doc.Content.End) afterPos = doc.Content.End;
                _app.Selection.SetRange(afterPos, afterPos);
            }
            catch
            {
                // BuildUp a échoué (format non reconnu) → garder le texte linéaire
                var fallbackPos = zoneStart + linearText.Length + 1;
                if (fallbackPos > doc.Content.End) fallbackPos = doc.Content.End;
                _app.Selection.SetRange(fallbackPos, fallbackPos);
            }

            return Task.FromResult(new EquationHandle(handleId));
        }

        // --- Stubs phase C2 (édition d'équations via Content Controls) ---

        public Task UpdateEquationAsync(EquationHandle handle, EquationOutput equation)
            => Task.CompletedTask;

        public Task RevertEquationAsync(EquationHandle handle)
            => Task.CompletedTask;

        public Task<EquationHandle> GetCaretEquationAsync()
            => Task.FromResult<EquationHandle>(null);

        public Unsubscribe OnCaretMoved(CaretMovedListener listener)
            => () => { }; // pas de polling cursor en VSTO (relicat Office.js)

        public Unsubscribe OnEquationEntered(EquationEnteredListener listener)
            => () => { }; // phase C2 via ContentControlOnEnter

        public Unsubscribe OnEquationExited(EquationExitedListener listener)
            => () => { }; // phase C2 via ContentControlOnExit

        public Unsubscribe OnConversionRequested(ConversionRequestedListener listener)
        {
            _conversionRequested += listener;
            return () => _conversionRequested -= listener;
        }

        /// <summary>Appelé par le bouton ribbon ou le hook Tab : déclenche conversion.</summary>
        public void TriggerConversion() => _conversionRequested?.Invoke();
    }
}
