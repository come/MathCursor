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
        private readonly WordContextReader _contextReader;
        private event ConversionRequestedListener _conversionRequested;

        public VstoDocumentHost(Word.Application app)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _contextReader = new WordContextReader(_app);
        }

        /// <summary>Lecture déléguée à WordContextReader (source unique).</summary>
        public Task<ContextText> ReadContextAroundCaretAsync(int charsBefore, int charsAfter)
        {
            return Task.FromResult(_contextReader.ReadAround(charsBefore, charsAfter));
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

            // La zone peut chevaucher un OMath existant — c'est le cas typique
            // après "alpha"+Tab puis "+beta"+Tab : on veut MISE À JOUR de l'OMath
            // existant pour qu'il devienne α+β. Word's Range.Text = "..." remplace
            // tout y compris les OMaths.
            var replaceRange = doc.Range(zoneStart, caretPos);

            var handleId = Guid.NewGuid().ToString("N");

            // Toute conversion finit en OMath (cohérence visuelle : math en
            // Cambria Math italic, pas de mélange texte plat / équation).
            // Texte_linéaire + ESPACE pour rester inline (sinon display mode).
            replaceRange.Text = linearText + " ";

            var mathRange = doc.Range(zoneStart, zoneStart + linearText.Length);

            try
            {
                mathRange.OMaths.Add(mathRange);
                mathRange.OMaths.BuildUp();

                // Trouver L'OMath qu'on vient d'ajouter — celui qui contient
                // zoneStart. doc.OMaths[Count] donnerait le DERNIER OMath du
                // document (en bas de page), pas le nôtre. Scan explicite.
                int afterPos = zoneStart + linearText.Length + 1; // estimation par défaut
                foreach (Word.OMath om in doc.OMaths)
                {
                    var rng = om.Range;
                    if (rng.Start <= zoneStart && rng.End > zoneStart)
                    {
                        // +1 pour passer l'espace trailing inséré juste après
                        afterPos = rng.End + 1;
                        break;
                    }
                }
                if (afterPos > doc.Content.End) afterPos = doc.Content.End;
                _app.Selection.SetRange(afterPos, afterPos);

                // Workaround Word : positionner le curseur juste après un OMath
                // peut le laisser INSIDE l'éditeur math (notamment dans le cas de
                // mise à jour d'un OMath existant). On vérifie via wdInMathBuilder
                // et on avance d'un caractère tant qu'on est encore dans le math.
                NudgeCursorOutOfMath(doc);
            }
            catch
            {
                // BuildUp a échoué (format non reconnu) → garder le texte linéaire
                var fallbackPos = zoneStart + linearText.Length + 1;
                if (fallbackPos > doc.Content.End) fallbackPos = doc.Content.End;
                _app.Selection.SetRange(fallbackPos, fallbackPos);
                NudgeCursorOutOfMath(doc);
            }

            return Task.FromResult(new EquationHandle(handleId));
        }

        /// <summary>
        /// Si Word a placé le curseur à l'intérieur d'un OMath malgré notre
        /// SetRange (Word interprète parfois "juste après" comme "dedans"),
        /// on saute directement à la fin de cet OMath + 1 (un seul saut, pas
        /// d'itération à travers l'expression).
        /// </summary>
        private void NudgeCursorOutOfMath(Word.Document doc)
        {
            try
            {
                var sel = _app.Selection;
                if (sel.OMaths.Count == 0) return;
                int omEnd = sel.OMaths[1].Range.End;
                int target = omEnd + 1;
                if (target > doc.Content.End) target = doc.Content.End;
                _app.Selection.SetRange(target, target);
            }
            catch
            {
                // Ne jamais propager : positionnement curseur best-effort
            }
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
