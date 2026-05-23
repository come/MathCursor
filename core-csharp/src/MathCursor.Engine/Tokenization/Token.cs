namespace MathCursor.Engine.Tokenization
{
    /// <summary>
    /// Token produit par <see cref="Tokenizer"/>. Immutable.
    /// Conserve les positions <see cref="Start"/>/<see cref="End"/> dans la
    /// source pour les diagnostics + le bonus caret-aware ranker.
    /// </summary>
    public sealed class Token
    {
        public string Text { get; }
        public TokenKind Kind { get; }
        public int Start { get; }
        public int End { get; }

        public Token(string text, TokenKind kind, int start, int end)
        {
            Text = text ?? string.Empty;
            Kind = kind;
            Start = start;
            End = end;
        }

        public override string ToString() => $"{Kind}({Text})@[{Start}..{End}]";
    }
}
