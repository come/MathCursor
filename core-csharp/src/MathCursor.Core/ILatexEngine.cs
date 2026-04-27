using System;
using System.Collections.Generic;

namespace MathCursor.Core
{
    /// <summary>
    /// Contrat du moteur de conversion span → suggestions LaTeX. Le code adapter
    /// (VSTO + tests) s'adresse à cette interface ; l'implémentation est
    /// remplacée à mesure qu'on développe le nouveau lattice engine.
    ///
    /// Pendant la phase de transition (PatternEngine supprimé, lattice pas
    /// encore prêt), <see cref="NotImplementedEngine"/> sert de placeholder :
    /// tous les appels jettent <see cref="NotImplementedException"/>, donc les
    /// tests compilent mais échouent — ce qui est l'état attendu pendant le
    /// développement par phases.
    /// </summary>
    public interface ILatexEngine
    {
        IReadOnlyList<LatexSuggestion> Convert(string rawSpan);
    }

    /// <summary>Suggestion LaTeX produite par le moteur.</summary>
    public sealed class LatexSuggestion
    {
        public string Latex { get; }
        public string PatternId { get; }
        public double Score { get; }
        public int ConsumedTokens { get; }
        public int TotalTokens { get; }
        public bool IsPartial { get; }

        public LatexSuggestion(
            string latex,
            string patternId,
            double score,
            int consumedTokens,
            int totalTokens,
            bool isPartial = false)
        {
            Latex = latex ?? "";
            PatternId = patternId ?? "";
            Score = score;
            ConsumedTokens = consumedTokens;
            TotalTokens = totalTokens;
            IsPartial = isPartial;
        }
    }

    /// <summary>
    /// Placeholder pendant le développement du lattice engine. Conserve
    /// l'API attendue mais lance NotImplementedException pour signaler que
    /// le vrai moteur n'est pas encore branché.
    /// </summary>
    public sealed class NotImplementedEngine : ILatexEngine
    {
        public IReadOnlyList<LatexSuggestion> Convert(string rawSpan)
            => throw new NotImplementedException(
                "Lattice engine pas encore implémenté. Cf. branche lattice-engine, phase 1.");

        public static NotImplementedEngine LoadEmbedded(string language = "fr")
            => new NotImplementedEngine();
    }
}
