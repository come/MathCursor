namespace MathCursor.Engine.Tokenization
{
    /// <summary>
    /// Catégorie syntaxique d'un <see cref="Token"/>. Sert au dispatch
    /// passe-pile (cf. brief v4 §1.2).
    /// </summary>
    public enum TokenKind
    {
        /// <summary>Identifier (variable, ancre textuelle, mot quelconque).</summary>
        Word,

        /// <summary>Nombre entier ou décimal (= virgule décimale FR résolue).</summary>
        Number,

        /// <summary>Délimiteur ouvrant : <c>(</c>, <c>[</c>, <c>{</c>.</summary>
        OpenDelim,

        /// <summary>Délimiteur fermant : <c>)</c>, <c>]</c>, <c>}</c>.</summary>
        CloseDelim,

        /// <summary>Symbole opérateur (+, -, *, /, ^, _, =, &lt;, &gt;, …).</summary>
        Symbol,

        /// <summary>Séparateur de liste/colonne (= colsep ou rowsep selon locale).</summary>
        Sep,

        /// <summary>Token "glue" intra-segment (= <c>-&gt;</c>, <c>=</c>, …).</summary>
        Glue,
    }
}
