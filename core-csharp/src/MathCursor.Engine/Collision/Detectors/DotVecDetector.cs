using System.Collections.Generic;
using MathCursor.Engine.Tokenization;

namespace MathCursor.Engine.Collision.Detectors
{
    /// <summary>
    /// Détecte le pattern <c>&lt;word&gt;.&lt;word&gt;</c> où les deux côtés
    /// sont vec-candidates, et propose l'alt produit scalaire vecteurs
    /// <c>\vec{a} \cdot \vec{b}</c>.
    /// </summary>
    public sealed class DotVecDetector : ICollisionDetector
    {
        public IEnumerable<EngineCandidate> Detect(CollisionContext ctx)
        {
            for (int i = 0; i < ctx.OperandTokens.Count; i++)
            {
                var b = ctx.OperandTokens[i];
                if (b.Count != 3) continue;
                if (b[0].Kind != TokenKind.Word) continue;
                if (b[1].Kind != TokenKind.Symbol || b[1].Text != ".") continue;
                if (b[2].Kind != TokenKind.Word) continue;
                if (!VecCandidate.IsVecCandidate(b[0].Text)) continue;
                if (!VecCandidate.IsVecCandidate(b[2].Text)) continue;

                string dotVec = "\\vec{" + b[0].Text + "} \\cdot \\vec{" + b[2].Text + "}";
                string alt = ctx.ReplaceOperand(i, dotVec);
                yield return new EngineCandidate(
                    latex: alt, description: "produit scalaire vecteurs",
                    ruleId: "dot-vec", score: ctx.Scores.ScoreFor("dot-vec"));
            }
        }
    }
}
