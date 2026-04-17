using System.Collections.Generic;
using MathCursor.Core.Tokenization;

namespace MathCursor.Core.ZoneDetection;

/// <summary>
/// Scoring "mathiness" 0..1 par token. Porté depuis
/// archive/officejs-prototype/src/taskpane/conversion/scorer.ts.
/// Les data (stopwords, math functions/keywords) sont inlinées ici pour phase B1 ;
/// à terme, charger depuis data/*.json via EmbeddedResource (TODO).
/// </summary>
public static class Scorer
{
    // Fonctions math universelles
    private static readonly HashSet<string> MathFunctions = new()
    {
        "sin", "cos", "tan", "cot", "sec", "csc",
        "log", "ln", "exp",
        "lim", "sum", "prod", "int",
        "max", "min", "sup", "inf",
        "det", "dim", "ker", "arg", "mod", "gcd", "lcm",
        "sqrt", "abs",
    };

    // Mots-clés math (raccourcis qu'on reconnaît)
    private static readonly HashSet<string> MathKeywords = new()
    {
        "alpha", "beta", "gamma", "delta", "epsilon", "theta", "lambda",
        "mu", "pi", "sigma", "omega", "phi", "psi", "rho", "tau",
        "vec", "seg", "ang", "vide",
    };

    // Stopwords multilingues ultra-courts qui collisionnent avec variables math
    private static readonly HashSet<string> Stopwords = new()
    {
        // FR
        "on", "un", "une", "le", "la", "les", "de", "du", "des", "et", "ou", "en",
        "il", "ce", "se", "ne", "pas", "que", "qui", "est", "son", "sa", "ses",
        "au", "aux", "par", "sur", "dans", "pour", "avec", "soit", "car", "donc",
        "mais", "alors", "ainsi", "puis", "comme", "bien", "tout",
        "cette", "quel", "voici",
        // EN
        "the", "is", "it", "in", "at", "to", "an", "of", "or", "and",
        "we", "he", "she", "be", "do", "if", "so", "no", "not", "has", "had",
        "was", "are", "its", "but", "for", "let", "set",
        // DE
        "es", "im", "zu", "ob", "da", "um", "am",
        "ist", "ein", "und", "der", "die", "das", "dem", "den",
        "sei", "mit", "aus", "auf", "bei", "vor",
        // ES
        "el", "lo", "al", "del", "por", "con",
        "sea", "los", "las", "una",
        // IT
        "di", "su", "per",
        // PT
        "em", "no", "na", "os", "as", "ao",
    };

    public static double ScoreMathiness(Token token, Token? prev = null, Token? next = null)
    {
        var cats = token.Categories;
        var norm = token.Normalized.ToLowerInvariant();

        if (cats.Contains(UnicodeCategory.Whitespace)) return 0.5;
        if (cats.Contains(UnicodeCategory.MathSymbol)) return 1.0;
        if (cats.Contains(UnicodeCategory.GreekLetter)) return 0.95;
        if (cats.Contains(UnicodeCategory.Operator)) return 0.9;

        if (cats.Contains(UnicodeCategory.Paren))
        {
            // ( après une lettre = probable function call f(x)
            if (token.Text == "(" && prev != null && prev.Categories.Contains(UnicodeCategory.Letter))
                return 0.9;
            return 0.7;
        }

        if (cats.Contains(UnicodeCategory.Comma)) return 0.5;
        if (cats.Contains(UnicodeCategory.Digit)) return 0.8;

        if (cats.Contains(UnicodeCategory.Dot))
        {
            // Après un nombre : décimal
            if (prev != null && prev.Categories.Contains(UnicodeCategory.Digit)) return 0.8;
            // Sinon fin de phrase
            return 0.1;
        }

        if (cats.Contains(UnicodeCategory.Letter))
        {
            if (MathFunctions.Contains(norm)) return 0.95;
            if (MathKeywords.Contains(norm)) return 0.95;
            if (Stopwords.Contains(norm)) return 0.0;

            // Variable 1 lettre : probable math si voisin direct est op/paren/digit
            if (norm.Length == 1)
            {
                bool prevIsOp = prev != null && (
                    prev.Categories.Contains(UnicodeCategory.Operator) ||
                    prev.Categories.Contains(UnicodeCategory.Paren) ||
                    prev.Categories.Contains(UnicodeCategory.Digit));
                bool nextIsOp = next != null && (
                    next.Categories.Contains(UnicodeCategory.Operator) ||
                    next.Categories.Contains(UnicodeCategory.Paren) ||
                    next.Categories.Contains(UnicodeCategory.Digit));
                if (prevIsOp || nextIsOp) return 0.85;
                return 0.35;
            }

            // Mot 2 lettres : ambigu, probable math si collé à chiffre/op
            if (norm.Length == 2)
            {
                bool prevIsMath = prev != null && (
                    prev.Categories.Contains(UnicodeCategory.Digit) ||
                    prev.Categories.Contains(UnicodeCategory.Operator));
                bool nextIsMath = next != null && (
                    next.Categories.Contains(UnicodeCategory.Digit) ||
                    next.Categories.Contains(UnicodeCategory.Operator) ||
                    next.Categories.Contains(UnicodeCategory.Paren));
                if (prevIsMath || nextIsMath) return 0.7;
                return 0.2;
            }

            // Mot 3+ : ratio voyelles de prose → pas math
            if (norm.Length >= 3)
            {
                int vowels = 0;
                foreach (char c in norm)
                {
                    if ("aeiouy".IndexOf(c) >= 0) vowels++;
                }
                double ratio = (double)vowels / norm.Length;
                if (ratio >= 0.3 && ratio <= 0.6) return 0.1;
                return 0.15;
            }

            return 0.3;
        }

        return 0.3;
    }

    private static Token? FindPrevNonWs(IList<Token> tokens, int i)
    {
        for (int j = i - 1; j >= 0; j--)
        {
            if (!tokens[j].Categories.Contains(UnicodeCategory.Whitespace)) return tokens[j];
        }
        return null;
    }

    private static Token? FindNextNonWs(IList<Token> tokens, int i)
    {
        for (int j = i + 1; j < tokens.Count; j++)
        {
            if (!tokens[j].Categories.Contains(UnicodeCategory.Whitespace)) return tokens[j];
        }
        return null;
    }

    /// <summary>Scoring en 2 passes : sans contexte, puis avec voisins non-whitespace.</summary>
    public static void ScoreAll(IList<Token> tokens)
    {
        // Passe 1 : sans contexte
        for (int i = 0; i < tokens.Count; i++)
        {
            tokens[i].Mathiness = ScoreMathiness(tokens[i]);
        }

        // Passe 2 : avec voisins (les décisions de passe 1 fournissent le contexte)
        for (int i = 0; i < tokens.Count; i++)
        {
            var prev = FindPrevNonWs(tokens, i);
            var next = FindNextNonWs(tokens, i);
            tokens[i].Mathiness = ScoreMathiness(tokens[i], prev, next);
        }
    }
}
