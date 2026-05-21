using System.Collections.Generic;

namespace MathCursor.Core.Patterns
{
    /// <summary>
    /// Variante d'un template "quantifier" (ou polymorphe similaire) : un
    /// head source → un symbole LaTeX + une mutation source canonique.
    /// Permet de factoriser <c>V→∀</c>, <c>E→∃</c>, et plus tard
    /// <c>Σ→\sum</c>, <c>Π→\prod</c>, etc. dans un même template.
    ///
    /// <para><b>Structure data-ready</b> (option γ du plan P5) : les
    /// variants vivent en C# pour P5, mais leur forme reflète celle d'un
    /// futur YAML <c>groups/quantifier.yaml</c>. Migration P9+ ne touchera
    /// pas l'interface du template, juste la source de ces variantes.</para>
    ///
    /// <para>Cf. ADR <c>2026-05-21-Feat-forall-belongs-pattern</c> (P5).</para>
    /// </summary>
    public sealed class QuantifierVariant
    {
        /// <summary>Caractère ou chaîne head dans la source utilisateur
        /// (ex. <c>"V"</c>, <c>"E"</c>, <c>"∀"</c>, <c>"∃"</c>).</summary>
        public string Head { get; }

        /// <summary>Macro LaTeX correspondante (ex. <c>"\forall"</c>,
        /// <c>"\exists"</c>).</summary>
        public string LatexSymbol { get; }

        /// <summary>Token source canonique pour la <c>SourceMutation</c> qui
        /// remplace <see cref="Head"/> (ex. <c>"forall"</c>, <c>"exists"</c>).
        /// Doit matcher un keyword du <c>Vocabulary</c> lattice pour que le
        /// pipeline aval rende le symbole.</summary>
        public string MutationReplacement { get; }

        /// <summary>Poids dans la désambig si plusieurs variants matchent à
        /// la même position (rare). Par défaut 100. Une variant rare ou
        /// douteuse aura un poids inférieur.</summary>
        public int Weight { get; }

        /// <summary>Hints contextuels facultatifs (langue, domaine, etc.)
        /// pour le ranker. Champ pré-réservé pour la migration YAML — non
        /// utilisé en P5.</summary>
        public IReadOnlyDictionary<string, string>? Hints { get; }

        public QuantifierVariant(
            string head, string latexSymbol, string mutationReplacement,
            int weight = 100, IReadOnlyDictionary<string, string>? hints = null)
        {
            Head = head ?? throw new System.ArgumentNullException(nameof(head));
            LatexSymbol = latexSymbol ?? throw new System.ArgumentNullException(nameof(latexSymbol));
            MutationReplacement = mutationReplacement
                ?? throw new System.ArgumentNullException(nameof(mutationReplacement));
            Weight = weight;
            Hints = hints;
        }
    }
}
