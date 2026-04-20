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
        // 0. SIGNAL DE SORTIE : si le texte se termine par un signal explicite
        //    de "fin de zone math", on n'essaie même pas. Couvre :
        //    - tab (\t) — touche tab manuelle (notre Tab intercepté n'arrive pas ici)
        //    - 2+ espaces consécutifs — l'utilisateur a "double-espacé" pour sortir
        //    Un seul espace est toléré (utilisateur en pause au milieu d'une frappe).
        //    Les sauts de ligne (\n / \r) sont gérés par le bornage paragraphe
        //    de WordContextReader — pas besoin de s'en occuper ici.
        if (HasExitSignal(text))
        {
            return new ConversionResult { Success = false, Reason = "trailing_exit_signal" };
        }

        // 1. Pipeline math principal : tokenize → score → zone detection.
        //    Si une zone est trouvée, on pré-traite son texte via SymbolMatcher
        //    pour expanser TOUS les symboles (alpha → α, beta → β, etc.) avant
        //    le parser. Ainsi "alpha+beta" est traité comme une expression
        //    unique qui devient α+β en OMath.
        var tokens = Tokenizer.Tokenize(text);
        Scorer.ScoreAll((System.Collections.Generic.IList<Token>)tokens);
        var zone = ZoneDetector.Detect(tokens);
        if (zone != null)
        {
            // Pré-traitement : remplace tous les symboles dans la zone normalisée
            var preprocessed = SymbolMatcher.ReplaceAllInText(zone.Normalized);

            var lex = Lexer.Lex(preprocessed);
            var ast = Parser.Parse(lex);
            var omml = OmmlSerializer.Serialize(ast);
            var ommlPkg = OmmlSerializer.BuildPackage(omml);

            return new ConversionResult
            {
                Success = true,
                Zone = zone,
                Equation = new EquationOutput
                {
                    Source = zone.Raw, // texte original à remplacer dans le doc
                    Latex = "",
                    Omml = ommlPkg,
                    UnicodeFallback = preprocessed, // texte ASCII/Unicode pour BuildUp
                    Metadata = BuildMetadata(languageHint, candidates: 1, selected: 0),
                },
            };
        }

        // 2. Fallback : pas de zone math (pas d'opérateur, etc.) MAIS peut-être
        //    juste un mot-symbole en fin (alpha, inf, vide). On retourne alors
        //    juste le remplacement, qui sera quand même wrapé en OMath par
        //    l'host (cohérence visuelle : tout est OMath).
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
                    Omml = null, // pas de pipeline OMML — l'host wrap juste le texte
                    UnicodeFallback = choice.Replacement,
                    Metadata = BuildMetadata(languageHint, candidates: sym.Choices.Count, selected: 0),
                },
            };
        }

        return new ConversionResult { Success = false, Reason = "no_math_zone_detected" };
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

    /// <summary>
    /// Détermine si le texte se termine par un signal explicite de sortie de zone math.
    /// Les sauts de ligne sont gérés par le bornage paragraphe de WordContextReader,
    /// donc inutile ici. Reste : tab manuel et double espace (règle UX).
    /// </summary>
    private static bool HasExitSignal(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        int last = text.Length - 1;
        char c = text[last];
        if (c == '\t') return true;
        if (c == ' ' && last >= 1 && text[last - 1] == ' ') return true;
        return false;
    }
}
