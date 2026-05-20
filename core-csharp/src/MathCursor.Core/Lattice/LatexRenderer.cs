using MathCursor.Core.Lattice.Ast;

namespace MathCursor.Core.Lattice
{
    /// <summary>
    /// Façade publique du rendu AST → LaTeX. La logique vit désormais dans
    /// <see cref="LatexRenderingVisitor"/> (pattern Visitor sur AST). Cette
    /// classe garde l'API <see cref="Render(AstNode?)"/> et <see cref="GlobalOptions"/>
    /// pour compatibilité avec tous les appelants existants.
    ///
    /// <para>Cf. ADR <c>2026-05-13-Refactor-ast-visitor.md</c> (étape 4 du
    /// refacto extensibilité). Avant ce refacto : un <c>switch (node)</c>
    /// exhaustif sur 18 types AST vivait ici. Le switch est remplacé par un
    /// dispatch <c>node.Accept(visitor)</c> qui force l'exhaustivité au
    /// niveau de l'interface <see cref="IAstVisitor{TResult}"/>.</para>
    /// </summary>
    public static class LatexRenderer
    {
        /// <summary>
        /// Options de rendu globales (configurées par l'adapter au démarrage).
        /// Cf. <see cref="MathCursor.Core.RenderOptions"/>. Notamment :
        /// <see cref="RenderOptions.MultSymbol"/> (`\times` ou `\cdot` selon
        /// culture/Registry) appliqué au rendu de Bin("*") explicite.
        ///
        /// <para>Le visiteur lit ces options à chaque <see cref="Render(AstNode?)"/>
        /// (un visiteur frais est instancié par appel pour rester aligné sur
        /// la dernière valeur — pas de capture stale au démarrage).</para>
        /// </summary>
        public static RenderOptions GlobalOptions { get; set; } = new RenderOptions();

        /// <summary>
        /// Rend un AST en LaTeX. Délègue à <see cref="LatexRenderingVisitor"/>
        /// via dispatch virtuel <c>node.Accept(visitor)</c>.
        /// </summary>
        public static string Render(AstNode? node)
        {
            if (node == null) return string.Empty;
            var visitor = new LatexRenderingVisitor(GlobalOptions);
            return node.Accept(visitor);
        }
    }
}
