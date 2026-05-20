namespace MathCursor.Host.Pipeline
{
    /// <summary>
    /// Une étape du <see cref="CommitPipeline"/> — applique une transformation
    /// pure sur un <see cref="CommitContext"/>. Cf. ADR
    /// <c>2026-05-06-Meta-l4-pipeline-and-session</c>.
    /// <para>
    /// Contrat :
    /// <list type="bullet">
    ///   <item>Pure : ne mute pas le ctx d'entrée (utiliser <c>With*</c>).</item>
    ///   <item>Idempotent vis-à-vis d'un ctx déjà transformé : si le stage
    ///     n'a rien à faire (ex. <c>MergerStage</c> sans voisin à absorber),
    ///     retourner le ctx tel quel.</item>
    ///   <item>Pas de side-effect global hors du ctx (sauf stages dont
    ///     c'est la nature : <c>InserterStage</c> écrit dans Word,
    ///     <c>StoreStage</c> écrit dans le CustomXMLPart).</item>
    /// </list>
    /// </para>
    /// </summary>
    internal interface ICommitStage
    {
        /// <summary>Nom du stage pour logs/diagnostics.</summary>
        string Name { get; }

        /// <summary>Applique la transformation. Retourne le ctx d'entrée
        /// inchangé si non applicable.</summary>
        CommitContext Apply(CommitContext ctx);
    }
}
