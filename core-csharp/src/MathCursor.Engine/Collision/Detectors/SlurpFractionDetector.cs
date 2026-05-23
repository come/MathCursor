using System.Collections.Generic;
using System.Text;
using MathCursor.Engine.Ast;
using MathCursor.Engine.Emit;
using MathCursor.Engine.Parsing;
using MathCursor.Engine.Parsing.List;
using MathCursor.Engine.Tokenization;

namespace MathCursor.Engine.Collision.Detectors
{
    /// <summary>
    /// Détecte le pattern <c>a/b + c</c> (= fraction au top-level d'un
    /// operand, suivie d'un <c>+</c> ou <c>-</c> qui mène à un operand
    /// suivant). Propose l'alt slurp où la fraction absorbe son côté
    /// droit : <c>\frac{a}{b+c}</c>.
    ///
    /// <para>Brief v5 §2.4 : démoté par convention (= la default reste
    /// (a/b)+c, le slurp est en alt).</para>
    /// </summary>
    public sealed class SlurpFractionDetector : ICollisionDetector
    {
        private readonly StackParser _parser;
        private readonly LatexEmitter _emitter;

        public SlurpFractionDetector(StackParser parser, LatexEmitter emitter)
        {
            _parser = parser;
            _emitter = emitter;
        }

        public IEnumerable<EngineCandidate> Detect(CollisionContext ctx)
        {
            if (ctx.OperandTokens.Count < 2 || ctx.OpTokens.Count < 1) yield break;

            for (int i = 0; i < ctx.OperandTokens.Count - 1 && i < ctx.OpTokens.Count; i++)
            {
                var op = ctx.OpTokens[i];
                if (op.Text != "+" && op.Text != "-") continue;
                var fracTokens = ctx.OperandTokens[i];
                if (fracTokens.Count == 0) continue;
                int slashIdx = FindTopLevelSlash(fracTokens);
                if (slashIdx < 0) continue;

                var numTokens = new List<Token>();
                for (int k = 0; k < slashIdx; k++) numTokens.Add(fracTokens[k]);
                var denTokens = new List<Token>();
                for (int k = slashIdx + 1; k < fracTokens.Count; k++) denTokens.Add(fracTokens[k]);
                if (numTokens.Count == 0 || denTokens.Count == 0) continue;

                var combinedDen = new List<Token>(denTokens);
                combinedDen.Add(op);
                combinedDen.AddRange(ctx.OperandTokens[i + 1]);
                string numLatex = RenderTokens(numTokens);
                string denLatex = RenderTokens(combinedDen);
                string slurpOperand = "\\frac{" + numLatex + "}{" + denLatex + "}";

                // Reconstruit le finalLatex : remplace operand[i] par slurp
                // et absorbe operand[i+1] + opTokens[i].
                var sb = new StringBuilder();
                for (int j = 0; j < i; j++)
                {
                    if (j > 0) AppendOpLatex(sb, ctx.OpTokens[j - 1], ctx);
                    sb.Append(ctx.OperandLatex[j]);
                }
                if (i > 0) AppendOpLatex(sb, ctx.OpTokens[i - 1], ctx);
                sb.Append(slurpOperand);
                for (int j = i + 2; j < ctx.OperandLatex.Count; j++)
                {
                    AppendOpLatex(sb, ctx.OpTokens[j - 1], ctx);
                    sb.Append(ctx.OperandLatex[j]);
                }
                yield return new EngineCandidate(
                    latex: sb.ToString(),
                    description: "fraction (slurp dénominateur)",
                    ruleId: "fraction-slurp",
                    score: ctx.Scores.ScoreFor("fraction-slurp"));
            }
        }

        private string RenderTokens(IReadOnlyList<Token> tokens)
        {
            if (tokens == null || tokens.Count == 0) return string.Empty;
            var ast = _parser.Parse(tokens);
            ast = ListCombinator.Promote(ast);
            return _emitter.Emit(ast);
        }

        private static int FindTopLevelSlash(IReadOnlyList<Token> tokens)
        {
            int depth = 0;
            for (int i = 0; i < tokens.Count; i++)
            {
                var t = tokens[i];
                if (t.Kind == TokenKind.OpenDelim) depth++;
                else if (t.Kind == TokenKind.CloseDelim) depth--;
                else if (depth == 0 && t.Kind == TokenKind.Symbol && t.Text == "/")
                    return i;
            }
            return -1;
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
