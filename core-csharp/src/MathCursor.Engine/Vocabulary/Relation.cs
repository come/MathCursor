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

        public Relation(string token, string tex, PrecedenceTier tier,
            string? tail = null, bool wrap = false)
        {
            Token = token ?? throw new System.ArgumentNullException(nameof(token));
            Tex = tex ?? throw new System.ArgumentNullException(nameof(tex));
            Tier = tier;
            Tail = tail;
            Wrap = wrap;
        }
    }
}
