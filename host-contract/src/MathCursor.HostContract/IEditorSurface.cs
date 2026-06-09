using System.Collections.Generic;

namespace MathCursor.HostContract;

/// <summary>
/// UI de présentation des suggestions et du mode édition. Chaque plateforme
/// choisit son format (popup WPF au caret, task pane, overlay HTML).
/// </summary>
public interface IEditorSurface
{
    void ShowSuggestions(IReadOnlyList<RankedCandidate> candidates);

    void HideSuggestions();

    void ShowEditMode(string source, EquationHandle handle);

    void ExitEditMode();

    void Notify(string message, NotificationLevel level);

    Unsubscribe OnSuggestionSelected(SuggestionSelectedListener listener);

    Unsubscribe OnEditCommitted(EditCommittedListener listener);
}
