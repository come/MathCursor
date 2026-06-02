using System.Collections.Generic;

namespace MathCursor.Engine.Rewriting
{
    /// <summary>Résultat d'une résolution du <see cref="RewriteEngine"/>.</summary>
    public sealed class RewriteResult
    {
        /// <summary>LaTeX de la meilleure lecture.</summary>
        public string TopLatex { get; }

        /// <summary>Items finaux restants (= 1 si tout absorbé).</summary>
        public IReadOnlyList<Item> Items { get; }

        /// <summary>Lectures alternatives (= collisions). Chaque lecture est la
        /// liste d'Items RÉSOLUE d'un ordre de composition concurrent (Principe 5).
        /// Structures pures — la sérialisation LaTeX se fait en dernière étape
        /// (adapter), jamais ici. Cf. ADR 2026-05-30-Feat-beam-search-principe-5.</summary>
        public IReadOnlyList<IReadOnlyList<Item>> Alternatives { get; }

        /// <summary>Id de la règle « top » (= dernière appliquée englobante).</summary>
        public string RuleId { get; }

        /// <summary>Trace debug : règles appliquées dans l'ordre (= diag).</summary>
        public IReadOnlyList<string> Trace { get; }

        public RewriteResult(string topLatex, IReadOnlyList<Item> items,
            IReadOnlyList<IReadOnlyList<Item>> alternatives, string ruleId,
            IReadOnlyList<string>? trace = null)
        {
            TopLatex = topLatex ?? string.Empty;
            Items = items;
            Alternatives = alternatives;
            RuleId = ruleId ?? string.Empty;
            Trace = trace ?? System.Array.Empty<string>();
        }

        public static RewriteResult Empty { get; } = new RewriteResult(
            "", new List<Item>(), new List<IReadOnlyList<Item>>(), "");
    }
}
