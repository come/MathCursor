using System;
using System.Collections.Generic;
using MathCursor.Engine.Emit;
using MathCursor.Engine.Rules;
using MathCursor.Engine.Tokenization;
using MathCursor.Engine.Vocabulary;

namespace MathCursor.Engine.Resolution
{
    /// <summary>
    /// Pre-resolver « prefix-match en cours de frappe » : si le source est un
    /// seul Word de longueur ≥ 3 préfixe d'un keyword reconnu (anchor,
    /// function, relation textuelle), on suggère via Collisions (= N matches)
    /// ou TopLatex (= 1 unique match avec carrés).
    ///
    /// <para>User-request 2026-05-25 : « il tape inte la popup montre inter
    /// et c'est bon… il tape ome la popup montre omega ».</para>
    ///
    /// <para>Migration Chantier 3 (2026-05-25) : extrait de <c>MathEngine.Resolve</c>
    /// vers un module dédié, implémente <see cref="IPreResolver"/>.</para>
    /// </summary>
    public sealed class PrefixMatchResolver : IPreResolver
    {
        private readonly LocaleVocabulary _vocab;
        private readonly IReadOnlyList<RuleSpec> _rules;
        private readonly TemplateEmitter _templateEmitter;

        public PrefixMatchResolver(
            LocaleVocabulary vocab,
            IReadOnlyList<RuleSpec> rules,
            TemplateEmitter templateEmitter)
        {
            _vocab = vocab ?? throw new ArgumentNullException(nameof(vocab));
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _templateEmitter = templateEmitter ?? throw new ArgumentNullException(nameof(templateEmitter));
        }

        public EngineResult? TryResolve(IReadOnlyList<Token> tokens)
        {
            if (!IsSingleWordStandalone(tokens, out var word)) return null;
            var matches = FindMatches(word);
            if (matches.Count == 1)
            {
                var only = matches[0];
                return new EngineResult(
                    topLatex: only.Latex,
                    isComplete: !only.Latex.Contains(@"\square"),
                    collisions: Array.Empty<EngineCandidate>(),
                    ruleId: "prefix-match:" + only.Source);
            }
            if (matches.Count >= 2)
            {
                var candidates = new List<EngineCandidate>(matches.Count);
                foreach (var pm in matches)
                    candidates.Add(new EngineCandidate(
                        latex: pm.Latex,
                        description: pm.Keyword,
                        ruleId: "prefix-match:" + pm.Source,
                        score: 100 - pm.Keyword.Length));
                return new EngineResult(
                    topLatex: word,
                    isComplete: false,
                    collisions: candidates,
                    ruleId: "prefix-match:multi");
            }
            return null;
        }

        /// <summary>True si <paramref name="tokens"/> est exactement 1 token
        /// Word. Utilisé pour ne déclencher que sur un mot standalone tapé
        /// en cours de frappe, pas dans une expression complète.</summary>
        public static bool IsSingleWordStandalone(IReadOnlyList<Token> tokens, out string word)
        {
            word = string.Empty;
            if (tokens.Count != 1) return false;
            if (tokens[0].Kind != TokenKind.Word) return false;
            word = tokens[0].Text;
            return true;
        }

        /// <summary>
        /// Cherche tous les keywords (anchors, functions, relations textuelles)
        /// dont <paramref name="word"/> est un préfixe strict (= longueur ≥ 3,
        /// mot complet exclu — laissé au full match). Casse contrôlée :
        /// fonctions distinguent <c>omega</c> vs <c>Omega</c>. Public car
        /// réutilisé par le closure anchor matcher de <c>MathEngine</c>.
        /// </summary>
        public IReadOnlyList<PrefixMatch> FindMatches(string word)
        {
            var results = new List<PrefixMatch>();
            if (word == null || word.Length < 3) return results;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var wordLower = word.ToLowerInvariant();

            foreach (var kv in _vocab.Anchors)
            {
                var alias = kv.Key;
                var aliasLower = alias.ToLowerInvariant();
                if (aliasLower == wordLower) continue;
                if (!aliasLower.StartsWith(wordLower, StringComparison.Ordinal)) continue;
                foreach (var rule in _rules)
                {
                    if (rule.Anchor != kv.Value) continue;
                    var synthetic = new ShapeMatch(rule, 0, 0,
                        new Dictionary<string, IReadOnlyList<Token>>(),
                        isPartial: true);
                    var emitted = _templateEmitter.Emit(synthetic);
                    if (seen.Add(alias + "→" + emitted))
                        results.Add(new PrefixMatch(alias, emitted, "anchor"));
                }
            }

            foreach (var kv in _vocab.Functions)
            {
                var name = kv.Key;
                var nameLower = name.ToLowerInvariant();
                if (nameLower == wordLower) continue;
                if (!nameLower.StartsWith(wordLower, StringComparison.Ordinal)) continue;
                bool userIsUpper = word.Length >= 1 && char.IsUpper(word[0]);
                bool keywordIsUpper = name.Length >= 1 && char.IsUpper(name[0]);
                if (userIsUpper != keywordIsUpper) continue;
                if (seen.Add(name + "→" + kv.Value))
                    results.Add(new PrefixMatch(name, kv.Value, "function"));
            }

            foreach (var kv in _vocab.Relations)
            {
                var name = kv.Key;
                if (name.Length < 2 || !char.IsLetter(name[0])) continue;
                var nameLower = name.ToLowerInvariant();
                if (nameLower == wordLower) continue;
                if (!nameLower.StartsWith(wordLower, StringComparison.Ordinal)) continue;
                if (seen.Add(name + "→" + kv.Value.Tex))
                    results.Add(new PrefixMatch(name, kv.Value.Tex, "relation"));
            }

            results.Sort((a, b) => string.Compare(a.Keyword, b.Keyword, StringComparison.Ordinal));
            return results;
        }
    }

    /// <summary>
    /// Représentation d'un prefix-match : le user a tapé un préfixe d'un
    /// keyword reconnu (anchor, function, relation textuelle), pas le
    /// keyword complet. User-request 2026-05-25 : « le gars peut passer
    /// à la suite, genre il tape inte la popup montre inter ».
    /// </summary>
    public readonly struct PrefixMatch
    {
        public PrefixMatch(string keyword, string latex, string source)
        { Keyword = keyword; Latex = latex; Source = source; }
        public string Keyword { get; }
        public string Latex { get; }
        public string Source { get; }
    }
}
