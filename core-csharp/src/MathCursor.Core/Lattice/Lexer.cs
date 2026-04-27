using System.Collections.Generic;

namespace MathCursor.Core.Lattice
{
    /// <summary>
    /// Lexer en lattice : pour un span d'entrée, génère TOUTES les arêtes de
    /// tokens candidats (DAG dont les nœuds sont les positions du texte). Le
    /// top-K Dijkstra (cf. <see cref="LatticePathFinder"/>) sélectionne ensuite
    /// les chemins de poids minimum à travers ce DAG.
    ///
    /// Pondérations (cf. algorithm.md §1) :
    /// <list type="bullet">
    /// <item>Mot-clé / Nombre / Op multi-char : 0</item>
    /// <item>Fonction / Lettre grecque : 1</item>
    /// <item>Identifiant 1 lettre : 5</item>
    /// <item>Identifiant n lettres : 18 + 3·n</item>
    /// </list>
    ///
    /// Pour les lettres : on calcule la plus longue séquence alphabétique à
    /// partir de la position courante, puis on émet une arête pour CHAQUE
    /// sous-longueur. Ça permet à Dijkstra de choisir entre découper "pi" en
    /// "pi" (=π, w=1) ou "p·i" (=p·i, w=10).
    /// </summary>
    public static class Lexer
    {
        public static List<LatticeEdge> Lex(string input)
        {
            var edges = new List<LatticeEdge>();
            if (string.IsNullOrEmpty(input)) return edges;
            int n = input.Length;

            for (int i = 0; i < n; i++)
            {
                char c = input[i];

                // Espace / tab : token séparateur
                if (c == ' ' || c == '\t')
                {
                    edges.Add(new LatticeEdge(i, i + 1, EdgeType.Space, " ", 0));
                    continue;
                }

                // Op multi-caractères (greedy : on émet TOUTES les variantes
                // qui matchent ; Dijkstra choisira la plus courte = plus lourde
                // = celle qu'on veut éviter — il faudra checker en pratique)
                foreach (var kv in Vocabulary.MultiCharOps)
                {
                    var op = kv.Key;
                    if (i + op.Length <= n && input.Substring(i, op.Length) == op)
                    {
                        edges.Add(new LatticeEdge(i, i + op.Length, EdgeType.Op, op, 0));
                    }
                }

                // Op mono-caractère
                if (Vocabulary.SingleOps.IndexOf(c) >= 0)
                {
                    bool? tight = null;
                    if (Vocabulary.TightOpChars.IndexOf(c) >= 0)
                    {
                        tight = IsTightOp(input, i, 1);
                    }
                    edges.Add(new LatticeEdge(i, i + 1, EdgeType.Op, c.ToString(), 0, tight));
                }

                // Nombre (digits + point décimal éventuel)
                if (c >= '0' && c <= '9')
                {
                    int j = i;
                    while (j < n && (input[j] >= '0' && input[j] <= '9' || input[j] == '.'))
                        j++;
                    edges.Add(new LatticeEdge(i, j, EdgeType.Number, input.Substring(i, j - i), 0));
                }

                // Lettres : plus longue séquence alphabétique → émet une arête par sous-longueur.
                // On émet TOUJOURS l'arête Ident, ET en plus l'arête spéciale (Keyword/Function/Greek)
                // si le mot la matche. Ça donne le choix au Dijkstra : "cos" peut être interprété
                // comme fonction (w=1) ou comme c·o·s (3 idents w=15) ou comme cos ident (w=27).
                if (IsAlphabetic(c))
                {
                    int maxJ = i;
                    while (maxJ < n && IsAlphabetic(input[maxJ])) maxJ++;
                    for (int len = 1; len <= maxJ - i; len++)
                    {
                        var sub = input.Substring(i, len);
                        var lower = sub.ToLowerInvariant();

                        if (Vocabulary.Keywords.ContainsKey(lower))
                            edges.Add(new LatticeEdge(i, i + len, EdgeType.Keyword, lower, 0));
                        if (Vocabulary.Functions.Contains(lower))
                            edges.Add(new LatticeEdge(i, i + len, EdgeType.Function, lower, 1));
                        if (Vocabulary.Greek.Contains(lower))
                            edges.Add(new LatticeEdge(i, i + len, EdgeType.Greek, lower, 1));

                        if (len == 1)
                            edges.Add(new LatticeEdge(i, i + len, EdgeType.Ident, sub, 5));
                        else
                            edges.Add(new LatticeEdge(i, i + len, EdgeType.Ident, sub, 18 + len * 3));
                    }
                }
            }
            return edges;
        }

        // True si pas d'espace (ou bord) ni avant ni après l'op.
        private static bool IsTightOp(string input, int pos, int len)
        {
            char prev = pos > 0 ? input[pos - 1] : ' ';
            char next = pos + len < input.Length ? input[pos + len] : ' ';
            return !char.IsWhiteSpace(prev) && !char.IsWhiteSpace(next);
        }

        private static bool IsAlphabetic(char c)
        {
            // Lettres ASCII + accents français courants
            if (c >= 'a' && c <= 'z') return true;
            if (c >= 'A' && c <= 'Z') return true;
            return c == 'é' || c == 'è' || c == 'à' || c == 'ù' || c == 'â'
                || c == 'ê' || c == 'î' || c == 'ô' || c == 'û' || c == 'ç';
        }
    }
}
