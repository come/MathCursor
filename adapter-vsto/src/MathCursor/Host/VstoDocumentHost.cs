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
            // ContentControlOnEnter : déclenché quand le curseur entre dans un CC
            try { _app.ContentControlOnEnter += OnContentControlEnter; } catch { /* build Word ancien */ }
            try { _app.ContentControlOnExit += OnContentControlExit; } catch { /* idem */ }
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

            // Le zone.StartOffset est relatif au ContextText lu, pas au document.
            // On re-détermine la plage à remplacer : les derniers N caractères avant le curseur,
            // où N = longueur du texte source.
            int caretPos = sel.Start;
            int zoneStart = Math.Max(doc.Content.Start, caretPos - zone.Text.Length);
            var target = doc.Range(zoneStart, caretPos);

            // Remplacement atomique via InsertXML (1 seul undo step)
            if (!string.IsNullOrEmpty(equation.Omml))
            {
                target.InsertXML(equation.Omml);
            }
            else
            {
                target.Text = equation.UnicodeFallback ?? equation.Source;
            }

            // Identifier l'équation insérée via un ContentControl (optionnel pour édition future)
            var handleId = Guid.NewGuid().ToString("N");
            // TODO phase C2 : wrapper l'équation dans un CC avec Tag = $"MathCursor:{handleId}"

            // Curseur après la zone insérée
            _app.Selection.Collapse(Word.WdCollapseDirection.wdCollapseEnd);

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

        private void OnContentControlEnter(Word.ContentControl cc)
        {
            if (cc.Tag?.StartsWith("MathCursor:") == true)
            {
                _equationEntered?.Invoke(new EquationHandle(cc.Tag.Substring("MathCursor:".Length)));
            }
        }

        private void OnContentControlExit(Word.ContentControl cc, ref bool Cancel)
        {
            if (cc.Tag?.StartsWith("MathCursor:") == true)
            {
                _equationExited?.Invoke(new EquationHandle(cc.Tag.Substring("MathCursor:".Length)));
            }
        }
    }
}
