using System.Collections.Generic;
using MathCursor.Core.Lattice;

namespace MathCursor.Core
{
    /// <summary>
    /// Façade du moteur lattice : enchaîne Lex → TopK → Parse → Render et
    /// expose une API conforme à <see cref="ILatexEngine"/> pour que l'adapter
    /// VSTO reste inchangé.
    ///
    /// Phase 5a : retourne plusieurs suggestions quand le top-K Dijkstra
    /// produit des chemins de coût comparable (ratio relatif). Le top-1 est
    /// toujours en première position de la liste — l'adapter VSTO peut
    /// l'utiliser comme suggestion par défaut. Les alternatives sémantiques
    /// (vec(AB), racine V(...), …) viendront en phase 5b via un module
    /// AlternativeGenerator distinct, branché sur l'AST top-1.
    /// </summary>
    public sealed class LatticeEngine : ILatexEngine
    {
        // Delta absolu max entre coût top-1 et coût d'une alternative. 5 =
        // "tolère un écart d'un identifiant 1 lettre". Choix delta absolu
        // (et pas ratio) : sur les longues formules, le coût total des deux
        // paths est proche numériquement même si une portion locale diverge
        // énormément (ex: "sum k=1 n+1 cos2x" — top-1 ~17, alt ~30, ratio
        // 1.76 mais l'écart vient EXCLUSIVEMENT de "cos" vs "c·o·s" qui est
        // un faux positif). Le delta absolu reflète mieux la divergence
        // locale du segment ambigu sans avoir à reconstruire les sous-paths.
        private const int AlternativeCostDelta = 5;

        // Top-1 + jusqu'à 3 alternatives = 4 items max dans la popup. Au-delà
        // c'est illisible dans une fenêtre étroite.
        private const int MaxSuggestions = 4;

        // Largeur de fenêtre top-K Dijkstra : on en prend plus que MaxSuggestions
        // pour tolérer la déduplication (deux paths peuvent rendre le même LaTeX).
        private const int TopKWidth = 6;

        public IReadOnlyList<LatexSuggestion> Convert(string rawSpan)
        {
            if (string.IsNullOrWhiteSpace(rawSpan))
                return System.Array.Empty<LatexSuggestion>();

            var trimmed = rawSpan.Trim();
            var edges = Lexer.Lex(trimmed);
            var paths = LatticePathFinder.TopK(edges, trimmed.Length, TopKWidth);
            if (paths.Count == 0)
                return System.Array.Empty<LatexSuggestion>();

            var topCost = paths[0].Cost;
            int maxDelta = AlternativeCostDelta;

            var seenLatex = new HashSet<string>();
            var suggestions = new List<LatexSuggestion>();
            foreach (var p in paths)
            {
                // Le top-1 entre toujours. Les alternatives doivent être à
                // delta ≤ MaxDelta du coût top-1.
                if (suggestions.Count > 0 && (p.Cost - topCost) > maxDelta) break;

                var ast = new Parser(p.Edges).Parse();
                var latex = LatexRenderer.Render(ast);
                if (!seenLatex.Add(latex)) continue; // dédupe sur LaTeX

                double score = System.Math.Max(0, 100 - p.Cost);
                suggestions.Add(new LatexSuggestion(
                    latex: latex,
                    patternId: "lattice",
                    score: score,
                    consumedTokens: trimmed.Length,
                    totalTokens: trimmed.Length,
                    isPartial: false));
                if (suggestions.Count >= MaxSuggestions) break;
            }
            return suggestions;
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
