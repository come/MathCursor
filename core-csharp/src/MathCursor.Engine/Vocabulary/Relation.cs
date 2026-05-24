namespace MathCursor.Engine.Vocabulary
{
    /// <summary>
    /// Entrée du vocabulaire pour un opérateur relationnel ou infixe.
    /// Source = YAML <c>relations:</c> (cf. brief v4 §3).
    /// </summary>
    public sealed class Relation
    {
        public string Token { get; }
        public string Tex { get; }
        public PrecedenceTier Tier { get; }
        public string? Tail { get; }

        /// <summary>
        /// P29 (2026-05-22) : si <c>true</c>, l'opérateur est rendu comme
        /// un wrapper du prochain opérande au lieu d'un infixe binaire :
        /// <c>&lt;Tex&gt;{&lt;next operand&gt;}</c>. Ex. <c>mod</c> →
        /// <c>\pmod{7}</c>. Évite un <c>if</c> spécial dans l'engine.
        /// </summary>
        public bool Wrap { get; }

        /// <summary>
        /// Contexte d'activation (= condition pour reclasser un Word en
        /// Symbol au tokenizer). Default <see cref="RelationContext.None"/>
        /// = toujours actif. <see cref="RelationContext.IsolatedBetweenBrackets"/>
        /// = uniquement quand le token est isolé entre 2 délimiteurs bracket
        /// (cas <c>[0,1[ u [0,1]</c> où <c>u</c> = <c>\cup</c>).
        /// </summary>
        public RelationContext Context { get; }

        public Relation(string token, string tex, PrecedenceTier tier,
            string? tail = null, bool wrap = false,
            RelationContext context = RelationContext.None)
        {
            Token = token ?? throw new System.ArgumentNullException(nameof(token));
            Tex = tex ?? throw new System.ArgumentNullException(nameof(tex));
            Tier = tier;
            Tail = tail;
            Wrap = wrap;
            Context = context;
        }
    }
}
