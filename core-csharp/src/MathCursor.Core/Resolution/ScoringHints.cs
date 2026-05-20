using System.Collections.Generic;

namespace MathCursor.Core.Resolution
{
    /// <summary>
    /// Sortie de <see cref="ContextScorer.Aggregate"/> : score cumulé par
    /// alternative + trace explicative pour debug.
    ///
    /// <para>Consommé par <c>ZoneResolver</c> pour décider de la résolution
    /// auto vs popup. Si l'alt gagnante d'un ruleId domine clairement (cf.
    /// seuil), résolution auto. Sinon popup avec les top-N alts.</para>
    /// </summary>
    public sealed class ScoringHints
    {
        /// <summary>Scores cumulés. Clé = <c>"{ruleId}:{altIdx}"</c>.
        /// Valeur = score (somme pondérée des contributions de signal).</summary>
        public IReadOnlyDictionary<string, double> AltScores { get; }

        /// <summary>Trace lisible pour debug : qui a contribué quoi à quel alt.</summary>
        public IReadOnlyList<string> Trace { get; }

        public static ScoringHints Empty { get; } = new ScoringHints(
            new Dictionary<string, double>(), new List<string>());

        public ScoringHints(
            IReadOnlyDictionary<string, double>? altScores,
            IReadOnlyList<string>? trace)
        {
            AltScores = altScores ?? new Dictionary<string, double>();
            Trace = trace ?? new List<string>();
        }

        /// <summary>
        /// Alternative gagnante pour <paramref name="ruleId"/>.
        /// Retourne <c>(altIdx = -1, score = 0)</c> si aucun score n'a été
        /// accumulé pour ce ruleId.
        /// </summary>
        public (int altIdx, double score) BestAltForRule(string? ruleId)
        {
            int best = -1;
            double bestScore = double.NegativeInfinity;
            string prefix = (ruleId ?? string.Empty) + ":";
            foreach (var kv in AltScores)
            {
                if (!kv.Key.StartsWith(prefix)) continue;
                if (kv.Value <= bestScore) continue;
                if (!int.TryParse(kv.Key.Substring(prefix.Length), out int alt)) continue;
                best = alt;
                bestScore = kv.Value;
            }
            return (best, best >= 0 ? bestScore : 0);
        }

        /// <summary>Construit la clé canonique <c>"{ruleId}:{altIdx}"</c>.</summary>
        public static string Key(string? ruleId, int altIdx)
            => (ruleId ?? string.Empty) + ":" + altIdx;
    }
}
