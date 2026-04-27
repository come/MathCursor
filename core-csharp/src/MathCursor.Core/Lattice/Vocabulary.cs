using System.Collections.Generic;

namespace MathCursor.Core.Lattice
{
    /// <summary>
    /// Vocabulaire reconnu par le lexer : mots-clés (déclenchent un scope),
    /// fonctions nommées, lettres grecques, opérateurs multi-caractères.
    ///
    /// Volontairement en C# (pas YAML) pour la phase 1 : le brief autorise
    /// l'extraction en YAML plus tard si besoin (cf. règle non-négo). Pour
    /// l'instant on ouvre/ferme une PR si on veut ajouter un mot.
    /// </summary>
    public static class Vocabulary
    {
        /// <summary>Mots-clés qui déclenchent un scope structuré (ex : lim x 0 f(x)).</summary>
        public static readonly Dictionary<string, string> Keywords = new Dictionary<string, string>
        {
            { "somme",     "sum" },
            { "sum",       "sum" },
            { "prod",      "prod" },
            { "produit",   "prod" },
            { "lim",       "lim" },
            { "limite",    "lim" },
            { "int",       "int" },
            { "integrale", "int" },
            { "intégrale", "int" },
            { "racine",    "sqrt" },
            { "sqrt",      "sqrt" },
            { "rac",       "sqrt" },
            { "frac",      "frac" },
            { "vec",       "vec" },
            { "vecteur",   "vec" },
            { "inf",       "infinity" },
            { "infini",    "infinity" },
            { "forall",    "forall" },
            { "exists",    "exists" },
            { "in",        "in" },
            { "appartient", "in" },
        };

        /// <summary>Fonctions nommées qui prennent un argument (sin x, cos(2x)).</summary>
        public static readonly HashSet<string> Functions = new HashSet<string>
        {
            "sin", "cos", "tan", "cot", "sec", "csc",
            "sinh", "cosh", "tanh",
            "arcsin", "arccos", "arctan",
            "ln", "log", "exp",
            "min", "max", "det",
        };

        /// <summary>Noms ASCII des lettres grecques (rendus en \alpha, \beta, …).</summary>
        public static readonly HashSet<string> Greek = new HashSet<string>
        {
            "alpha", "beta", "gamma", "delta", "epsilon", "zeta", "eta", "theta",
            "iota", "kappa", "lambda", "mu", "nu", "xi", "pi", "rho", "sigma", "tau",
            "phi", "chi", "psi", "omega",
        };

        /// <summary>Opérateurs multi-caractères (≥ 2 chars) à matcher en greedy.</summary>
        public static readonly Dictionary<string, string> MultiCharOps = new Dictionary<string, string>
        {
            { "<=", "leq" },
            { ">=", "geq" },
            { "!=", "neq" },
            { "<>", "neq" },
            { "->", "to" },
            { "=>", "Rightarrow" },
        };

        /// <summary>Opérateurs mono-caractère reconnus. Inclut les relations
        /// `=`, `&lt;`, `&gt;` qui sont consommées par <c>parseRelation</c> au
        /// top-level (sinon des inputs comme "a &lt; b" produisent un résultat
        /// vide car `&lt;` n'est pas tokenisé).</summary>
        public const string SingleOps = "+-*/^_=<>()[]{},|;";

        /// <summary>Opérateurs binaires pour lesquels on calcule le drapeau Tight.</summary>
        public const string TightOpChars = "+-*/^";
    }
}
