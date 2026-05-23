using System.Collections.Generic;
using MathCursor.Engine.Emit;
using MathCursor.Engine.Parsing;
using MathCursor.Engine.Parsing.List;
using MathCursor.Engine.Tokenization;

namespace MathCursor.Engine.Collision.Detectors
{
    /// <summary>
    /// P31 (2026-05-22) : refactor pour scanner les operandTokens au lieu
    /// du LaTeX rendu (= conformité brief v5 §5 « ne scanner JAMAIS le
    /// LaTeX rendu »).
    ///
    /// <para>Détecte chaque operand qui contient une juxtaposition
    /// <c>&lt;letter&gt;&lt;number&gt;</c> collée (= `x2`, `e3`). Pour
    /// chaque match, génère l'alt indice via re-render de l'operand avec
    /// <see cref="LatexEmitter"/> en mode subscript.</para>
    /// </summary>
    public sealed class LetterSupSubDetector : ICollisionDetector
    {
        private readonly StackParser _parser;
        private readonly LatexEmitter _subEmitter;

        public LetterSupSubDetector(StackParser parser)
        {
            _parser = parser;
            _subEmitter = new LatexEmitter(preferSubscript: true);
        }

        public IEnumerable<EngineCandidate> Detect(CollisionContext ctx)
        {
            for (int i = 0; i < ctx.OperandTokens.Count; i++)
            {
                var bucket = ctx.OperandTokens[i];
                if (!ContainsLetterDigitJuxtaposition(bucket)) continue;
                var ast = _parser.Parse(bucket);
                ast = ListCombinator.Promote(ast);
                string subLatex = _subEmitter.Emit(ast);
                string altFinal = ctx.ReplaceOperand(i, subLatex);
                yield return new EngineCandidate(
                    latex: altFinal,
                    description: "indice (au lieu d'exposant)",
                    ruleId: "letter-sub-number",
                    score: ctx.Scores.ScoreFor("letter-sub-number"));
            }
        }

        /// <summary>
        /// Cherche dans <paramref name="bucket"/> un pattern : Word d'1
        /// lettre suivi d'un Number, sans gap (= collés).
        /// </summary>
        private static bool ContainsLetterDigitJuxtaposition(IReadOnlyList<Token> bucket)
        {
            for (int i = 0; i < bucket.Count - 1; i++)
            {
                var a = bucket[i];
                var b = bucket[i + 1];
                if (a.Kind != TokenKind.Word) continue;
                if (a.Text.Length != 1) continue;
                if (!char.IsLetter(a.Text[0])) continue;
                if (b.Kind != TokenKind.Number) continue;
                if (a.End != b.Start) continue; // collés
                return true;
            }
            return false;
        }
    }
}
