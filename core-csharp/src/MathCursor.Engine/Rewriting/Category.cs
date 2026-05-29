namespace MathCursor.Engine.Rewriting
{
    /// <summary>
    /// Catégorie sémantique d'un <see cref="Item"/>. Le typage des slots
    /// repose dessus : un slot <c>{x:set}</c> n'accepte qu'un Item dont la
    /// catégorie est <see cref="Set"/> ou une sous-catégorie (= subsumption,
    /// cf. <see cref="Subsumes"/>).
    ///
    /// <para>Moteur V2 (2026-05-29). Cf. ADR 2026-05-28-rewriting-engine-v2.</para>
    /// </summary>
    public enum Category
    {
        /// <summary>Wildcard — accepte tout.</summary>
        Any,

        // ── Atomiques (= produits par le tokenizer) ──
        /// <summary>1 lettre seule (= variable scalaire : x, n, k).</summary>
        Letter,
        /// <summary>Constante numérique (= 1, 42).</summary>
        Number,
        /// <summary>Opérateur / glue primitif (= +, =, tend vers).</summary>
        Symbol,
        /// <summary>Délimiteur ouvrant/fermant (= ( ) [ ] { }).</summary>
        Delim,
        /// <summary>Séparateur (= espace, \n, ,).</summary>
        Sep,
        /// <summary>Identifiant multi-char non-fonction (= AB, frac brut).</summary>
        Var,

        // ── Sémantiques (= produits par les règles) ──
        /// <summary>Expression composite — catégorie « valeur » la plus large.</summary>
        Expr,
        /// <summary>Intervalle borné (= [0;1], ]0;1]).</summary>
        Interval,
        /// <summary>Ensemble (= \mathbb{R}, {1,2,3}).</summary>
        Set,
        /// <summary>Fonction (= \sin, \cos, f: x \mapsto …).</summary>
        Function,
        /// <summary>Vecteur (= \vec{u}).</summary>
        Vector,
        /// <summary>Matrice / structure 2D (= \begin{pmatrix}…).</summary>
        Matrix,
    }

    public static class Categories
    {
        /// <summary>
        /// True si un Item de catégorie <paramref name="actual"/> satisfait
        /// une demande de catégorie <paramref name="requested"/>.
        ///
        /// <para>Règles de subsumption :</para>
        /// <list type="bullet">
        ///   <item><see cref="Category.Any"/> accepte tout.</item>
        ///   <item>match exact accepté.</item>
        ///   <item><see cref="Category.Expr"/> accepte toute « valeur »
        ///     (= Letter, Number, Var, Interval, Set, Function, Vector,
        ///     Matrix, Expr).</item>
        ///   <item><see cref="Category.Set"/> accepte aussi
        ///     <see cref="Category.Interval"/> (= un intervalle est un
        ///     ensemble).</item>
        /// </list>
        /// </summary>
        public static bool Subsumes(Category requested, Category actual)
        {
            if (requested == Category.Any) return true;
            if (requested == actual) return true;

            if (requested == Category.Expr)
            {
                switch (actual)
                {
                    case Category.Letter:
                    case Category.Number:
                    case Category.Var:
                    case Category.Interval:
                    case Category.Set:
                    case Category.Function:
                    case Category.Vector:
                    case Category.Matrix:
                    case Category.Expr:
                        return true;
                    default:
                        return false;
                }
            }

            if (requested == Category.Set && actual == Category.Interval)
                return true;

            return false;
        }

        /// <summary>Parse une catégorie depuis le YAML (= <c>produces:</c> ou
        /// type de slot <c>{x:type}</c>). Throw si inconnue.</summary>
        public static Category Parse(string? value)
        {
            if (string.IsNullOrEmpty(value)) return Category.Expr;
            switch (value!.Trim().ToLowerInvariant())
            {
                case "any": return Category.Any;
                case "letter": return Category.Letter;
                case "number": return Category.Number;
                case "symbol": return Category.Symbol;
                case "delim": return Category.Delim;
                case "sep": return Category.Sep;
                case "var": return Category.Var;
                case "expr": return Category.Expr;
                case "interval": return Category.Interval;
                case "set": return Category.Set;
                case "function": return Category.Function;
                case "vector": return Category.Vector;
                case "matrix": return Category.Matrix;
                default:
                    throw new System.ArgumentException(
                        $"Catégorie inconnue : '{value}'. Attendu : any, letter, " +
                        "number, symbol, delim, sep, var, expr, interval, set, " +
                        "function, vector, matrix.");
            }
        }
    }
}
