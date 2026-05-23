using System.Collections.Generic;
using System.Text;
using MathCursor.Engine.Tokenization;
using MathCursor.Engine.Vocabulary;

namespace MathCursor.Engine.Collision
{
    /// <summary>
    /// Bag immuable passé à chaque <see cref="ICollisionDetector"/>. Fournit
    /// les tokens / LaTeX / vocab + un helper de reconstruction pour générer
    /// le finalLatex modifié avec un seul operand remplacé.
    ///
    /// <para>Cf. brief v5 §6 (collisions déclarées) + ADR P28.</para>
    /// </summary>
    public sealed class CollisionContext
    {
        public IReadOnlyList<IReadOnlyList<Token>> OperandTokens { get; }
        public IReadOnlyList<string> OperandLatex { get; }
        public IReadOnlyList<Token> OpTokens { get; }
        public string FinalLatex { get; }
        public LocaleVocabulary Vocab { get; }
        public CollisionScores Scores { get; }

        public CollisionContext(
            IReadOnlyList<IReadOnlyList<Token>> operandTokens,
            IReadOnlyList<string> operandLatex,
            IReadOnlyList<Token> opTokens,
            string finalLatex,
            LocaleVocabulary vocab,
            CollisionScores scores)
        {
            OperandTokens = operandTokens;
            OperandLatex = operandLatex;
            OpTokens = opTokens;
            FinalLatex = finalLatex;
            Vocab = vocab;
            Scores = scores;
        }

        /// <summary>
        /// Reconstruit le finalLatex en remplaçant l'operand à
        /// <paramref name="replaceIdx"/> par <paramref name="newOperandLatex"/>.
        /// Les ops et les autres operands sont préservés tels quels.
        /// </summary>
        public string ReplaceOperand(int replaceIdx, string newOperandLatex)
        {
            var sb = new StringBuilder();
            for (int j = 0; j < OperandLatex.Count; j++)
            {
                if (j > 0) AppendOpLatex(sb, OpTokens[j - 1]);
                sb.Append(j == replaceIdx ? newOperandLatex : OperandLatex[j]);
            }
            return sb.ToString();
        }

        private void AppendOpLatex(StringBuilder sb, Token op)
        {
            if (Vocab.Relations.TryGetValue(op.Text, out var r))
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
