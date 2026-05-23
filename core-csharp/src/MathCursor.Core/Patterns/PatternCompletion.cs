using MathCursor.Core.Lattice;

namespace MathCursor.Core.Patterns
{
    /// <summary>
    /// Une proposition affichée par la popup pour un état donné d'un
    /// <see cref="PatternMatch"/>. Peut être une forme partielle (slots vides
    /// matérialisés dans <see cref="HintLatex"/> par des carrés) ou une forme
    /// complète (slots tous remplis).
    ///
    /// <para><see cref="PreviewLatex"/> = ce que l'utilisateur verra rendu s'il
    /// valide cette complétion (slots remplis seulement).
    /// <see cref="HintLatex"/> = même rendu mais avec les slots vides
    /// matérialisés (typiquement <c>\square</c> ou Unicode <c>▭</c>) — sert à
    /// montrer à l'user ce qu'il reste à taper.</para>
    ///
    /// <para><see cref="Mutation"/> optionnelle : si fournie, la sélection de
    /// cette complétion mute la source brute via le mécanisme
    /// <see cref="SourceMutation"/> partagé avec les ambig closed.</para>
    ///
    /// <para>Cf. ADR <c>2026-05-21-Meta-pattern-templates-vs-ambig-closed</c>,
    /// étape P2.</para>
    /// </summary>
    public sealed class PatternCompletion
    {
        public string Description { get; }
        public string PreviewLatex { get; }
        public string HintLatex { get; }
        public SourceMutation? Mutation { get; }

        /// <summary>Score de complétude (0 = juste le head, 100 = tous slots
        /// remplis). Utilisé par la popup pour trier les propositions :
        /// les plus complètes en premier.</summary>
        public int CompletenessScore { get; }

        /// <summary>Position de début du span source matché (= identique à
        /// <c>PatternMatch.SourceStart</c>). <c>-1</c> si non fourni (= legacy
        /// call-sites). Utilisé par <see cref="Ranking.IPatternRanker"/> pour
        /// NMS overlap + bonus span complet + caret-aware.</summary>
        public int SourceStart { get; }

        /// <summary>Position de fin (exclue) du span source matché.
        /// <c>-1</c> si non fourni.</summary>
        public int SourceEnd { get; }

        public PatternCompletion(
            string description,
            string previewLatex,
            string hintLatex,
            SourceMutation? mutation,
            int completenessScore,
            int sourceStart = -1,
            int sourceEnd = -1)
        {
            Description = description ?? throw new System.ArgumentNullException(nameof(description));
            PreviewLatex = previewLatex ?? throw new System.ArgumentNullException(nameof(previewLatex));
            HintLatex = hintLatex ?? throw new System.ArgumentNullException(nameof(hintLatex));
            Mutation = mutation;
            CompletenessScore = completenessScore;
            SourceStart = sourceStart;
            SourceEnd = sourceEnd;
        }
    }
}
