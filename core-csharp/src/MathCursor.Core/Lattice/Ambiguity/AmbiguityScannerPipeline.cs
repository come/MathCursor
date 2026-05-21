using System.Collections.Generic;
using System.Linq;
using MathCursor.Core.Lattice.Ambiguity.Scanners;

namespace MathCursor.Core.Lattice.Ambiguity
{
    /// <summary>
    /// Orchestrateur des <see cref="IAmbiguityScanner"/>. Trie les scanners
    /// par <see cref="IAmbiguityScanner.Order"/> et les exécute en cascade ;
    /// chaque scanner observe / mute le <c>consumed[]</c> partagé pour
    /// empêcher la double-émission par les scanners ultérieurs.
    ///
    /// <para>Tri post-collecte : priorité par règle
    /// (<see cref="AlternativeGenerator.GetRulePriority"/>) puis rightmost
    /// first (= la plus proche du caret en saisie).</para>
    ///
    /// <para>Cf. ADR <c>2026-05-13-Refactor-ambiguity-scanners-strategy</c>.</para>
    /// </summary>
    public sealed class AmbiguityScannerPipeline
    {
        private readonly IReadOnlyList<IAmbiguityScanner> _scanners;

        public AmbiguityScannerPipeline(IEnumerable<IAmbiguityScanner> scanners)
        {
            _scanners = scanners.OrderBy(s => s.Order).ToList();
        }

        /// <summary>Pipeline par défaut. P6 (2026-05-21) a retiré
        /// <c>VAsForallEAsExistsScanner</c> et <c>CanonicalSetLettersScanner</c>
        /// — leur comportement est désormais couvert par les templates
        /// <c>ForallBelongsTemplate</c> et <c>EnsembleTemplate</c> du chantier
        /// Patterns (cf. ADR <c>2026-05-21-Refactor-remove-legacy-quantifier-set-scanners</c>).
        /// 8 scanners restants pour les ambig closed.</summary>
        public static AmbiguityScannerPipeline Default { get; } = new AmbiguityScannerPipeline(new IAmbiguityScanner[]
        {
            new AngleTwoLetterPlaceholderScanner(),     // 0
            new AstBasedScanner(),                       // 1
            new DecoratedTwoThreeUpperScanner(),         // 2
            new UppercaseSequencesScanner(),             // 3
            new FunctionTypicalCommaCoordsScanner(),     // 6
            new VectorLayoutFlipTopLevelScanner(),       // 7
            new TightChainExtensionScanner(),            // 8
            new DecimalVsMultiplicationScanner(),        // 9
        });

        /// <summary>
        /// Exécute la pipeline et retourne tous les matches triés
        /// (priorité ruleId + rightmost first).
        /// </summary>
        public IReadOnlyList<AmbiguityMatch> Run(ScanContext ctx)
        {
            var matches = new List<AmbiguityMatch>();
            var consumed = new bool[ctx.TopLatex.Length];
            foreach (var scanner in _scanners)
                scanner.Scan(ctx, matches, consumed);

            // Tri : priorité aux règles structurantes (V→∀, E→∃ qui changent
            // la sémantique globale) puis aux règles locales. Égalité priorité
            // → rightmost first (= la plus proche du caret).
            matches.Sort((a, b) =>
            {
                int prioA = AlternativeGenerator.GetRulePriority(a.Spot.RuleId);
                int prioB = AlternativeGenerator.GetRulePriority(b.Spot.RuleId);
                if (prioA != prioB) return prioA.CompareTo(prioB);
                return b.Start.CompareTo(a.Start);
            });
            return matches;
        }
    }
}
