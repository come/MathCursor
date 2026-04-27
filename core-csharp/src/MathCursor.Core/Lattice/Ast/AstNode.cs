namespace MathCursor.Core.Lattice.Ast
{
    /// <summary>
    /// Racine de la hiérarchie AST. Tous les nœuds (atomes, binops, scopes,
    /// holes…) en héritent. Voir <see cref="AstNodes"/> pour la liste complète.
    ///
    /// Les nœuds sont volontairement immuables : un AST se construit une fois,
    /// puis se lit. Les passes ultérieures (renderer, re-ranker) construisent
    /// de nouveaux nœuds plutôt que de muter ceux existants.
    /// </summary>
    public abstract class AstNode { }
}
