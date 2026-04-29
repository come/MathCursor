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
            { "dans",      "in" },
            { "perp",      "perp" },
            { "perpendiculaire", "perp" },
            { "union",     "union" },
            { "inter",     "inter" },
            { "intersection", "inter" },
            // Ensembles canoniques. On enregistre uniquement les versions
            // préfixées `bb*` parce que les lettres seules R/N/Z/Q/C doivent
            // rester atom par défaut (pour préserver `pi*R²`, `2N+1`, etc.).
            // L'AlternativeGenerator scan R/N/Z/Q/C isolées et propose une
            // mutation `R` → `bbR` si l'utilisateur veut l'ensemble.
            // Clés en minuscules : le lexer fait ToLowerInvariant sur le mot
            // avant lookup. La mutation `bbR` (tapée caps par convention) est
            // donc lookupée comme `bbr` dans ce dict.
            { "bbr",       "bbR" },
            { "bbn",       "bbN" },
            { "bbz",       "bbZ" },
            { "bbq",       "bbQ" },
            { "bbc",       "bbC" },
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

        /// <summary>Opérateurs multi-caractères (≥ 2 chars) à matcher en greedy.
        /// Le lexer émet ces ops avec coût négatif (-length) pour garantir
        /// que le Dijkstra préfère la variante la plus longue (ex: `<=>` plutôt
        /// que `<=` + `>`).</summary>
        public static readonly Dictionary<string, string> MultiCharOps = new Dictionary<string, string>
        {
            // Implication / équivalence (ADR 29-04). À déclarer AVANT `<=` pour
            // que les variantes longues gagnent visuellement le tri à coût égal,
            // bien que le coût négatif rend l'ordre non-critique.
            { "<==>", "Leftrightarrow" },
            { "<=>",  "Leftrightarrow" },
            { "==>",  "Rightarrow" },
            { "<==",  "Leftarrow" },
            { "<=", "leq" },
            { ">=", "geq" },
            { "!=", "neq" },
            { "<>", "neq" },
            { "->", "to" },
            { "=>", "Rightarrow" },
            // Unicode arrows (copier-coller depuis sources externes OU
            // Word AutoCorrect qui remplace `=>`/`<=>` par ces variantes
            // selon la version d'Office et la langue).
            { "⇒", "Rightarrow" },
            { "⇔", "Leftrightarrow" },
            { "⇐", "Leftarrow" },
            { "↔", "Leftrightarrow" }, // U+2194 flèche simple bidirectionnelle (Word AutoCorrect FR pour <=>)
            { "⟺", "Leftrightarrow" }, // U+27FA flèche longue
            { "⟹", "Rightarrow" },     // U+27F9 flèche longue à droite
            { "⟸", "Leftarrow" },      // U+27F8 flèche longue à gauche
            // Notation clavier `(-` pour `\in` (alias de `dans`/`in`/`appartient`).
            // Visuellement le `(` ouvert + `-` rappelle ∈.
            { "(-", "in_op" },
            // // = parallèle (∥) entre deux droites/vecteurs. Évite que la
            // saisie clavier fluide "AB//CD" soit interprétée comme une
            // fraction imbriquée AB/(CD) ou similaire.
            { "//", "parallel" },
        };

        /// <summary>Opérateurs mono-caractère reconnus. Inclut les relations
        /// `=`, `&lt;`, `&gt;` qui sont consommées par <c>parseRelation</c> au
        /// top-level (sinon des inputs comme "a &lt; b" produisent un résultat
        /// vide car `&lt;` n'est pas tokenisé).</summary>
        public const string SingleOps = "+-*/^_=<>()[]{},|;:";

        /// <summary>Opérateurs binaires pour lesquels on calcule le drapeau Tight.</summary>
        public const string TightOpChars = "+-*/^";
    }
}
