using System.Linq;
using MathCursor.Core.PatternEngine;
using Engine = MathCursor.Core.PatternEngine.PatternEngine;
using Xunit;
using Xunit.Abstractions;

namespace MathCursor.Core.Tests
{
    /// <summary>
    /// Tests du matching préfixe : quand aucun pattern complet ne matche le span
    /// courant, PatternEngine.ConvertPartials boucle sur tous les patterns en
    /// acceptant qu'ils se terminent prématurément (tokens épuisés mid-pattern).
    /// Les slots non remplis sortent en \ldots.
    ///
    /// Comportement attendu :
    ///  - Pas de match complet → partiels proposés.
    ///  - Au moins un match complet → aucun partiel (filet de secours uniquement,
    ///    option (a) du design).
    ///  - Partiels marqués IsPartial=true et contiennent \ldots.
    /// </summary>
    public sealed class PartialMatchingTests
    {
        private readonly ITestOutputHelper _log;
        public PartialMatchingTests(ITestOutputHelper log) { _log = log; }

        private void DumpSuggestions(System.Collections.Generic.IReadOnlyList<LatexSuggestion> suggestions, string input)
        {
            _log.WriteLine($"Input: \"{input}\"  → {suggestions.Count} suggestions :");
            foreach (var s in suggestions.Take(5))
                _log.WriteLine($"  partial={s.IsPartial}  score={s.Score,8:F1}  pattern={s.PatternId,-30}  latex=\"{s.Latex}\"");
        }

        [Fact]
        public void Incomplete_interval_yields_partial_with_ldots()
        {
            var engine = Engine.LoadEmbedded("fr");
            // "]-inf" = fragment d'intervalle ouvert-X. Aucun pattern complet ne
            // matche → fallback partial sur interval_open_* qui ont RBRACKET en tête.
            var suggestions = engine.Convert("]-inf");
            DumpSuggestions(suggestions, "]-inf");

            Assert.NotEmpty(suggestions);
            Assert.All(suggestions, s => Assert.True(s.IsPartial, $"expected all partial, got non-partial: {s.PatternId}"));
            Assert.Contains(suggestions, s => s.Latex.Contains("\\ldots"));
            // L'un des candidats doit commencer par "]" (borne gauche ouverte).
            Assert.Contains(suggestions, s => s.Latex.StartsWith("]"));
        }

        [Fact]
        public void Complete_interval_does_not_yield_partial()
        {
            var engine = Engine.LoadEmbedded("fr");
            // "]0;1]" matche interval_open_closed complètement → aucun partial.
            var suggestions = engine.Convert("]0;1]");
            DumpSuggestions(suggestions, "]0;1]");

            Assert.NotEmpty(suggestions);
            Assert.All(suggestions, s => Assert.False(s.IsPartial, $"expected no partial, got partial: {s.PatternId}"));
            Assert.DoesNotContain(suggestions, s => s.Latex.Contains("\\ldots"));
        }

        [Fact]
        public void Partial_has_lower_priority_than_complete_match()
        {
            var engine = Engine.LoadEmbedded("fr");
            // "f(x)" matche function_call / equation_simple → pas de partial.
            // On vérifie que même si des patterns plus complexes (sum, lim...)
            // pourraient matcher en préfixe, ils ne polluent pas les suggestions.
            var suggestions = engine.Convert("f(x)");
            DumpSuggestions(suggestions, "f(x)");

            Assert.NotEmpty(suggestions);
            Assert.DoesNotContain(suggestions, s => s.IsPartial);
        }

        // Note : le fallback partial n'est déclenché que quand deduped.Count == 0,
        // c.-à-d. quand AUCUN pattern (y compris generic_expression) ne matche.
        // generic_expression étant très greedy (EXPR:full), il absorbe la plupart
        // des entrées — notamment "lim x" → "\lim x" (littéral). Les partials ne
        // sont donc visibles que sur des inputs que generic_expression rejette,
        // typiquement ceux commençant par "]" (RBRACKET en tête d'EXPR rejeté
        // par le check BracketsWellFormed).
        //
        // Amélioration future possible : déclencher aussi les partials quand le
        // meilleur match est generic_expression avec faible couverture. Mais ça
        // change la sémantique "filet de secours" actée dans decisions.md.

        [Fact]
        public void Partial_suggestions_are_capped_at_three()
        {
            var engine = Engine.LoadEmbedded("fr");
            // Input très permissif : beaucoup de patterns peuvent matcher en préfixe.
            // Le cap à 3 évite de saturer la popup.
            var suggestions = engine.Convert("]");
            DumpSuggestions(suggestions, "]");

            Assert.True(suggestions.Count <= 3, $"Expected ≤ 3 partial suggestions, got {suggestions.Count}");
        }
    }
}
