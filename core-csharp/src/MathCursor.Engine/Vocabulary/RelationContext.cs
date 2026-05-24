namespace MathCursor.Engine.Vocabulary
{
    /// <summary>
    /// Contexte d'activation d'une <see cref="Relation"/>. Permet d'exprimer
    /// en YAML des règles qui ne s'appliquent que dans certains contextes
    /// (= éviter le conflit avec une lettre par exemple).
    ///
    /// <para>Source = YAML <c>relations: { context: 'isolated_between_brackets' }</c>.
    /// Vérifié au tokenizer post-process avant de reclasser un Word en Symbol.</para>
    /// </summary>
    public enum RelationContext
    {
        /// <summary>Pas de condition : la relation s'applique toujours.</summary>
        None,

        /// <summary>
        /// Token isolé (= Sep des 2 côtés) ET voisins non-Sep sont des
        /// délimiteurs bracket (`[`, `]`, `(`, `)`, `{`, `}`). Couvre
        /// <c>[0,1[ u [0,1]</c> où <c>u</c> = <c>\cup</c>.
        /// </summary>
        IsolatedBetweenBrackets,
    }
}
