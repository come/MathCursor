using System;
using System.Collections.Generic;
using System.Linq;

namespace MathCursor.Core.PatternEngine
{
    /// <summary>
    /// Vérifie qu'un rendu LaTeX contient bien une trace de chaque "token fort" de
    /// l'input utilisateur. Un token fort = quelque chose que l'utilisateur a tapé
    /// intentionnellement et qui DOIT apparaître au rendu (IDENT, NUMBER, token
    /// nommé avec canonical). Les connectors, stop-words, ponctuation et opérateurs
    /// "fondus" (/, *, ^) sont ignorés car légitimement transformés ou absorbés.
    ///
    /// Usage :
    /// - en test : pour chaque gold example, on asserte qu'aucun token fort n'est perdu
    /// - en runtime : on peut flagger un rendu suspect pour l'utilisateur
    /// </summary>
    public sealed class CoverageValidator
    {
        private readonly PatternRepository _repo;
        private readonly Tokenizer _tokenizer;

        public CoverageValidator(PatternRepository repo)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _tokenizer = new Tokenizer(repo);
        }

        /// <summary>
        /// Retourne la liste des tokens forts de l'input qui n'ont PAS de trace
        /// textuelle évidente dans l'output LaTeX. Liste vide = tout est couvert.
        /// </summary>
        public List<CanonicalToken> FindMissingTokens(string input, string output)
        {
            if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(output))
                return new List<CanonicalToken>();

            var tokens = _tokenizer.Tokenize(input);
            var missing = new List<CanonicalToken>();

            foreach (var t in tokens)
            {
                if (!IsStrongToken(t)) continue;
                string needle = ExpectedTrace(t);
                if (string.IsNullOrEmpty(needle)) continue;
                if (!output.Contains(needle)) missing.Add(t);
            }
            return missing;
        }

        /// <summary>True si la sortie couvre tous les tokens forts de l'entrée.</summary>
        public bool IsFullyCovered(string input, string output)
            => FindMissingTokens(input, output).Count == 0;

        // Tokens nommés dont le canonical est CONTEXT-DEPENDENT (même token → plusieurs
        // rendus selon le pattern qui matche). Le validator ne peut pas trancher, on skip.
        // VAR : "V" peut devenir \forall, \sqrt, variance.
        // COMPLEX : "C" peut devenir \mathbb{C} OU \binom (coefficient binomial).
        private static readonly HashSet<string> AmbiguousNames = new HashSet<string>(System.StringComparer.Ordinal)
        {
            "VAR",
            "COMPLEX",
        };

        // Mots bruts qui sont des marqueurs structurels (absorbés par un pattern sans
        // laisser de trace textuelle directe). "U" devient \cup, "converge" disparaît, etc.
        private static readonly HashSet<string> StructuralMarkers = new HashSet<string>(System.StringComparer.Ordinal)
        {
            "U", "converge",
            "bar", "barre",   // "x barre" → \overline{x} : absorbé par mean_overline_spaced
            "Bar", "Barre",
            "exp", "Exp",     // "exp(x)" → "e^{x}" : mot-clé absorbé, trace via "e"
        };

        private static bool IsStrongToken(CanonicalToken t)
        {
            if (t.IsConnector) return false;
            if (!string.IsNullOrEmpty(t.Name) && AmbiguousNames.Contains(t.Name!)) return false;
            if (StructuralMarkers.Contains(t.Raw)) return false;
            // Raccourcis "Vx" (VxShort) et "Cf" (CfShort) sont éclatés au rendu :
            // "Vx" → "\forall x" (V disparaît comme keyword forall, seul x trace).
            // On accepte la perte du préfixe en skippant ces tokens.
            if (IsShorthandIdent(t.Raw)) return false;
            switch (t.Raw)
            {
                case "+": case "-": case "*": case "/": case "^": case "_":
                case "=": case "<": case ">":
                case ",": case ";":
                case "(": case ")": case "[": case "]": case "{": case "}":
                case "|":
                    return false;
            }
            return true;
        }

        private static bool IsShorthandIdent(string raw)
        {
            if (raw.Length < 2) return false;
            // "Vx" (forall), "Cf" (courbe), "Df" (ensemble de définition)
            if ((raw[0] == 'V' || raw[0] == 'C' || raw[0] == 'D') && raw.Length >= 2)
            {
                bool allLower = true;
                for (int i = 1; i < raw.Length; i++)
                    if (!char.IsLower(raw[i])) { allLower = false; break; }
                if (allLower) return true;
            }
            // "xbarre" / "ybar" (moyenne statistique)
            if (raw.Length >= 4 && char.IsLetter(raw[0]))
            {
                string suffix = raw.Substring(1).ToLowerInvariant();
                if (suffix == "bar" || suffix == "barre") return true;
            }
            // "xA" / "yB" / "zM" / "xa" (coordonnée d'un point)
            if (raw.Length == 2)
            {
                char f = raw[0];
                bool isCoordBase = f == 'x' || f == 'y' || f == 'z'
                                || f == 'X' || f == 'Y' || f == 'Z';
                if (isCoordBase && char.IsLetter(raw[1])) return true;
            }
            return false;
        }

        /// <summary>
        /// La trace attendue d'un token dans l'output. Pour un IDENT c'est son raw,
        /// pour un token nommé c'est sa canonical (ex. LIMIT → "\lim").
        /// </summary>
        private static string ExpectedTrace(CanonicalToken t)
        {
            if (!string.IsNullOrEmpty(t.Canonical)) return t.Canonical;
            return t.Raw;
        }
    }
}
