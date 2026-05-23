using System.Collections.Generic;
using System.Text;
using MathCursor.Engine.Tokenization;

namespace MathCursor.Engine.Collision.Detectors
{
    /// <summary>
    /// P31 (2026-05-22) : détecte le pattern <c>&lt;vec-candidate&gt;(a,b)</c>
    /// (= application/coordonnées) et propose l'alt vecteur en colonne
    /// <c>\vec{u}\begin{pmatrix}a\\b\end{pmatrix}</c>. Brief v5 §VectorCoords.
    ///
    /// <para>Stratégie : scanne chaque operand pour Word collé à un groupe
    /// délimité contenant des séparateurs (`,` ou `;`). Le top reste le
    /// rendu function-call par défaut ; l'alt est démotée.</para>
    /// </summary>
    public sealed class VectorCoordsDetector : ICollisionDetector
    {
        public IEnumerable<EngineCandidate> Detect(CollisionContext ctx)
        {
            for (int i = 0; i < ctx.OperandTokens.Count; i++)
            {
                var bucket = ctx.OperandTokens[i];
                if (bucket.Count < 4) continue; // minimum: <w>(a,b) = 5 tokens
                if (bucket[0].Kind != TokenKind.Word) continue;
                if (!VecCandidate.IsVecCandidate(bucket[0].Text)) continue;
                if (bucket[1].Kind != TokenKind.OpenDelim) continue;
                if (bucket[1].Text != "(") continue;
                if (bucket[0].End != bucket[1].Start) continue; // collés

                // Trouve la close-paren matching.
                int closeIdx = FindMatchingClose(bucket, 1);
                if (closeIdx < 0) continue;
                if (closeIdx != bucket.Count - 1) continue; // groupe doit clore l'operand

                // Extrait les items entre les parens, séparés par ',' ou ';'.
                var items = ExtractItems(bucket, 2, closeIdx);
                if (items.Count < 2) continue; // au moins 2 coordonnées

                // Génère l'alt vec coords.
                var sb = new StringBuilder();
                sb.Append("\\vec{").Append(bucket[0].Text).Append("}");
                sb.Append("\\begin{pmatrix}");
                for (int k = 0; k < items.Count; k++)
                {
                    if (k > 0) sb.Append(" \\\\ ");
                    sb.Append(items[k]);
                }
                sb.Append("\\end{pmatrix}");
                string altFinal = ctx.ReplaceOperand(i, sb.ToString());
                yield return new EngineCandidate(
                    latex: altFinal,
                    description: "vecteur (coordonnées)",
                    ruleId: "vec-coords",
                    score: ctx.Scores.ScoreFor("vec-coords"));
            }
        }

        private static int FindMatchingClose(IReadOnlyList<Token> tokens, int openIdx)
        {
            int depth = 1;
            for (int i = openIdx + 1; i < tokens.Count; i++)
            {
                if (tokens[i].Kind == TokenKind.OpenDelim) depth++;
                else if (tokens[i].Kind == TokenKind.CloseDelim)
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        private static List<string> ExtractItems(IReadOnlyList<Token> tokens, int from, int to)
        {
            // Sépare les tokens entre `from` et `to` par `,` ou `;` au top-level.
            var items = new List<string>();
            var current = new StringBuilder();
            int depth = 0;
            for (int i = from; i < to; i++)
            {
                var t = tokens[i];
                if (depth == 0 && t.Kind == TokenKind.Sep
                    && (t.Text == "," || t.Text == ";"))
                {
                    items.Add(current.ToString().Trim());
                    current.Clear();
                    continue;
                }
                if (t.Kind == TokenKind.OpenDelim) depth++;
                else if (t.Kind == TokenKind.CloseDelim) depth--;
                // Skip whitespace Sep tokens dans le rendu.
                if (t.Kind == TokenKind.Sep && t.Text == " ") continue;
                current.Append(t.Text);
            }
            if (current.Length > 0) items.Add(current.ToString().Trim());
            return items;
        }
    }
}
