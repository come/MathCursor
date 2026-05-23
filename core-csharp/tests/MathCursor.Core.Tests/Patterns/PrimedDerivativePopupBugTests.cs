using System.Linq;
using Xunit;
using MathCursor.Core.Patterns;

namespace MathCursor.Core.Tests.Patterns
{
    /// <summary>
    /// Bug 2026-05-21 (= P9g+ test diag) : pour la source <c>F'(x)=1/x</c>,
    /// la popup affichait <b>3 completions</b> : <c>(x, ▢)</c> (interval-union),
    /// <c>(x, ▢)</c> (ensemble qui délègue à interval-union), puis <c>F'(x)</c>
    /// (= primed-derivative correct). Les 2 premières étaient parasites :
    /// <c>IntervalUnionTemplate</c> matchait le <c>(</c> à position 2 car le
    /// boundary check n'invalidait que <c>IsLetterOrDigit('\'')</c> qui est
    /// <c>false</c> (= apostrophe), laissant passer <c>(x</c> comme intervalle.
    ///
    /// <para>Le fix consiste à étendre la guard <c>(</c> dans
    /// <see cref="MathCursor.Core.Patterns.Templates.IntervalUnionTemplate"/>
    /// pour aussi rejeter les apostrophes/primes (ASCII + Unicode variants).</para>
    /// </summary>
    public class PrimedDerivativePopupBugTests
    {
        private static PatternScanContext Ctx(string source) =>
            new PatternScanContext(
                topAst: null,
                topLatex: source,
                source: source,
                caretOffset: null,
                startPos: 0,
                registry: DefaultPatternRegistry.Build());

        [Fact]
        public void Source_F_prime_with_args_yields_only_primed_derivative()
        {
            // Le user a tapé "F'(x)=1/x". Attendu : une seule completion =
            // primed-derivative `F'(x)`. Pas de match interval-union parasite
            // sur `(x)`, donc pas de doublon Ensemble→Interval via délégation.
            var (pipeline, registry) = DefaultPatternRegistry.BuildBoth();
            var ctx = new PatternScanContext(
                topAst: null,
                topLatex: "F'(x)=1/x",
                source: "F'(x)=1/x",
                caretOffset: null,
                startPos: 0,
                registry: registry);

            var completions = pipeline.Run(ctx);

            // Avant fix : 3 completions (2 IntervalUnion-shape + 1 PrimedDerivative).
            // Après fix : 1 seule = primed-derivative.
            Assert.DoesNotContain(completions, c =>
                c.PreviewLatex.Contains(",") && c.PreviewLatex.Contains("\\left("));
            Assert.Contains(completions, c => c.PreviewLatex == "F'(x)");
        }

        [Theory]
        [InlineData("f'(x)")]
        [InlineData("g''(x+1)")]
        [InlineData("h\"(t)")]
        [InlineData("f’(x)")]  // U+2019 (Word auto-correct ASCII ' → ’)
        public void Common_primed_function_calls_dont_trigger_interval_union(string source)
        {
            // Tous ces cas sont des function calls de dérivée. Aucun ne doit
            // produire un match interval-union (= forme avec virgule + brackets).
            var (pipeline, registry) = DefaultPatternRegistry.BuildBoth();
            var ctx = new PatternScanContext(
                topAst: null, topLatex: source, source: source,
                caretOffset: null, startPos: 0, registry: registry);

            var completions = pipeline.Run(ctx);
            Assert.DoesNotContain(completions, c =>
                c.PreviewLatex.Contains(",") && c.PreviewLatex.Contains("\\left("));
        }
    }
}
