using Xunit;

namespace MathCursor.Core.Tests.Lattice
{
    /// <summary>
    /// Collection xUnit qui sérialise les tests modifiant
    /// <c>LatexRenderer.GlobalOptions</c> (état global statique). Sans
    /// sérialisation, deux tests parallèles qui flippent le setting MultSymbol
    /// se polluent mutuellement (race condition entre constructor save et
    /// test body assertion).
    ///
    /// Toute classe de tests qui touche <c>LatexRenderer.GlobalOptions</c>
    /// doit être annotée <c>[Collection(GlobalOptionsTestCollection.Name)]</c>.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class GlobalOptionsTestCollection
    {
        public const string Name = "LatexRenderer.GlobalOptions";
    }
}
