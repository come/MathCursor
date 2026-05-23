using System.Collections.Generic;
using MathCursor.Core;
using MathCursor.Core.Lattice;
using MathCursor.Core.Patterns;
using MathCursor.Engine;

namespace MathCursor.Engine.Adapter
{
    /// <summary>
    /// Adapter Engine v2 → contrat <see cref="ResolvedZone"/> legacy.
    /// Drop-in transparent : un <see cref="ZoneResolver"/> peut être construit
    /// avec un <see cref="IEngineFrontend"/> et déléguer toute la résolution
    /// au nouveau moteur, sans toucher l'adapter VSTO.
    ///
    /// <para>Mapping :</para>
    /// <list type="bullet">
    ///   <item><see cref="EngineResult.TopLatex"/> → <see cref="ResolvedZone.TopLatex"/></item>
    ///   <item><see cref="EngineResult.IsComplete"/> → !<see cref="ResolvedZone.IsIncomplete"/></item>
    ///   <item><see cref="EngineResult.Collisions"/> → <see cref="ResolvedZone.PatternCompletions"/>
    ///     (= consommé comme une popup IntelliSense brief §2.4)</item>
    /// </list>
    ///
    /// <para>Cf. ADR <c>2026-05-22-Feat-engine-poc-isolation</c>.</para>
    /// </summary>
    public static class EngineToResolvedZone
    {
        public static ResolvedZone Map(string rawSource, EngineResult engineResult)
        {
            if (engineResult == null) engineResult = EngineResult.Empty;

            var patternCompletions = MapCollisionsToPatternCompletions(engineResult.Collisions);

            return new ResolvedZone(
                rawSource: rawSource ?? string.Empty,
                mutedSource: rawSource ?? string.Empty,
                topLatex: engineResult.TopLatex,
                spot: null,
                spotStart: null,
                spotEnd: null,
                allMatches: System.Array.Empty<AmbiguityMatch>(),
                isIncomplete: !engineResult.IsComplete,
                baseTopLatex: engineResult.TopLatex,
                patternCompletions: patternCompletions);
        }

        private static IReadOnlyList<PatternCompletion> MapCollisionsToPatternCompletions(
            IReadOnlyList<EngineCandidate> collisions)
        {
            if (collisions == null || collisions.Count == 0)
                return System.Array.Empty<PatternCompletion>();
            var list = new List<PatternCompletion>(collisions.Count);
            foreach (var c in collisions)
            {
                list.Add(new PatternCompletion(
                    description: c.Description,
                    previewLatex: c.Latex,
                    hintLatex: c.Latex,
                    mutation: null,
                    completenessScore: c.Score));
            }
            return list;
        }
    }
}
