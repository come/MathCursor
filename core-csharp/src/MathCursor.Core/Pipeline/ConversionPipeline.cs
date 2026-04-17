using MathCursor.Core.Parsing;
using MathCursor.Core.Serialization;
using MathCursor.Core.Symbols;
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
        // 1. Patterns symboliques en priorité (vec AB, Vx(R, alpha, ≥, ...).
        //    Si match en fin de texte, on remplace JUSTE cette sous-chaîne par
        //    son rendu unicode, sans wrapping OMath (insertion texte simple).
        var sym = SymbolMatcher.FindSymbol(text);
        if (sym != null && sym.Choices.Count > 0)
        {
            var choice = sym.Choices[0];
            return new ConversionResult
            {
                Success = true,
                Zone = null,
                Equation = new EquationOutput
                {
                    Source = sym.Raw,
                    Latex = "",
                    Omml = null, // null = insertion texte simple, pas d'OMath wrap
                    UnicodeFallback = choice.Replacement,
                    Metadata = BuildMetadata(languageHint, candidates: sym.Choices.Count, selected: 0),
                },
            };
        }

        // 2. Sinon : pipeline math classique (zone detection → AST → OMML)
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

        return new ConversionResult
        {
            Success = true,
            Zone = zone,
            Equation = new EquationOutput
            {
                Source = zone.Raw,
                Latex = "",
                Omml = ommlPkg,
                UnicodeFallback = zone.Normalized,
                Metadata = BuildMetadata(languageHint, candidates: 1, selected: 0),
            },
        };
    }

    private static EquationMetadata BuildMetadata(string? languageHint, int candidates, int selected)
    {
        return new EquationMetadata
        {
            SourceLanguage = languageHint,
            CandidatesConsidered = candidates,
            SelectedCandidateIndex = selected,
            CoreVersion = CoreVersion,
        };
    }
}
