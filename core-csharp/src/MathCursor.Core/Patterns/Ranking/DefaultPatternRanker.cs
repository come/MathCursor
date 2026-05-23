using System.Collections.Generic;
using System.Linq;

namespace MathCursor.Core.Patterns.Ranking
{
    /// <summary>
    /// Implémentation par défaut du <see cref="IPatternRanker"/>. Algorithme
    /// en 3 étapes :
    ///
    /// <list type="number">
    ///   <item><b>Dédup exact</b> — 2 completions à clé identique
    ///     <c>(SourceStart, SourceEnd, PreviewLatex)</c> → garder la première
    ///     (ordre de production stable = ordre Order asc des templates).</item>
    ///   <item><b>Score composite</b> — <c>CompletenessScore</c> du template
    ///     + bonus span complet (<c>+30</c> si le span couvre toute la source)
    ///     + bonus caret-aware (<c>+15</c> si le caret est dans le span).</item>
    ///   <item><b>NMS overlap</b> — 2 completions dont les spans se chevauchent
    ///     ne peuvent pas coexister ; la moins scorée est jetée. Égalité de
    ///     score → garder la première (= déterminisme).</item>
    /// </list>
    ///
    /// <para>Cf. ADR <c>2026-05-21-Feat-pattern-ranker</c> (P10).</para>
    /// </summary>
    public sealed class DefaultPatternRanker : IPatternRanker
    {
        /// <summary>Bonus appliqué quand le span d'une completion couvre toute
        /// la <see cref="PatternScanContext.Source"/>.</summary>
        public const int BonusSpanComplete = 30;

        /// <summary>Bonus appliqué quand <see cref="PatternScanContext.CaretOffset"/>
        /// est dans le span <c>[SourceStart, SourceEnd]</c>.</summary>
        public const int BonusCaretInsideSpan = 15;

        public IReadOnlyList<PatternCompletion> Rank(
            IReadOnlyList<PatternCompletion> raw, PatternScanContext ctx)
        {
            if (raw == null || raw.Count == 0) return System.Array.Empty<PatternCompletion>();

            // 1. Dédup exact sur (SourceStart, SourceEnd, PreviewLatex).
            var deduped = Dedup(raw);

            // 2. Score composite.
            var scored = deduped
                .Select(c => (Completion: c, Score: ComputeScore(c, ctx)))
                .OrderByDescending(p => p.Score)
                .ToList();

            // 3. NMS overlap : itérer du meilleur au pire, garder un span qu'on
            //    n'a pas déjà couvert. Égalité de score → ordre stable (le 1er
            //    rencontré gagne — l'OrderByDescending est stable côté LINQ).
            var kept = new List<PatternCompletion>(scored.Count);
            foreach (var (completion, _) in scored)
            {
                if (HasOverlap(completion, kept)) continue;
                kept.Add(completion);
            }
            return kept;
        }

        private static List<PatternCompletion> Dedup(IReadOnlyList<PatternCompletion> raw)
        {
            var seen = new HashSet<(int, int, string)>();
            var result = new List<PatternCompletion>(raw.Count);
            foreach (var c in raw)
            {
                var key = (c.SourceStart, c.SourceEnd, c.PreviewLatex ?? string.Empty);
                if (seen.Add(key)) result.Add(c);
            }
            return result;
        }

        /// <summary>
        /// Calcule le score composite d'une completion dans son contexte.
        /// Exposé public pour réutilisation par des rankers custom qui veulent
        /// hériter de cette stratégie de scoring.
        /// </summary>
        public static int ComputeScore(PatternCompletion c, PatternScanContext ctx)
        {
            int score = c.CompletenessScore;

            // Span info indispo (= legacy call-site sans SourceStart/End) → pas
            // de bonus géométrique, on retombe sur le seul CompletenessScore.
            if (c.SourceStart < 0 || c.SourceEnd < 0 || ctx == null)
                return score;

            int sourceLen = ctx.Source?.Length ?? 0;
            if (sourceLen > 0
                && c.SourceStart == 0
                && c.SourceEnd == sourceLen)
            {
                score += BonusSpanComplete;
            }

            if (ctx.CaretOffset is int caret
                && caret >= c.SourceStart
                && caret <= c.SourceEnd)
            {
                score += BonusCaretInsideSpan;
            }

            return score;
        }

        private static bool HasOverlap(PatternCompletion candidate, List<PatternCompletion> kept)
        {
            // Span indispo → on ne peut pas calculer d'overlap, on garde.
            // (Le ranker dégrade gracieusement pour les call-sites legacy.)
            if (candidate.SourceStart < 0 || candidate.SourceEnd < 0) return false;

            foreach (var k in kept)
            {
                if (k.SourceStart < 0 || k.SourceEnd < 0) continue;
                // Overlap au sens "intervalles se chevauchent" (= strict, pas
                // d'inclusion stricte requise). Deux completions tangentes
                // (end == start) ne se chevauchent pas.
                if (candidate.SourceStart < k.SourceEnd
                    && k.SourceStart < candidate.SourceEnd)
                    return true;
            }
            return false;
        }
    }
}
