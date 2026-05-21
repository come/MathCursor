using MathCursor.Core.Lattice.Ast;

namespace MathCursor.Core.Patterns
{
    /// <summary>
    /// Contexte passé à chaque <see cref="IPatternTemplate.TryMatchHead"/>
    /// et <see cref="IPatternTemplate.Expand"/>. Bag immuable contenant les
    /// 3 vues sur la zone + la position caret.
    ///
    /// <para>Distinct de <c>MathCursor.Core.Lattice.Ambiguity.ScanContext</c>
    /// (qui sert les <c>IAmbiguityScanner</c> closed) pour ne pas forcer la
    /// notion de caret dans la sémantique des ambig fermées qui n'en ont
    /// pas besoin. Cf. ADR
    /// <c>2026-05-21-Meta-pattern-templates-vs-ambig-closed</c>.</para>
    /// </summary>
    public sealed class PatternScanContext
    {
        /// <summary>AST top-1 produit par le parser sur la source mutée.</summary>
        public AstNode TopAst { get; }

        /// <summary>LaTeX rendu à partir de <see cref="TopAst"/>.</summary>
        public string TopLatex { get; }

        /// <summary>Source brute (post préprocesseurs + <c>ApplyPreferences</c>).</summary>
        public string Source { get; }

        /// <summary>Position du caret dans <see cref="Source"/>, ou <c>null</c> si
        /// inconnue. Indexée par offset source.</summary>
        public int? CaretOffset { get; }

        public PatternScanContext(AstNode topAst, string topLatex, string source, int? caretOffset)
        {
            TopAst = topAst;
            TopLatex = topLatex ?? string.Empty;
            Source = source ?? string.Empty;
            CaretOffset = caretOffset;
        }
    }
}
