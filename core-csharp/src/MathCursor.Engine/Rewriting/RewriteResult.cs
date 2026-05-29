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

        /// <summary>Lectures alternatives (= collisions à exposer en popup).</summary>
        public IReadOnlyList<RewriteMatch> Alternatives { get; }

        /// <summary>Id de la règle « top » (= dernière appliquée englobante).</summary>
        public string RuleId { get; }

        public RewriteResult(string topLatex, IReadOnlyList<Item> items,
            IReadOnlyList<RewriteMatch> alternatives, string ruleId)
        {
            TopLatex = topLatex ?? string.Empty;
            Items = items;
            Alternatives = alternatives;
            RuleId = ruleId ?? string.Empty;
        }

        public static RewriteResult Empty { get; } = new RewriteResult(
            "", new List<Item>(), new List<RewriteMatch>(), "");
    }
}
