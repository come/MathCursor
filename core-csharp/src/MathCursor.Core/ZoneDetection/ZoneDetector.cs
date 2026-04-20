using System.Collections.Generic;
using System.Linq;
using System.Text;
using MathCursor.Core.Tokenization;

namespace MathCursor.Core.ZoneDetection;

/// <summary>
/// Zone math détectée : tokens, offsets, confiance, textes raw/normalized.
/// </summary>
public sealed class MathZone
{
    public int StartTokenIdx { get; init; }
    public int EndTokenIdx { get; init; }
    public double Confidence { get; init; }
    public IReadOnlyList<Token> Tokens { get; init; } = new List<Token>();
    public string Raw { get; init; } = "";
    public string Normalized { get; init; } = "";
}

/// <summary>
/// Trouve la frontière prose/math dans une séquence de tokens scorés.
/// Porté depuis archive/officejs-prototype/src/taskpane/conversion/zone-detector.ts.
/// 47 cas multilingues dans le corpus de tests (specs/test-fixtures/phase1-zone-detection.json).
/// </summary>
public static class ZoneDetector
{
    public const double MathThreshold = 0.5;

    public static MathZone? Detect(IReadOnlyList<Token> tokens)
    {
        if (tokens.Count == 0) return null;

        // Ignorer les whitespaces ET les tokens prose en fin. Un mot non-math
        // collé après une zone math ("f(x)=1/x bonjour") ne doit pas étendre la
        // zone — on la borne au dernier token vraiment math.
        int end = tokens.Count - 1;
        while (end >= 0)
        {
            var t = tokens[end];
            if (t.Categories.Contains(UnicodeCategory.Whitespace)) { end--; continue; }
            if (t.Mathiness < MathThreshold) { end--; continue; }
            break;
        }
        if (end < 0) return null;

        // Remonter depuis la fin : chercher la dernière transition prose → math
        int start = end;
        while (start > 0)
        {
            int prev = start - 1;
            var prevToken = tokens[prev];

            // Virgule = boundary SAUF si dans des parenthèses
            if (prevToken.Categories.Contains(UnicodeCategory.Comma))
            {
                int depth = 0;
                for (int j = prev + 1; j <= end; j++)
                {
                    if (tokens[j].Text == ")" || tokens[j].Text == "]") depth++;
                    if (tokens[j].Text == "(" || tokens[j].Text == "[") depth--;
                }
                if (depth <= 0) break; // virgule hors parens → boundary
                start = prev;
                continue;
            }

            // Whitespace : regarder ce qu'il y a avant
            if (prevToken.Categories.Contains(UnicodeCategory.Whitespace))
            {
                int lookback = prev - 1;
                while (lookback >= 0 && tokens[lookback].Categories.Contains(UnicodeCategory.Whitespace)) lookback--;

                if (lookback < 0) break;

                // Virgule avant le whitespace → boundary (sauf dans des parens)
                if (tokens[lookback].Categories.Contains(UnicodeCategory.Comma))
                {
                    int depth = 0;
                    for (int j = lookback + 1; j <= end; j++)
                    {
                        if (tokens[j].Text == ")" || tokens[j].Text == "]") depth++;
                        if (tokens[j].Text == "(" || tokens[j].Text == "[") depth--;
                    }
                    if (depth <= 0) break;
                }

                if (tokens[lookback].Mathiness >= MathThreshold)
                {
                    start = lookback;
                    continue;
                }
                break;
            }

            // Token score < threshold → frontière
            if (prevToken.Mathiness < MathThreshold) break;

            start = prev;
        }

        // Extraire la zone et vérifier qu'elle contient du math
        var zoneTokens = new List<Token>();
        for (int k = start; k <= end; k++) zoneTokens.Add(tokens[k]);

        int mathCount = 0;
        foreach (var t in zoneTokens)
        {
            if (!t.Categories.Contains(UnicodeCategory.Whitespace) && t.Mathiness >= MathThreshold)
                mathCount++;
        }
        if (mathCount == 0) return null;

        // Confiance : moyenne des scores non-whitespace
        var nonWs = zoneTokens.Where(t => !t.Categories.Contains(UnicodeCategory.Whitespace)).ToList();
        double avgScore = nonWs.Count == 0 ? 0 : nonWs.Sum(t => t.Mathiness) / nonWs.Count;

        // Doit contenir au moins UNE feature math vraie (pas juste un nombre ou variable isolée)
        bool hasMathFeature = zoneTokens.Any(t =>
            t.Categories.Contains(UnicodeCategory.Operator) ||
            t.Categories.Contains(UnicodeCategory.MathSymbol) ||
            t.Categories.Contains(UnicodeCategory.GreekLetter) ||
            (t.Categories.Contains(UnicodeCategory.Paren) && t.Mathiness >= 0.7));

        if (!hasMathFeature) return null;

        var rawSb = new StringBuilder();
        var normSb = new StringBuilder();
        foreach (var t in zoneTokens)
        {
            rawSb.Append(t.Text);
            normSb.Append(t.Normalized);
        }

        return new MathZone
        {
            StartTokenIdx = start,
            EndTokenIdx = end,
            Confidence = avgScore,
            Tokens = zoneTokens,
            Raw = rawSb.ToString(),
            Normalized = normSb.ToString(),
        };
    }
}
