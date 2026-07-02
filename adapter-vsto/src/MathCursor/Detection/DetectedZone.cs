// MathCursor: capturing mathematical intent from linear keyboard input.
// Copyright (C) 2026  Côme de Percin
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

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
