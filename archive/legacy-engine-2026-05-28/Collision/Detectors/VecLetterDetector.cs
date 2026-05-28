using System.Collections.Generic;
using MathCursor.Engine.Tokenization;

namespace MathCursor.Engine.Collision.Detectors
{
    /// <summary>
    /// Détecte un Word isolé vec-candidat (= `u`, `AB`) dans n'importe quel
    /// operand et propose l'alt <c>\vec{...}</c>.
    /// </summary>
    public sealed class VecLetterDetector : ICollisionDetector
    {
        public IEnumerable<EngineCandidate> Detect(CollisionContext ctx)
        {
            // P29 : proposer vec UNIQUEMENT si l'expression entière est
            // un Word isolé (= `u`, `AB`). Dans une équation `A=u.v`, on
            // ne propose pas `\vec{A}` qui pollue les choix réels (dot-vec).
            if (ctx.OperandTokens.Count != 1) yield break;
            var bucket = ctx.OperandTokens[0];
            if (bucket.Count != 1) yield break;
            if (bucket[0].Kind != TokenKind.Word) yield break;
            if (!VecCandidate.IsVecCandidate(bucket[0].Text)) yield break;

            string vecLatex = "\\vec{" + bucket[0].Text + "}";
            string alt = ctx.ReplaceOperand(0, vecLatex);
            yield return new EngineCandidate(
                latex: alt, description: "vecteur",
                ruleId: "vec", score: ctx.Scores.ScoreFor("vec"));
        }
    }
}
