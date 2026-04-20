using System.Collections.Generic;

namespace MathCursor.Detection.Sp
{
    /// <summary>
    /// Trie de pièces SentencePiece pour lookup rapide pendant le Viterbi.
    /// Pour chaque position dans le texte, on parcourt le trie et on émet
    /// chaque pièce trouvée avec son score et son ID.
    /// </summary>
    public sealed class PieceTrie
    {
        private sealed class Node
        {
            public Dictionary<char, Node> Children;
            public bool IsTerminal;
            public int PieceId;
            public float Score;
        }

        private readonly Node _root = new Node();

        public void Insert(string piece, int id, float score)
        {
            var node = _root;
            foreach (var ch in piece)
            {
                if (node.Children == null) node.Children = new Dictionary<char, Node>();
                if (!node.Children.TryGetValue(ch, out var next))
                {
                    next = new Node();
                    node.Children[ch] = next;
                }
                node = next;
            }
            node.IsTerminal = true;
            node.PieceId = id;
            node.Score = score;
        }

        /// <summary>
        /// À partir de la position <paramref name="from"/> dans <paramref name="text"/>,
        /// énumère toutes les pièces du vocab qui matchent (avec leur longueur,
        /// leur ID et leur score). Renvoie la liste dans l'ordre croissant de longueur.
        /// </summary>
        public IEnumerable<(int Length, int PieceId, float Score)> MatchAt(string text, int from)
        {
            var node = _root;
            int len = 0;
            int max = text.Length - from;
            while (len < max)
            {
                if (node.Children == null) yield break;
                if (!node.Children.TryGetValue(text[from + len], out var next)) yield break;
                node = next;
                len++;
                if (node.IsTerminal)
                {
                    yield return (len, node.PieceId, node.Score);
                }
            }
        }
    }
}
