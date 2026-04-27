using System.Collections.Generic;
using MathCursor.Core.Lattice;

namespace MathCursor.Core
{
    /// <summary>
    /// Façade du moteur lattice : enchaîne Lex → TopK → Parse → Render et
    /// expose une API conforme à <see cref="ILatexEngine"/> pour que l'adapter
    /// VSTO reste inchangé.
    ///
    /// Pour l'instant on ne renvoie qu'UNE suggestion (le top-1). La gestion
    /// des alternatives via la popup d'ambiguïté viendra phase 5 — elle
    /// utilisera <see cref="AmbiguityDetector"/> pour produire des suggestions
    /// supplémentaires uniquement quand le segment ambigu est non-trivial.
    /// </summary>
    public sealed class LatticeEngine : ILatexEngine
    {
        public IReadOnlyList<LatexSuggestion> Convert(string rawSpan)
        {
            if (string.IsNullOrWhiteSpace(rawSpan))
                return System.Array.Empty<LatexSuggestion>();

            var trimmed = rawSpan.Trim();
            var edges = Lexer.Lex(trimmed);
            var paths = LatticePathFinder.TopK(edges, trimmed.Length, 3);
            if (paths.Count == 0)
                return System.Array.Empty<LatexSuggestion>();

            var top = paths[0];
            var ast = new Parser(top.Edges).Parse();
            var latex = LatexRenderer.Render(ast);

            // Score : on inverse le coût Dijkstra. Coût faible = chemin
            // plausible = score élevé. La constante 100 donne un score
            // "humainement lisible" pour la popup (typiquement 95-100 pour
            // une formule reconnue, dégrade vers 0 pour les paths longs).
            double score = System.Math.Max(0, 100 - top.Cost);

            return new[]
            {
                new LatexSuggestion(
                    latex: latex,
                    patternId: "lattice",
                    score: score,
                    consumedTokens: trimmed.Length,
                    totalTokens: trimmed.Length,
                    isPartial: false),
            };
        }

        /// <summary>
        /// Factory pour rester aligné avec le contrat de
        /// <see cref="NotImplementedEngine.LoadEmbedded"/> (que l'adapter
        /// VSTO appelle au démarrage). Le paramètre langue est ignoré : la
        /// langue est gérée par le vocabulaire interne du lexer.
        /// </summary>
        public static LatticeEngine LoadEmbedded(string language = "fr")
            => new LatticeEngine();
    }
}
