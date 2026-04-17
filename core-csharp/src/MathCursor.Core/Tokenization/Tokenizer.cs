using System.Collections.Generic;

namespace MathCursor.Core.Tokenization;

/// <summary>
/// Texte → tokens catégorisés. À porter depuis le prototype Office.js :
/// archive/officejs-prototype/src/taskpane/conversion/tokenizer.ts
/// </summary>
public static class Tokenizer
{
    public static IReadOnlyList<Token> Tokenize(string text)
    {
        // TODO phase B : implémenter selon archive/officejs-prototype/.../tokenizer.ts
        // - Classifier chaque codepoint (letter, greekLetter, mathSymbol, operator, etc.)
        // - Grouper en tokens (lettres en mots, chiffres en nombres, ops multi-char)
        // - Normaliser les math italic U+1D400+ vers ASCII
        return new List<Token>();
    }
}
