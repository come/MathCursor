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
        /// <summary>AST top-1 produit par le parser sur la source mutée.
        /// <para>Nullable depuis P7a (2026-05-21) : aucun des templates pilotes
        /// actuels (forall-belongs, ensemble, interval-union) ne consomme l'AST
        /// — ils scannent <see cref="Source"/>. Quand le <c>ZoneResolver</c>
        /// invoque le <c>PatternPipeline</c>, l'AST n'est pas exposé par
        /// <c>LatticeEngine.ConvertWithAmbiguity</c> et reste null. Si un futur
        /// template AST-aware en P9+ a besoin de l'AST, il fera <c>if (ctx.TopAst != null)</c>
        /// et le caller (resolver) sera adapté.</para></summary>
        public AstNode? TopAst { get; }

        /// <summary>LaTeX rendu à partir de <see cref="TopAst"/>.</summary>
        public string TopLatex { get; }

        /// <summary>Source brute (post préprocesseurs + <c>ApplyPreferences</c>).</summary>
        public string Source { get; }

        /// <summary>Position du caret dans <see cref="Source"/>, ou <c>null</c> si
        /// inconnue. Indexée par offset source.</summary>
        public int? CaretOffset { get; }

        /// <summary>
        /// Position de départ pour le scan dans <see cref="Source"/>. Default 0
        /// pour les appels top-level. Les sub-patterns (composition parent↔enfant
        /// via <see cref="PatternRefSlot"/>) construisent un sub-contexte avec
        /// <see cref="StartPos"/> ajustée pour scanner depuis la fin de l'opener
        /// parent. Ajouté en P5 pour la composition. Les templates qui ne
        /// supportent pas la composition peuvent l'ignorer (= comportement
        /// rétro-compat scan from 0).
        /// </summary>
        public int StartPos { get; }

        /// <summary>
        /// Registre des templates pour résoudre les <see cref="PatternRefSlot"/>
        /// (composition parent↔enfant). <c>null</c> = template isolé, pas de
        /// composition possible. Ajouté en P5 ; les templates P3/P4 fonctionnent
        /// sans (rétro-compat).
        /// </summary>
        public PatternRegistry? Registry { get; }

        public PatternScanContext(AstNode? topAst, string topLatex, string source, int? caretOffset)
            : this(topAst, topLatex, source, caretOffset, startPos: 0, registry: null) { }

        public PatternScanContext(
            AstNode? topAst, string topLatex, string source,
            int? caretOffset, int startPos, PatternRegistry? registry)
        {
            TopAst = topAst;
            TopLatex = topLatex ?? string.Empty;
            Source = source ?? string.Empty;
            CaretOffset = caretOffset;
            StartPos = startPos < 0 ? 0 : startPos;
            Registry = registry;
        }

        /// <summary>
        /// Construit un nouveau <see cref="PatternScanContext"/> identique avec
        /// <see cref="StartPos"/> ajustée. Utilisé par les templates parents
        /// pour déléguer à un sub-pattern à partir d'une position arbitraire.
        /// </summary>
        public PatternScanContext WithStartPos(int newStartPos)
            => new PatternScanContext(TopAst, TopLatex, Source, CaretOffset, newStartPos, Registry);
    }
}
