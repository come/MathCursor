using System.Threading.Tasks;
using MathCursor.Core.Pipeline;
using MathCursor.HostContract;

namespace MathCursor.Core.Orchestration;

/// <summary>
/// Chef d'orchestre : coordonne le pipeline de conversion avec les interfaces
/// host-contract. Aucune référence directe à Word / VSTO / Office.js.
/// </summary>
public sealed class MathCursorOrchestrator
{
    private readonly IDocumentHost _host;
    private readonly IEquationStore _store;
    private readonly IEditorSurface _surface;
    private readonly IUserFeedback _feedback;

    // Anti-boucle Ctrl+Z : textes récemment convertis. Libéré quand l'utilisateur édite.
    private string? _lastConvertedSource;

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

        _host.OnConversionRequested(() => _ = OnConversionRequestedAsync());
        _host.OnEquationEntered(handle => _ = OnEquationEnteredAsync(handle));
        _host.OnEquationExited(_ => _surface.ExitEditMode());
        _surface.OnSuggestionSelected(_ => { /* phase C — 1 candidat unique pour l'instant */ });
        _surface.OnEditCommitted(newSource => _ = OnEditCommittedAsync(newSource));
    }

    private async Task OnConversionRequestedAsync()
    {
        var ctx = await _host.ReadContextAroundCaretAsync(charsBefore: 200, charsAfter: 20);
        var textBefore = ctx.TextBefore;

        // Pipeline pur
        var result = ConversionPipeline.Convert(textBefore, ctx.LanguageHint);
        if (!result.Success || result.Zone == null || result.Equation == null)
        {
            _feedback.LogParsingError(textBefore, result.Reason ?? "unknown");
            _surface.Notify("Aucune expression math détectée près du curseur.", NotificationLevel.Info);
            return;
        }

        // Anti-boucle : si c'est le même texte qu'à la dernière conversion, ne rien faire
        if (_lastConvertedSource == result.Equation.Source) return;

        // La zone math est relative au début du contexte lu ; on la convertit en TextZone absolu
        var zone = new TextZone
        {
            StartOffset = ctx.CaretOffset - result.Equation.Source.Length,
            EndOffset = ctx.CaretOffset,
            Text = result.Equation.Source,
        };

        var handle = await _host.InsertEquationAsync(zone, result.Equation);
        await _store.StoreAsync(handle, result.Equation.Source, result.Equation.Metadata);
        _lastConvertedSource = result.Equation.Source;
    }

    private async Task OnEquationEnteredAsync(EquationHandle handle)
    {
        var stored = await _store.RetrieveAsync(handle);
        if (stored != null)
        {
            _surface.ShowEditMode(stored.Source, handle);
        }
    }

    private async Task OnEditCommittedAsync(string newSource)
    {
        // Re-conversion depuis le texte édité et update de l'équation
        var result = ConversionPipeline.Convert(newSource);
        if (!result.Success || result.Equation == null)
        {
            _surface.Notify("Impossible de re-convertir : expression invalide.", NotificationLevel.Warning);
            return;
        }
        var handle = await _host.GetCaretEquationAsync();
        if (handle == null) return;
        await _host.UpdateEquationAsync(handle, result.Equation);
        await _store.UpdateAsync(handle, newSource);
        _surface.ExitEditMode();
    }
}
