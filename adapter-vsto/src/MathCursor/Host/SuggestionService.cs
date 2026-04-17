using System;
using MathCursor.Core.Symbols;
using MathCursor.UI;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host
{
    /// <summary>
    /// Écoute les mouvements du curseur, lit le contexte (~50 chars avant le
    /// caret), interroge SymbolMatcher, et affiche/cache une popup WPF près
    /// du caret avec les candidats. Tab valide via le hook clavier (déjà branché),
    /// Up/Down naviguent, Esc cache.
    /// </summary>
    public sealed class SuggestionService : IDisposable
    {
        private const int ContextChars = 50;

        private readonly Word.Application _app;
        private SuggestionPopupWindow _popup;
        private bool _installed;

        public SuggestionService(Word.Application app)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
        }

        public void Install()
        {
            if (_installed) return;
            _app.WindowSelectionChange += OnSelectionChange;
            _installed = true;
        }

        public void Dispose()
        {
            try { if (_installed) _app.WindowSelectionChange -= OnSelectionChange; } catch { }
            try { _popup?.Close(); } catch { }
            _popup = null;
            _installed = false;
        }

        public bool IsPopupVisible => _popup != null && _popup.IsVisible;
        public int SelectedIndex => _popup?.SelectedIndex ?? 0;

        public void MoveSelection(int delta) => _popup?.MoveSelection(delta);

        public void HidePopup() => _popup?.HidePopup();

        private void OnSelectionChange(Word.Selection sel)
        {
            try
            {
                var ctx = ReadContext();
                var match = SymbolMatcher.FindSymbol(ctx);
                if (match != null && match.Choices.Count > 0)
                {
                    ShowPopup(match.Choices);
                }
                else
                {
                    HidePopup();
                }
            }
            catch
            {
                // Ne jamais propager depuis un event handler Word
            }
        }

        private string ReadContext()
        {
            var sel = _app.Selection;
            int caretPos = sel.Start;
            var doc = _app.ActiveDocument;
            int start = Math.Max(doc.Content.Start, caretPos - ContextChars);
            if (start >= caretPos) return "";
            return doc.Range(start, caretPos).Text ?? "";
        }

        private void ShowPopup(System.Collections.Generic.IReadOnlyList<SymbolChoice> choices)
        {
            if (_popup == null) _popup = new SuggestionPopupWindow();
            var pos = GetCaretScreenPosition();
            _popup.ShowSuggestions(choices, pos.x, pos.y);
        }

        private (double x, double y) GetCaretScreenPosition()
        {
            try
            {
                var sel = _app.Selection;
                var win = _app.ActiveWindow;
                // Position du caret en points (1/72") relativement à la page
                double hPos = (double)sel.get_Information(Word.WdInformation.wdHorizontalPositionRelativeToPage);
                double vPos = (double)sel.get_Information(Word.WdInformation.wdVerticalPositionRelativeToPage);
                double x = win.PointsToScreenPixelsX(hPos);
                double y = win.PointsToScreenPixelsY(vPos);
                return (x, y + 22); // 22px sous la ligne du caret
            }
            catch
            {
                return (200, 200);
            }
        }
    }
}
