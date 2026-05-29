namespace MathCursor.Engine.Rewriting
{
    /// <summary>
    /// Catégorie sémantique d'un <see cref="Item"/>. Un <c>TokenItem</c> a une
    /// catégorie dérivée de son <c>TokenKind</c> (= <c>Letter</c>, <c>Number</c>,
    /// <c>Symbol</c>, …). Un <c>RewriteItem</c> a la catégorie déclarée par la
    /// règle qui l'a produit (= <c>produces:</c>).
    ///
    /// <para>Les patterns YAML peuvent typer leurs slots par catégorie (=
    /// <c>{a:interval}</c> ne matche qu'un Item de catégorie <c>Interval</c>).
    /// C'est ce qui permet la composition bottom-up : une règle « union
    /// d'intervalles » consomme 2 Items <c>Interval</c> déjà reconnus.</para>
    ///
    /// <para>Migration Chantier 4 Phase A (2026-05-25) — POC rewriting-based.</para>
    /// </summary>
    public enum Category
    {
        /// <summary>Inconnu / non typé. Catégorie par défaut pour un TokenItem.</summary>
        Any,

        /// <summary>1 lettre seule (= variable scalaire).</summary>
        Letter,

        /// <summary>Suite de chiffres (= constante numérique).</summary>
        Number,

        /// <summary>Symbole / opérateur primitif (= <c>+</c>, <c>=</c>, …).</summary>
        Symbol,

        /// <summary>Délimiteur ouvrant ou fermant primitif (= <c>(</c>, <c>{</c>, …).</summary>
        Delim,

        /// <summary>Séparateur primitif (= espace, <c>\n</c>, <c>,</c>, …).</summary>
        Sep,

        /// <summary>Expression composite (= produit d'une règle générale).</summary>
        Expr,

        /// <summary>Variable nommée (= identifiant utilisateur).</summary>
        Var,

        /// <summary>Intervalle (= <c>[a,b]</c>, <c>]a,b]</c>, etc.).</summary>
        Interval,

        /// <summary>Ensemble (= <c>{x, y, z}</c>, <c>\mathbb{R}</c>, …).</summary>
        Set,

        /// <summary>Fonction (= <c>\sin</c>, <c>\cos</c>, <c>f</c>, …).</summary>
        Function,

        /// <summary>Vecteur (= <c>\vec{x}</c>, <c>\vec{AB}</c>, …).</summary>
        Vector,
    }
}
