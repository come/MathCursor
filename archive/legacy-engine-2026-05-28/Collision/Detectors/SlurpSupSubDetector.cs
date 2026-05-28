using System.Collections.Generic;
using System.Text;
using MathCursor.Engine.Emit;
using MathCursor.Engine.Parsing;
using MathCursor.Engine.Parsing.List;
using MathCursor.Engine.Tokenization;

namespace MathCursor.Engine.Collision.Detectors
{
    /// <summary>
    /// P30 (2026-05-22) : slurp exposant/indice. Pattern <c>x^a + b</c> ou
    /// <c>u_n + 1</c> au top-level d'un operand, suivi d'un <c>+</c> ou
    /// <c>-</c>. Propose l'alt où l'opérateur sup/sub absorbe son côté droit :
    /// <c>x^{a+b}</c>, <c>u_{n+1}</c>.
    ///
    /// <para>Brief v5 §2.4. Démoté comme le slurp fraction (= défaut reste
    /// la version left-assoc).</para>
    /// </summary>
    public sealed class SlurpSupSubDetector : ICollisionDetector
    {
        private readonly StackParser _parser;
        private readonly LatexEmitter _emitter;

        public SlurpSupSubDetector(StackParser parser, LatexEmitter emitter)
        {
            _parser = parser;
            _emitter = emitter;
        }

        public IEnumerable<EngineCandidate> Detect(CollisionContext ctx)
        {
            if (ctx.OperandTokens.Count < 2 || ctx.OpTokens.Count < 1) yield break;

            for (int i = 0; i < ctx.OperandTokens.Count - 1 && i < ctx.OpTokens.Count; i++)
            {
                var nextOp = ctx.OpTokens[i];
                if (nextOp.Text != "+" && nextOp.Text != "-") continue;

                var bucket = ctx.OperandTokens[i];
                if (bucket.Count < 3) continue;
                // Pattern : <atom> <^|_> <expr>
                if (bucket[0].Kind != TokenKind.Word && bucket[0].Kind != TokenKind.Number)
                    continue;
                if (bucket[1].Kind != TokenKind.Symbol) continue;
                string supSubOp = bucket[1].Text;
                if (supSubOp != "^" && supSubOp != "_") continue;

                // base + supSubOp + expanded.
                var baseToken = bucket[0];
                var expTokens = new List<Token>();
                for (int k = 2; k < bucket.Count; k++) expTokens.Add(bucket[k]);
                // expansion = exp + nextOp + operand[i+1].
                var combined = new List<Token>(expTokens);
                combined.Add(nextOp);
                combined.AddRange(ctx.OperandTokens[i + 1]);
                string expandedLatex = RenderTokens(combined);
                string newOperand = baseToken.Text + supSubOp + "{" + expandedLatex + "}";

                // Reconstruit final en absorbant operand[i+1] + op[i].
                var sb = new StringBuilder();
                for (int j = 0; j < i; j++)
                {
                    if (j > 0) AppendOpLatex(sb, ctx.OpTokens[j - 1], ctx);
                    sb.Append(ctx.OperandLatex[j]);
                }
                if (i > 0) AppendOpLatex(sb, ctx.OpTokens[i - 1], ctx);
                sb.Append(newOperand);
                for (int j = i + 2; j < ctx.OperandLatex.Count; j++)
                {
                    AppendOpLatex(sb, ctx.OpTokens[j - 1], ctx);
                    sb.Append(ctx.OperandLatex[j]);
                }
                string desc = supSubOp == "^" ? "exposant (slurp)" : "indice (slurp)";
                string ruleId = supSubOp == "^" ? "supsub-slurp-sup" : "supsub-slurp-sub";
                yield return new EngineCandidate(
                    latex: sb.ToString(),
                    description: desc,
                    ruleId: ruleId,
                    score: ctx.Scores.ScoreFor(ruleId));
            }
        }

        private string RenderTokens(IReadOnlyList<Token> tokens)
        {
            if (tokens == null || tokens.Count == 0) return string.Empty;
            var ast = _parser.Parse(tokens);
            ast = ListCombinator.Promote(ast);
            return _emitter.Emit(ast);
        }

        private static void AppendOpLatex(StringBuilder sb, Token op, CollisionContext ctx)
        {
            if (ctx.Vocab.Relations.TryGetValue(op.Text, out var r))
            {
                if (op.Text == "+" || op.Text == "-") sb.Append(r.Tex);
                else sb.Append(' ').Append(r.Tex).Append(' ');
            }
            else
            {
                sb.Append(' ').Append(op.Text).Append(' ');
            }
        }
    }
}
