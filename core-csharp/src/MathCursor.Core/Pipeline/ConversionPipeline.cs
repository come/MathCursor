using MathCursor.Core.Parsing;
using MathCursor.Core.Serialization;
using MathCursor.Core.Tokenization;
using MathCursor.Core.ZoneDetection;
using MathCursor.HostContract;

namespace MathCursor.Core.Pipeline;

/// <summary>
/// Pipeline complet de conversion : texte → détection zone → AST → OMML.
/// Pure function, zéro dépendance plateforme. Testable unitairement.
/// </summary>
public static class ConversionPipeline
{
    public const string CoreVersion = "0.1.0";

    /// <summary>Résultat détaillé pour observabilité + insertion.</summary>
    public sealed class ConversionResult
    {
        public bool Success { get; init; }
        public string? Reason { get; init; }
        public MathZone? Zone { get; init; }
        public EquationOutput? Equation { get; init; }
    }

    /// <summary>
    /// Détecte la zone math dans <paramref name="text"/>, parse l'expression et
    /// produit une <see cref="EquationOutput"/> prête à être insérée.
    /// </summary>
    public static ConversionResult Convert(string text, string? languageHint = null)
    {
        var tokens = Tokenizer.Tokenize(text);
        Scorer.ScoreAll((System.Collections.Generic.IList<Token>)tokens);
        var zone = ZoneDetector.Detect(tokens);
        if (zone == null)
        {
            return new ConversionResult { Success = false, Reason = "no_math_zone_detected" };
        }

        var lex = Lexer.Lex(zone.Normalized);
        var ast = Parser.Parse(lex);
        var omml = OmmlSerializer.Serialize(ast);
        var ommlPkg = OmmlSerializer.BuildPackage(omml);

        var equation = new EquationOutput
        {
            Source = zone.Raw,
            Latex = "", // TODO phase B3 : LaTeX serializer
            Omml = ommlPkg,
            UnicodeFallback = zone.Normalized,
            Metadata = new EquationMetadata
            {
                SourceLanguage = languageHint,
                CandidatesConsidered = 1,
                SelectedCandidateIndex = 0,
                CoreVersion = CoreVersion,
            },
        };

        return new ConversionResult
        {
            Success = true,
            Zone = zone,
            Equation = equation,
        };
    }
}
