using System.Collections.Generic;

namespace MathCursor.Core.Symbols;

/// <summary>Un candidat de remplacement issu d'un pattern symbolique.</summary>
public sealed class SymbolChoice
{
    public string Label { get; init; } = "";
    public string Display { get; init; } = "";
    public string Replacement { get; init; } = "";
    /// <summary>True si la suggestion vient d'un match préfixe (input incomplet) :
    /// contient un ou plusieurs <c>\ldots</c> en placeholders. La popup les colorie
    /// pour signaler à l'utilisateur que la formule reste à compléter.</summary>
    public bool IsPartial { get; init; }
}

/// <summary>
/// Résultat d'un match symbol : la sous-chaîne du texte qui a matché et
/// les choix proposés à l'utilisateur (1 ou plusieurs en cas d'ambiguïté).
/// </summary>
public sealed class SymbolMatch
{
    /// <summary>La sous-chaîne du texte original qui a matché (à remplacer).</summary>
    public string Raw { get; init; } = "";
    public IReadOnlyList<SymbolChoice> Choices { get; init; } = new List<SymbolChoice>();
}
