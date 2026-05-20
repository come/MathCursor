using MathCursor.Core.Lattice.Ast;

namespace MathCursor.Core.Lattice.Ambiguity
{
    /// <summary>
    /// Contexte passé à chaque <see cref="IAmbiguityScanner.Scan"/>.
    /// Bag immuable contenant les 3 vues sur la zone en cours de résolution :
    /// l'AST top-1, le LaTeX rendu, la source brute.
    ///
    /// <para>Un scanner peut consommer une, deux ou les trois vues selon son
    /// heuristique :</para>
    /// <list type="bullet">
    /// <item>AST-based : <see cref="TopAst"/> uniquement (ex.
    ///   <c>AstBasedScanner</c>, <c>DecoratedTwoThreeUpperScanner</c>).</item>
    /// <item>String-based topLatex : <see cref="TopLatex"/> (ex.
    ///   <c>UppercaseSequencesScanner</c>).</item>
    /// <item>Source-mutation : <see cref="Source"/> + <see cref="TopLatex"/>
    ///   pour mapper la position source vers topLatex (ex.
    ///   <c>VAsForallEAsExistsScanner</c>, <c>CanonicalSetLettersScanner</c>).</item>
    /// </list>
    /// </summary>
    public sealed class ScanContext
    {
        /// <summary>AST top-1 produit par le parser sur la source mutée.</summary>
        public AstNode TopAst { get; }

        /// <summary>LaTeX rendu par <c>LatexRenderer</c> à partir de <see cref="TopAst"/>.</summary>
        public string TopLatex { get; }

        /// <summary>Source brute passée au pipeline (post préprocesseurs +
        /// <c>ApplyPreferences</c>). Utilisée par les scanners source-based
        /// pour calculer des <c>SourceMutation</c> précises.</summary>
        public string Source { get; }

        public ScanContext(AstNode topAst, string topLatex, string source)
        {
            TopAst = topAst;
            TopLatex = topLatex ?? string.Empty;
            Source = source ?? string.Empty;
        }
    }
}
