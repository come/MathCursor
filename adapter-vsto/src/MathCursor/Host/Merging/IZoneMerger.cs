namespace MathCursor.Host.Merging
{
    /// <summary>
    /// Contrat d'un merger de zone : tente d'absorber des OMaths/paragraphes
    /// adjacents au commit courant. Cf. ADR <c>2026-05-06-Meta-zone-merger-pipeline</c>.
    /// <para>
    /// Implémentations actuelles : <see cref="IntraOMathsMerger"/>,
    /// <see cref="RevertedMultiLineMerger"/>, <see cref="CasesChainCascadeMerger"/>,
    /// <see cref="MarkerChainCascadeMerger"/>.
    /// </para>
    /// <para>
    /// Contrat (cf. ADR 06-05 sidecar Phase 1.6) : si le résultat retourné
    /// contient des handles absorbés (<c>RemovedHandles.Count &gt; 0</c>), le
    /// <c>MergedSidecar</c> doit être calculé (offsets recalibrés sur la
    /// <c>MergedSource</c>). Sinon les désambiguïsations (vec, ∀, ∃, ...)
    /// des OMaths absorbés seront perdues au reranking. Le pipeline log
    /// un WARN si ce contrat est violé.
    /// </para>
    /// <para>
    /// Self-guarding : si le merger n'est pas applicable au commit courant
    /// (mauvais marker, pas de voisin adjacent, etc.), retourne <c>null</c>.
    /// Le pipeline essaie alors le merger suivant.
    /// </para>
    /// </summary>
    internal interface IZoneMerger
    {
        /// <summary>Nom du merger pour logs/diagnostics. Pas de logique dessus.</summary>
        string Name { get; }

        /// <summary>
        /// Tente le merge. Retourne <c>null</c> si non-applicable, sinon un
        /// <see cref="MergeResult"/> avec MergedSidecar calculé.
        /// </summary>
        MergeResult TryMerge(int absStart, int absEnd, string currentSource);
    }
}
