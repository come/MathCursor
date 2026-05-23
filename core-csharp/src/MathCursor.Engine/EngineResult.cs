using System.Collections.Generic;

namespace MathCursor.Engine
{
    /// <summary>
    /// Résultat d'une résolution <see cref="IEngineFrontend.Resolve"/>.
    /// </summary>
    public sealed class EngineResult
    {
        /// <summary>LaTeX produit (= meilleure interprétation si pas de collision,
        /// premier candidat sinon). Vide si rien n'a matché.</summary>
        public string TopLatex { get; }

        /// <summary><c>true</c> si tous les slots requis sont remplis et la
        /// passe n'a pas laissé de cadre ouvert.</summary>
        public bool IsComplete { get; }

        /// <summary>Liste des candidats sur collision (= ≥ 2 lectures
        /// valides). Vide si pas de collision. Brief §2.4.</summary>
        public IReadOnlyList<EngineCandidate> Collisions { get; }

        /// <summary>Nom de la règle qui a produit <see cref="TopLatex"/>
        /// (= <c>RuleSpec.Id</c>), ou chaîne vide pour un rendu plat sans règle.</summary>
        public string RuleId { get; }

        public EngineResult(string topLatex, bool isComplete,
            IReadOnlyList<EngineCandidate> collisions, string ruleId)
        {
            TopLatex = topLatex ?? string.Empty;
            IsComplete = isComplete;
            Collisions = collisions ?? System.Array.Empty<EngineCandidate>();
            RuleId = ruleId ?? string.Empty;
        }

        public static EngineResult Empty { get; } = new EngineResult(
            string.Empty, false, System.Array.Empty<EngineCandidate>(), string.Empty);
    }

    /// <summary>
    /// Candidat sur collision — brief §2.4. Présenté à l'user comme dans
    /// une autocomplétion d'IDE (= popup IntelliSense).
    /// </summary>
    public sealed class EngineCandidate
    {
        public string Latex { get; }
        public string Description { get; }
        public string RuleId { get; }
        public int Score { get; }

        public EngineCandidate(string latex, string description, string ruleId, int score)
        {
            Latex = latex ?? string.Empty;
            Description = description ?? string.Empty;
            RuleId = ruleId ?? string.Empty;
            Score = score;
        }
    }
}
