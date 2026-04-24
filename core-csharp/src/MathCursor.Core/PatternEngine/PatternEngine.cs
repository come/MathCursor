using System;
using System.Collections.Generic;
using System.Linq;

namespace MathCursor.Core.PatternEngine
{
    /// <summary>
    /// Point d'entrée : convertit un span texte brut en LaTeX canonique via
    /// matching de patterns YAML.
    /// </summary>
    public sealed class PatternEngine
    {
        private readonly PatternRepository _repo;
        private readonly Tokenizer _tokenizer;

        public PatternEngine(PatternRepository repo)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _tokenizer = new Tokenizer(repo);
        }

        /// <summary>Charge les YAML embarqués pour la langue donnée et construit le moteur.</summary>
        public static PatternEngine LoadEmbedded(string language = "fr")
        {
            return new PatternEngine(PatternRepository.LoadEmbedded(language));
        }

        // Profondeur max de récursion via les slots EXPR. 3 = top-level puis 2 niveaux
        // de récursion. Couvre "equation_simple → fraction_inline → sqrt_as_V" et empêche
        // les boucles infinies.
        private const int MaxRecursionDepth = 3;

        /// <summary>
        /// Convertit un span en 0 à N suggestions LaTeX triées par score décroissant.
        /// </summary>
        public IReadOnlyList<LatexSuggestion> Convert(string rawSpan) => ConvertInternal(rawSpan, MaxRecursionDepth);

        /// <summary>Récursion limitée à la profondeur donnée (0 = plus de récursion).</summary>
        internal IReadOnlyList<LatexSuggestion> ConvertAtDepth(string rawSpan, int depth) => ConvertInternal(rawSpan, depth);

        private IReadOnlyList<LatexSuggestion> ConvertInternal(string rawSpan, int recursionDepth)
        {
            if (string.IsNullOrWhiteSpace(rawSpan)) return Array.Empty<LatexSuggestion>();
            // Rééquilibrage des parens : on supprime les ')' en trop et on complète les
            // '(' manquants. Ça permet aux captures EXPR de ne pas être rejetées par
            // ParensBalanced quand l'utilisateur tape une paren en rab ou en oublie une.
            rawSpan = BalanceParens(rawSpan);
            var tokens = _tokenizer.Tokenize(rawSpan);
            if (tokens.Count == 0) return Array.Empty<LatexSuggestion>();

            var renderer = new TemplateRenderer(recursionDepth > 0 ? this : null, rawSpan, recursionDepth);

            var candidates = new List<LatexSuggestion>();
            foreach (var pattern in _repo.Patterns)
            {
                MatchResult? mr;
                try { mr = PatternMatcher.TryMatch(pattern, tokens); }
                catch { mr = null; }
                if (mr == null) continue;

                List<string> latexAlts;
                try { latexAlts = renderer.RenderAll(pattern.Template, mr); }
                catch { continue; }

                double score = ComputeScore(pattern, mr, tokens);
                // Le premier rendu = variante top-1 (full score).
                // Les suivants = alternatives (swap d'un slot) à score légèrement dégradé
                // pour qu'elles apparaissent en-dessous sans dominer.
                for (int i = 0; i < latexAlts.Count; i++)
                {
                    double altScore = i == 0 ? score : score * 0.95;
                    candidates.Add(new LatexSuggestion(latexAlts[i], pattern.Id, altScore, mr.ConsumedTokens, tokens.Count));
                }
            }

            var sorted = candidates.OrderByDescending(s => s.Score).ToList();

            // Déduplication NORMALISÉE : on écrase les whitespaces multiples en un seul
            // pour que "V(x + 1)" et "V(x+1)" soient considérés identiques. Évite les
            // doublons visuels dans la popup.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var deduped = new List<LatexSuggestion>(sorted.Count);
            foreach (var s in sorted)
                if (seen.Add(NormalizeLatex(s.Latex))) deduped.Add(s);

            // Filtre de score : drop les candidats sous 30% du top.
            // Exception pour full-coverage : ne s'applique QUE si le top est partiel
            // (besoin de garder le fallback generic_expression qui capture tout).
            // Si le top est déjà full-coverage, pas d'exception — on épure.
            if (deduped.Count > 1)
            {
                double top = deduped[0].Score;
                bool topIsFull = deduped[0].TotalTokens > 0 && deduped[0].ConsumedTokens == deduped[0].TotalTokens;
                double threshold = top * 0.3;
                deduped = deduped.Where(s =>
                    s.Score >= threshold
                    || (!topIsFull && s.TotalTokens > 0 && s.ConsumedTokens == s.TotalTokens)
                ).ToList();
            }

            // Fallback partiels : si aucun candidat complet ET qu'on n'est pas en
            // récursion (on ne veut pas que les partiels polluent le rendu d'un EXPR
            // interne), on tente un match préfixe sur tous les patterns. Les slots
            // non remplis sortent en "\ldots" que la popup coloriera en rouge.
            // Seulement quand deduped est vide : option (a) du design — filet de
            // secours, pas de bruit sous des matches complets.
            if (deduped.Count == 0 && recursionDepth == MaxRecursionDepth)
            {
                deduped = ConvertPartials(tokens, rawSpan);
            }
            return deduped;
        }

        /// <summary>
        /// Match "préfixe" sur tous les patterns : on accepte qu'ils se terminent
        /// avant la fin des tokens si l'input est épuisé. Les slots non atteints
        /// sont rendus en <c>\ldots</c> par le TemplateRenderer. On trie par nombre
        /// de tokens consommés (plus consommé = préfixe plus long = suggestion plus
        /// pertinente) et on limite à 3 propositions.
        /// </summary>
        private List<LatexSuggestion> ConvertPartials(List<CanonicalToken> tokens, string rawSpan)
        {
            var renderer = new TemplateRenderer(null, rawSpan, 0);
            var partials = new List<LatexSuggestion>();
            foreach (var pattern in _repo.Patterns)
            {
                // generic_expression (priorité 1) = catch-all qui ne contient pas de
                // placeholder utile en mode partiel → on le skip.
                if (pattern.Id == "generic_expression") continue;

                MatchResult? mr;
                try { mr = PatternMatcher.TryMatchPrefix(pattern, tokens); }
                catch { mr = null; }
                if (mr == null || !mr.IsPartial) continue;
                // Rejeter les partiels "inutiles" : si aucun slot n'a été capturé,
                // la suggestion est juste un squelette sans le contexte utilisateur.
                if (mr.Slots.Count == 0) continue;

                string latex;
                try { latex = renderer.Render(pattern.Template, mr); }
                catch { continue; }
                if (string.IsNullOrWhiteSpace(latex)) continue;

                // Score = tokens consommés * priorité (pas le ComputeScore complet,
                // qui favorise la couverture — en mode partiel la couverture est
                // mécaniquement incomplète).
                double score = mr.ConsumedTokens * Math.Max(1, pattern.Priority);
                partials.Add(new LatexSuggestion(latex, pattern.Id, score, mr.ConsumedTokens, tokens.Count, isPartial: true));
            }

            // Déduplication + tri + cap à 3
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var unique = new List<LatexSuggestion>();
            foreach (var s in partials.OrderByDescending(s => s.Score))
                if (seen.Add(NormalizeLatex(s.Latex))) unique.Add(s);
            return unique.Take(3).ToList();
        }

        /// <summary>
        /// Normalisation pour dédup visuelle : supprime tous les whitespaces.
        /// LaTeX-wise "x + 1" et "x+1" rendent identique, donc on les considère dupliqués.
        /// </summary>
        private static string NormalizeLatex(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var c in s)
                if (c != ' ' && c != '\t' && c != '\n' && c != '\r') sb.Append(c);
            return sb.ToString();
        }

        /// <summary>
        /// Rééquilibre les parenthèses de l'input :
        /// - ')' en excès → supprimées (celles qui feraient passer depth sous zéro)
        /// - '(' en excès → compensées par ajout de ')' en fin
        /// Les patterns shorthand (où '(' peut être utilisé comme '∈') gèrent
        /// le cas via un RPAREN? optionnel — ils matchent avec ou sans ')'.
        ///
        /// Exception : les notations de demi-droite FR "[AB)" et "(AB]" utilisent
        /// des parens "déséquilibrées" intentionnellement. Si l'input ressemble à
        /// une demi-droite (bracket + 2 majuscules + paren, 4 caractères), on
        /// laisse tel quel.
        /// </summary>
        private static string BalanceParens(string span)
        {
            if (LooksLikeHalfLine(span)) return span;

            int opens = 0, closes = 0;
            foreach (var c in span)
            {
                if (c == '(') opens++;
                else if (c == ')') closes++;
            }
            if (opens == closes) return span;

            if (closes > opens)
            {
                var sb = new System.Text.StringBuilder(span.Length);
                int depth = 0;
                foreach (var c in span)
                {
                    if (c == '(') { depth++; sb.Append(c); }
                    else if (c == ')')
                    {
                        if (depth > 0) { depth--; sb.Append(c); }
                    }
                    else sb.Append(c);
                }
                return sb.ToString();
            }
            return span + new string(')', opens - closes);
        }

        // Reconnaît "[AB)" ou "(AB]" : crochet/paren d'un type + 2+ majuscules +
        // paren/crochet de l'autre type. Pas d'autres caractères.
        private static bool LooksLikeHalfLine(string s)
        {
            var trimmed = s.Trim();
            if (trimmed.Length < 4) return false;
            char first = trimmed[0];
            char last = trimmed[trimmed.Length - 1];
            bool openVariant = (first == '[' && last == ')') || (first == '(' && last == ']');
            if (!openVariant) return false;
            for (int k = 1; k < trimmed.Length - 1; k++)
                if (!char.IsUpper(trimmed[k])) return false;
            return true;
        }

        private static double ComputeScore(PatternDef pattern, MatchResult mr, List<CanonicalToken> tokens)
        {
            // Score favorise les patterns qui couvrent une grande fraction du span :
            //   score = ratio_couverture * tokens_consommés * priority * slot_quality
            // Ratio de couverture évite qu'un pattern haute priorité matchant 1 token
            // écrase un pattern moyenne priorité matchant tout le span.
            int consumed = mr.ConsumedTokens > 0 ? mr.ConsumedTokens : tokens.Count;
            double priorityFactor = pattern.Priority > 0 ? pattern.Priority : 1;
            double coverage = tokens.Count > 0 ? consumed / (double)tokens.Count : 1.0;

            int canonicalCount = 0, totalInSlots = 0;
            foreach (var kv in mr.Slots)
                foreach (var t in kv.Value)
                {
                    totalInSlots++;
                    if (t.Name != null) canonicalCount++;
                }
            double slotBonus = totalInSlots > 0 ? 1.0 + 0.2 * (canonicalCount / (double)totalInSlots) : 1.0;

            return coverage * consumed * priorityFactor * slotBonus;
        }
    }
}
