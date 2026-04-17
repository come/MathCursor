using System;
using System.Threading.Tasks;
using MathCursor.HostContract;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host
{
    /// <summary>
    /// Implémentation VSTO de IDocumentHost. Accès direct mémoire Word via
    /// Microsoft.Office.Interop.Word — pas de latence réseau, events natifs fiables.
    /// </summary>
    public sealed class VstoDocumentHost : IDocumentHost
    {
        private readonly Word.Application _app;

        // Events abonnés par l'orchestrateur
        private event CaretMovedListener _caretMoved;
        private event EquationEnteredListener _equationEntered;
        private event EquationExitedListener _equationExited;
        private event ConversionRequestedListener _conversionRequested;

        public VstoDocumentHost(Word.Application app)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _app.WindowSelectionChange += OnSelectionChange;
            // NOTE phase C2 : ContentControlOnEnter / ContentControlOnExit existent
            // sur Application mais ne sont pas exposés directement via l'interop embarqué.
            // Il faut passer par ((Word.ApplicationEvents4_Event)_app).ContentControlOnEnter
            // ou gérer via un wrapper. Reporté tant qu'on n'utilise pas les CC.
        }

        public Task<ContextText> ReadContextAroundCaretAsync(int charsBefore, int charsAfter)
        {
            // Accès synchrone direct : pas besoin d'async réel.
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
                var rBefore = doc.Range(startOffset, caretPos);
                textBefore = rBefore.Text ?? "";
            }

            string textAfter = "";
            if (endOffset > caretPos)
            {
                var rAfter = doc.Range(caretPos, endOffset);
                textAfter = rAfter.Text ?? "";
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

            // La zone à remplacer : les N derniers caractères avant le curseur,
            // où N = longueur du texte source.
            int caretPos = sel.Start;
            int zoneStart = Math.Max(doc.Content.Start, caretPos - zone.Text.Length);

            // 1. Remplacer le texte tapé par sa version linéaire propre
            //    (UnicodeFallback = normalisation ASCII, ou Source brut en fallback).
            var linearText = !string.IsNullOrEmpty(equation.UnicodeFallback)
                ? equation.UnicodeFallback
                : equation.Source;

            var replaceRange = doc.Range(zoneStart, caretPos);
            replaceRange.Text = linearText;

            // 2. Re-cibler la plage sur le nouveau texte, wrapper dans OMath,
            //    puis BuildUp : Word parse le format linéaire et convertit en
            //    équation formatée (fractions, exposants, √, etc.). C'est la
            //    méthode native VSTO, plus robuste que l'insertion OOXML.
            var mathRange = doc.Range(zoneStart, zoneStart + linearText.Length);
            var handleId = Guid.NewGuid().ToString("N");

            try
            {
                var oMath = mathRange.OMaths.Add(mathRange);
                oMath.BuildUp();

                // Curseur juste après l'équation
                var endPos = oMath.Range.End;
                _app.Selection.SetRange(endPos, endPos);
            }
            catch
            {
                // BuildUp a échoué (format non reconnu par Word) → on garde
                // le texte linéaire tel quel. Cursor après.
                _app.Selection.SetRange(zoneStart + linearText.Length, zoneStart + linearText.Length);
            }

            return Task.FromResult(new EquationHandle(handleId));
        }

        public Task UpdateEquationAsync(EquationHandle handle, EquationOutput equation)
        {
            // TODO phase C2 : retrouver le CC par Tag et remplacer son contenu
            return Task.CompletedTask;
        }

        public Task RevertEquationAsync(EquationHandle handle)
        {
            // TODO phase C2 : retrouver le CC par Tag et remplacer par le source texte
            return Task.CompletedTask;
        }

        public Task<EquationHandle> GetCaretEquationAsync()
        {
            // TODO phase C2 : inspecter selection.ParentContentControl
            return Task.FromResult<EquationHandle>(null);
        }

        public Unsubscribe OnCaretMoved(CaretMovedListener listener)
        {
            _caretMoved += listener;
            return () => _caretMoved -= listener;
        }

        public Unsubscribe OnEquationEntered(EquationEnteredListener listener)
        {
            _equationEntered += listener;
            return () => _equationEntered -= listener;
        }

        public Unsubscribe OnEquationExited(EquationExitedListener listener)
        {
            _equationExited += listener;
            return () => _equationExited -= listener;
        }

        public Unsubscribe OnConversionRequested(ConversionRequestedListener listener)
        {
            _conversionRequested += listener;
            return () => _conversionRequested -= listener;
        }

        /// <summary>Appelé par le bouton ribbon : trigger une conversion.</summary>
        public void TriggerConversion() => _conversionRequested?.Invoke();

        // --- Events Word → listeners abstraits ---

        private void OnSelectionChange(Word.Selection sel)
        {
            _caretMoved?.Invoke(new CaretPosition { Offset = sel.Start });
        }
    }
}
