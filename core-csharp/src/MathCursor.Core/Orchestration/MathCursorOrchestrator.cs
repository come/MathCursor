using System;
using System.Threading.Tasks;
using MathCursor.Core.Pipeline;
using MathCursor.HostContract;

namespace MathCursor.Core.Orchestration;

/// <summary>
/// Chef d'orchestre : coordonne le pipeline de conversion avec les interfaces
/// host-contract. Aucune référence directe à Word / VSTO / Office.js.
///
/// En VSTO, le déclenchement est explicite (Tab hook ou bouton ribbon) → pas de
/// polling, pas de loop possible, donc pas d'anti-boucle nécessaire (relicat
/// retiré du prototype Office.js).
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

        _host.OnConversionRequested(() => _ = TryConvertAtCaretAsync());
        _host.OnEquationEntered(handle => _ = OnEquationEnteredAsync(handle));
        _host.OnEquationExited(_ => _surface.ExitEditMode());
        _surface.OnEditCommitted(newSource => _ = OnEditCommittedAsync(newSource));
    }

    /// <summary>
    /// Tente la conversion de l'expression sous le curseur. Retourne true si
    /// effectuée, false sinon. Utilisé par le hook clavier pour décider si on
    /// consomme la touche Tab.
    /// </summary>
    public bool TryConvertAtCaret()
    {
        return TryConvertAtCaretAsync().GetAwaiter().GetResult();
    }

    private async Task<bool> TryConvertAtCaretAsync()
    {
        var ctx = await _host.ReadContextAroundCaretAsync(charsBefore: 200, charsAfter: 0);
        var textBefore = ctx.TextBefore;

        var result = ConversionPipeline.Convert(textBefore, ctx.LanguageHint);
        if (!result.Success || result.Zone == null || result.Equation == null)
        {
            _feedback.LogParsingError(textBefore, result.Reason ?? "unknown");
            return false;
        }

        var zone = new TextZone
        {
            StartOffset = ctx.CaretOffset - result.Equation.Source.Length,
            EndOffset = ctx.CaretOffset,
            Text = result.Equation.Source,
        };

        // Insertion : si l'host refuse (zone chevauche un OMath, etc.), on
        // log et on rend la main au caller (Tab → propagation normale).
        EquationHandle handle;
        try
        {
            handle = await _host.InsertEquationAsync(zone, result.Equation);
        }
        catch (Exception insertEx)
        {
            _feedback.LogParsingError("(insert)", insertEx.Message);
            return false;
        }

        // Storage best-effort : un échec ne doit pas casser l'UX de conversion.
        try
        {
            await _store.StoreAsync(handle, result.Equation.Source, result.Equation.Metadata);
        }
        catch (Exception storeEx)
        {
            _feedback.LogParsingError("(store)", storeEx.Message);
        }

        _feedback.LogSuggestionSelected(0);
        return true;
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
