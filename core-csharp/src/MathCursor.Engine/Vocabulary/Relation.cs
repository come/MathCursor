namespace MathCursor.Engine.Vocabulary
{
    /// <summary>
    /// Entrée du vocabulaire pour un opérateur relationnel/infixe. Source =
    /// YAML <c>relations:</c>.
    ///
    /// <para>Moteur V2 (2026-05-29) : réduit à ce que le tokenizer utilise
    /// réellement. <see cref="Token"/> sert de clé (= lecture multi-char
    /// <c>&lt;=&gt;</c>, <c>=&gt;</c>). <see cref="Tex"/> est le rendu LaTeX
    /// pour le reclassement Word→Symbol. <see cref="Context"/> conditionne
    /// le reclassement (= <c>u</c> → <c>\cup</c> entre brackets).</para>
    ///
    /// <para>La machinerie de précédence (tier/wrap/align/compact/leading)
    /// du moteur legacy a été supprimée — le moteur V2 compose par règles
    /// YAML, pas par climbing de tiers.</para>
    /// </summary>
    public sealed class Relation
    {
        public string Token { get; }
        public string Tex { get; }
        public RelationContext Context { get; }

        public Relation(string token, string tex,
            RelationContext context = RelationContext.None)
        {
            Token = token ?? throw new System.ArgumentNullException(nameof(token));
            Tex = tex ?? throw new System.ArgumentNullException(nameof(tex));
            Context = context;
        }
    }
}
