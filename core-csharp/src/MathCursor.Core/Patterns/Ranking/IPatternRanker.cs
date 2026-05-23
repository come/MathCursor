using System.Collections.Generic;

namespace MathCursor.Core.Patterns.Ranking
{
    /// <summary>
    /// Étape de tri/filtrage entre <see cref="PatternPipeline"/> (= matching)
    /// et <see cref="MathCursor.Core.ResolvedZone.PatternCompletions"/>
    /// (= consommation popup). Reçoit la liste brute de <see cref="PatternCompletion"/>
    /// produite par les templates et retourne la liste **affichable** :
    /// dédupliquée, scorée, et filtrée par NMS (= overlap).
    ///
    /// <para>Pure fonction, idempotente : <c>Rank(Rank(x, ctx), ctx) == Rank(x, ctx)</c>.</para>
    ///
    /// <para>Cf. ADR <c>2026-05-21-Feat-pattern-ranker</c> (P10).</para>
    /// </summary>
    public interface IPatternRanker
    {
        /// <summary>
        /// Trie/filtre <paramref name="raw"/>. Doit retourner une liste
        /// non-null (vide si <paramref name="raw"/> vide). L'ordre du résultat
        /// est l'ordre d'affichage attendu côté popup (= meilleur score en
        /// premier).
        /// </summary>
        IReadOnlyList<PatternCompletion> Rank(
            IReadOnlyList<PatternCompletion> raw,
            PatternScanContext ctx);
    }
}
