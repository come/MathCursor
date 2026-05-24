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

        /// <summary>
        /// Si <c>true</c>, l'opérateur peut apparaître en préfixe unaire en
        /// début d'expression (ex. <c>+y2</c>, <c>=1/2</c>). Le parser
        /// l'encapsule dans un <c>UnaryPrefixNode</c>. Source YAML
        /// <c>allow_leading: true</c>. User-report 2026-05-23.
        /// </summary>
        public bool AllowLeading { get; }

        /// <summary>
        /// Si <c>true</c>, l'opérateur est rendu compact (= sans espace
        /// autour : <c>a+b</c>, pas <c>a + b</c>). Conv math FR pour
        /// <c>+</c>/<c>-</c> arithmétiques. Source YAML <c>compact: true</c>.
        /// </summary>
        public bool Compact { get; }

        /// <summary>
        /// Préfixe LaTeX utilisé en mode multi-line align quand l'opérateur
        /// apparaît en début de ligne 2+. <c>null</c> = pas un marker align
        /// (= ne peut pas démarrer une ligne en mode align*). Empty string
        /// <c>""</c> = chaîne d'égalités (= aligné via <c>&amp;</c>, pas de
        /// prefix visible). Sinon = la commande LaTeX (<c>\Leftrightarrow</c>,
        /// <c>\Rightarrow</c>, <c>\Leftarrow</c>). Source YAML
        /// <c>align_prefix: '\Leftrightarrow'</c>.
        /// </summary>
        public string? AlignPrefix { get; }

        public Relation(string token, string tex, PrecedenceTier tier,
            string? tail = null, bool wrap = false,
            RelationContext context = RelationContext.None,
            bool allowLeading = false,
            bool compact = false,
            string? alignPrefix = null)
        {
            Token = token ?? throw new System.ArgumentNullException(nameof(token));
            Tex = tex ?? throw new System.ArgumentNullException(nameof(tex));
            Tier = tier;
            Tail = tail;
            Wrap = wrap;
            Context = context;
            AllowLeading = allowLeading;
            Compact = compact;
            AlignPrefix = alignPrefix;
        }
    }
}
