using System.Threading.Tasks;

namespace MathCursor.HostContract;

/// <summary>
/// Interaction avec le document texte. Chaque adapter plateforme
/// (VSTO, Office.js, Web) fournit sa propre implémentation.
/// </summary>
public interface IDocumentHost
{
    Task<ContextText> ReadContextAroundCaretAsync(int charsBefore, int charsAfter);

    Task<EquationHandle> InsertEquationAsync(TextZone zone, EquationOutput equation);

    Task UpdateEquationAsync(EquationHandle handle, EquationOutput equation);

    Task RevertEquationAsync(EquationHandle handle);

    Task<EquationHandle?> GetCaretEquationAsync();

    Unsubscribe OnCaretMoved(CaretMovedListener listener);

    Unsubscribe OnEquationEntered(EquationEnteredListener listener);

    Unsubscribe OnEquationExited(EquationExitedListener listener);

    Unsubscribe OnConversionRequested(ConversionRequestedListener listener);
}
