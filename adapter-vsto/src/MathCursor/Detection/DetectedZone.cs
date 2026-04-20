namespace MathCursor.Detection
{
    /// <summary>
    /// Une zone math détectée par le modèle NER avec sa confiance et ses
    /// positions caractères dans le texte original.
    /// </summary>
    public sealed class DetectedZone
    {
        public int Start { get; }
        public int End { get; }
        public string Text { get; }
        public double Confidence { get; }

        public DetectedZone(int start, int end, string text, double confidence)
        {
            Start = start;
            End = end;
            Text = text;
            Confidence = confidence;
        }

        public override string ToString() => $"[{Start}..{End}] \"{Text}\" ({Confidence:P0})";
    }
}
