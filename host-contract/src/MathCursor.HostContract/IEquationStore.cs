using System.Collections.Generic;
using System.Threading.Tasks;

namespace MathCursor.HostContract;

/// <summary>
/// Stockage des sources d'équations (persistance hors flux de texte).
/// VSTO → CustomXMLParts, Office.js → customXmlParts, Web → localStorage.
/// </summary>
public interface IEquationStore
{
    Task StoreAsync(EquationHandle handle, string source, EquationMetadata metadata);

    Task<StoredEquation?> RetrieveAsync(EquationHandle handle);

    Task UpdateAsync(EquationHandle handle, string newSource);

    Task RemoveAsync(EquationHandle handle);

    Task<IReadOnlyList<EquationHandle>> ListAllAsync();
}
