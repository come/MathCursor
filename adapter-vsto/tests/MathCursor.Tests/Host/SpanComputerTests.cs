using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MathCursor.Host;
using Xunit;

namespace MathCursor.Tests.Host
{
    /// <summary>
    /// Span du Ctrl+Espace manuel (ADR 2026-06-18-Fix-input-autocorrect-fraction-
    /// factorial). Régression clé : `!` retiré des délimiteurs → un caret juste
    /// après `n!` capte bien `n!` au lieu d'une span vide (= pas de popup).
    ///
    /// Les cas vivent dans <c>Host/spancomputer-fixtures.txt</c> — SOURCE UNIQUE
    /// partagée avec le port JS (<c>web-demo/.../spancomputer.test.js</c>), pour
    /// que la parité C#↔JS ne puisse plus dériver en silence (ADR 2026-06-23).
    /// </summary>
    public sealed class SpanComputerTests
    {
        private static readonly IReadOnlyList<(int, int)> NoOMath = new List<(int, int)>();

        // Reproduit le flux de ConversionController.Trigger : span brute puis
        // trim des blancs aux deux bouts.
        private static string Span(string text, int caret)
        {
            int s = SpanComputer.ComputeSpanStart(text, caret, NoOMath);
            int e = SpanComputer.ComputeSpanEnd(text, caret, NoOMath);
            while (s < e && char.IsWhiteSpace(text[s])) s++;
            while (e > s && char.IsWhiteSpace(text[e - 1])) e--;
            return text.Substring(s, e - s);
        }

        public static IEnumerable<object[]> ParityCases()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "spancomputer-fixtures.txt");
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var p = line.Split('|');
                yield return new object[] { p[0], p[1], int.Parse(p[2], CultureInfo.InvariantCulture), p[3] };
            }
        }

        [Theory]
        [MemberData(nameof(ParityCases))]
        public void Parity(string name, string text, int caret, string expected)
        {
            var got = Span(text, caret);
            Assert.True(got == expected, $"{name} : attendu \"{expected}\", obtenu \"{got}\"");
        }
    }
}
