using System.Collections.Generic;

namespace MathCursor.Core.PatternEngine
{
    /// <summary>
    /// Définition d'un token canonique chargé depuis YAML.
    /// </summary>
    public sealed class TokenDef
    {
        public string Name { get; set; } = "";
        public string Canonical { get; set; } = "";
        public IReadOnlyList<string> Aliases { get; set; } = System.Array.Empty<string>();
        public int FuzzyMaxDistance { get; set; } = 0;
        public string Role { get; set; } = ""; // "connector" = ignoré silencieusement pendant le match
        public int Priority { get; set; }       // priorité du fichier source (collision d'alias)
    }

    /// <summary>
    /// Définition d'un pattern chargé depuis YAML.
    /// </summary>
    public sealed class PatternDef
    {
        public string Id { get; set; } = "";
        public string Description { get; set; } = "";
        public string Match { get; set; } = "";
        public string Template { get; set; } = "";
        public int Priority { get; set; } // domain priority
        public IReadOnlyList<PatternExample> Examples { get; set; } = System.Array.Empty<PatternExample>();
    }

    public sealed class PatternExample
    {
        public string Input { get; set; } = "";
        public string Output { get; set; } = "";
        /// <summary>Si non vide, raison pour laquelle cet exemple n'est pas couvert par le moteur actuel.</summary>
        public string Skip { get; set; } = "";
    }

    /// <summary>Résultat du moteur : un candidat LaTeX avec son score.</summary>
    public sealed class LatexSuggestion
    {
        public string Latex { get; }
        public string PatternId { get; }
        public double Score { get; }
        /// <summary>Nombre de tokens consommés par le pattern sur le span d'entrée.</summary>
        public int ConsumedTokens { get; }
        /// <summary>Nombre total de tokens dans le span d'entrée (pour calculer le ratio).</summary>
        public int TotalTokens { get; }
        /// <summary>True si le pattern a matché en préfixe seulement (input incomplet).
        /// Les slots non capturés ont été rendus en <c>\ldots</c>. Signal à la popup
        /// qu'il faut afficher ces placeholders en couleur pour indiquer à l'utilisateur
        /// qu'il lui reste à taper la suite.</summary>
        public bool IsPartial { get; }

        public LatexSuggestion(string latex, string patternId, double score, int consumed = 0, int total = 0, bool isPartial = false)
        {
            Latex = latex;
            PatternId = patternId;
            Score = score;
            ConsumedTokens = consumed;
            TotalTokens = total;
            IsPartial = isPartial;
        }
    }

    /// <summary>Token canonicalisé produit par le tokenizer.</summary>
    public sealed class CanonicalToken
    {
        /// <summary>Nom du token canonique (ex. "LIMIT"). Null si IDENT/NUMBER libre.</summary>
        public string? Name { get; set; }
        /// <summary>Valeur brute telle que tapée (utilisée pour capturer les slots IDENT/NUMBER/EXPR).</summary>
        public string Raw { get; set; } = "";
        /// <summary>Forme canonique à rendre (ex. "\\lim"). Égal à Raw si pas de canonical dédié.</summary>
        public string Canonical { get; set; } = "";
        /// <summary>Type générique si pas un token nommé : "IDENT" ou "NUMBER". Vide si Name != null.</summary>
        public string Generic { get; set; } = "";
        /// <summary>True si ce token est un connector (ignoré silencieusement pendant le match).</summary>
        public bool IsConnector { get; set; }
        /// <summary>True si ce token était précédé d'au moins un whitespace dans le texte source.</summary>
        public bool HadSpaceBefore { get; set; }
        /// <summary>Offset caractère de début dans le span source.</summary>
        public int Start { get; set; }
        /// <summary>Offset caractère de fin (exclusif) dans le span source.</summary>
        public int End { get; set; }

        public override string ToString()
        {
            if (Name != null) return Name + (Raw.Length > 0 ? $"({Raw})" : "");
            return Generic + ":" + Raw;
        }
    }
}
