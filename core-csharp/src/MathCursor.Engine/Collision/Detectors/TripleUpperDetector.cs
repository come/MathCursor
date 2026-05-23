using System.Collections.Generic;
using MathCursor.Engine.Tokenization;

namespace MathCursor.Engine.Collision.Detectors
{
    /// <summary>
    /// P30 (2026-05-22) : `ABC` (= 3 majuscules adjacentes) → alt
    /// <c>\triangle ABC</c> (= notation triangle). Sur expression isolée
    /// (= 1 seul operand pour éviter les faux positifs dans une équation).
    /// </summary>
    public sealed class TripleUpperDetector : ICollisionDetector
    {
        public IEnumerable<EngineCandidate> Detect(CollisionContext ctx)
        {
            if (ctx.OperandTokens.Count != 1) yield break;
            var bucket = ctx.OperandTokens[0];
            if (bucket.Count != 1) yield break;
            if (bucket[0].Kind != TokenKind.Word) yield break;
            var text = bucket[0].Text;
            if (text.Length != 3) yield break;
            if (!char.IsUpper(text[0]) || !char.IsUpper(text[1]) || !char.IsUpper(text[2]))
                yield break;

            yield return new EngineCandidate(
                latex: "\\triangle " + text,
                description: "triangle",
                ruleId: "triangle",
                score: ctx.Scores.ScoreFor("triangle"));
        }
    }
}
