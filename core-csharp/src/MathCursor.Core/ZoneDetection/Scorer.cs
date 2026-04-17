using System.Collections.Generic;
using MathCursor.Core.Tokenization;

namespace MathCursor.Core.ZoneDetection;

/// <summary>
/// Scoring "mathiness" : chaque token reçoit un score 0..1 selon sa
/// probabilité d'être du math. À porter depuis :
/// archive/officejs-prototype/src/taskpane/conversion/scorer.ts
/// </summary>
public static class Scorer
{
    public static void ScoreAll(IList<Token> tokens)
    {
        // TODO phase B : double passe (sans contexte, puis avec voisins)
        // Les stopwords multilingues viennent de data/stopwords.json
        // Les fonctions math de data/operators.json
    }
}
