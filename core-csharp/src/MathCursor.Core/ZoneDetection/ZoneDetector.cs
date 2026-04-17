using System.Collections.Generic;
using MathCursor.Core.Tokenization;

namespace MathCursor.Core.ZoneDetection;

/// <summary>
/// Trouve la frontière prose/math dans une séquence de tokens scorés.
/// À porter depuis archive/officejs-prototype/src/taskpane/conversion/zone-detector.ts.
/// Validé par 47 cas multilingues dans le corpus de tests.
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

public static class ZoneDetector
{
    public const double MathThreshold = 0.5;

    public static MathZone? Detect(IReadOnlyList<Token> tokens)
    {
        // TODO phase B : porter la logique du prototype
        return null;
    }
}
