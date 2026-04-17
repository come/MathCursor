using System.Collections.Generic;
using MathCursor.HostContract;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host
{
    /// <summary>
    /// Implémentation VSTO de IEditorSurface. Pour le MVP phase C1 :
    /// - Notify() écrit dans la StatusBar Word
    /// - ShowSuggestions / ShowEditMode sont des no-ops (pas de popup pour l'instant)
    /// Une popup WPF TopMost au caret sera ajoutée en phase C2.
    /// </summary>
    public sealed class VstoEditorSurface : IEditorSurface
    {
        private readonly Word.Application _app;
        private event SuggestionSelectedListener _suggestionSelected;
        private event EditCommittedListener _editCommitted;

        public VstoEditorSurface(Word.Application app)
        {
            _app = app;
        }

        public void ShowSuggestions(IReadOnlyList<RankedCandidate> candidates)
        {
            // Phase C1 : pas de UI de suggestions. Le pipeline n'en produit qu'une de toute façon.
            // Phase C2 : popup WPF ancrée au caret.
        }

        public void HideSuggestions() { }

        public void ShowEditMode(string source, EquationHandle handle)
        {
            // Phase C2 : popup éditable
            _app.StatusBar = "Édition : " + source;
        }

        public void ExitEditMode()
        {
            _app.StatusBar = "";
        }

        public void Notify(string message, NotificationLevel level)
        {
            var prefix = level == NotificationLevel.Error ? "⚠ "
                       : level == NotificationLevel.Warning ? "! "
                       : "";
            _app.StatusBar = prefix + message;
        }

        public Unsubscribe OnSuggestionSelected(SuggestionSelectedListener listener)
        {
            _suggestionSelected += listener;
            return () => _suggestionSelected -= listener;
        }

        public Unsubscribe OnEditCommitted(EditCommittedListener listener)
        {
            _editCommitted += listener;
            return () => _editCommitted -= listener;
        }
    }
}
