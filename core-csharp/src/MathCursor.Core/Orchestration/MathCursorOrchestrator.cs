using System.Threading.Tasks;
using MathCursor.HostContract;

namespace MathCursor.Core.Orchestration;

/// <summary>
/// Chef d'orchestre qui coordonne le pipeline en utilisant uniquement les
/// interfaces host-contract. Aucune référence à Word / VSTO / Office.js.
/// </summary>
public sealed class MathCursorOrchestrator
{
    private readonly IDocumentHost _host;
    private readonly IEquationStore _store;
    private readonly IEditorSurface _surface;
    private readonly IUserFeedback _feedback;

    public MathCursorOrchestrator(
        IDocumentHost host,
        IEquationStore store,
        IEditorSurface surface,
        IUserFeedback feedback)
    {
        _host = host;
        _store = store;
        _surface = surface;
        _feedback = feedback;

        // Câblage des events via les interfaces abstraites
        _host.OnConversionRequested(OnConversionRequested);
        _host.OnEquationEntered(OnEquationEntered);
        _host.OnEquationExited(OnEquationExited);
        _surface.OnSuggestionSelected(OnSuggestionSelected);
        _surface.OnEditCommitted(OnEditCommitted);
    }

    private void OnConversionRequested()
    {
        // TODO phase B : lire contexte, détecter zone, générer candidats,
        // afficher via IEditorSurface
        _ = HandleConversionAsync();
    }

    private async Task HandleConversionAsync()
    {
        var ctx = await _host.ReadContextAroundCaretAsync(charsBefore: 100, charsAfter: 20);
        // Pipeline à implémenter : tokenize → score → detect zone → parse → render
        _feedback.LogPlatformCapability("orchestrator.conversion.wiring", true);
    }

    private void OnEquationEntered(EquationHandle handle)
    {
        _ = ShowEditModeAsync(handle);
    }

    private async Task ShowEditModeAsync(EquationHandle handle)
    {
        var stored = await _store.RetrieveAsync(handle);
        if (stored != null)
        {
            _surface.ShowEditMode(stored.Source, handle);
        }
    }

    private void OnEquationExited(EquationHandle handle)
    {
        _surface.ExitEditMode();
    }

    private void OnSuggestionSelected(int index)
    {
        // TODO phase B : insérer l'équation sélectionnée via _host.InsertEquationAsync
    }

    private void OnEditCommitted(string newSource)
    {
        // TODO phase B : reconvertir depuis newSource et appeler _host.UpdateEquationAsync
    }
}
