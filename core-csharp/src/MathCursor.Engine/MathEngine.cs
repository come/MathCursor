using System.Collections.Generic;
using System.Linq;
using System.Text;
using MathCursor.Engine.Ast;
using MathCursor.Engine.Collision;
using MathCursor.Engine.Collision.Detectors;
using MathCursor.Engine.Emit;
using MathCursor.Engine.Parsing;
using MathCursor.Engine.Parsing.List;
using MathCursor.Engine.Resolution;
using MathCursor.Engine.Rewriting;
using MathCursor.Engine.Rewriting.Yaml;
using MathCursor.Engine.Rules;
using MathCursor.Engine.Tokenization;
using MathCursor.Engine.Vocabulary;

namespace MathCursor.Engine
{
    /// <summary>
    /// Implémentation par défaut de <see cref="IEngineFrontend"/>.
    ///
    /// <para>P14 (2026-05-22) — parsing top-level avec composition :</para>
    /// <list type="number">
    ///   <item>Tokenize via <see cref="Tokenizer"/>.</item>
    ///   <item>Parse top-level séquentiel : à chaque position, soit une ancre
    ///     reconnue → match d'une règle YAML (= operand "anchor"), soit un
    ///     atom/groupe via <see cref="StackParser"/> (= operand "flat").</item>
    ///   <item>Entre operands, infixes top-level via <see cref="LocaleVocabulary.Relations"/>.</item>
    ///   <item>Compose en arbre via précédence des tiers ; emit LaTeX.</item>
    /// </list>
    ///
    /// <para>Permet la composition <c>lim f + lim g</c> →
    /// <c>\lim f + \lim g</c> (= 2 ancres compositées par <c>+</c>).</para>
    /// </summary>
    public sealed class MathEngine : IEngineFrontend
    {
        private readonly LocaleVocabulary _vocab;
        private readonly IReadOnlyList<RuleSpec> _rules;

        /// <summary>Vocab locale chargé (= stopwords, span_delimiters,
        /// math_prefix_keywords, etc. côté data-driven). Exposé pour que
        /// l'adapter VSTO accède aux mêmes listes que le moteur. Migration
        /// Chantier 1 (2026-05-25).</summary>
        public LocaleVocabulary Vocab => _vocab;
        private readonly Tokenizer _tokenizer;
        private readonly ShapeMatcher _matcher;
        private readonly TemplateEmitter _templateEmitter;
        private readonly StackParser _parser;
        private readonly LatexEmitter _flatEmitter;

        private readonly IReadOnlyList<ICollisionDetector> _detectors;
        private readonly CollisionScores _collisionScores;

        // Pre-resolvers (Chantier 3, 2026-05-25) : itérés en début de Resolve
        // avant le main loop. Premier match wins. Cf. IPreResolver.
        private readonly PrefixMatchResolver _prefixMatchResolver;
        private readonly IReadOnlyList<IPreResolver> _preResolvers;

        // Phase D-6 (2026-05-26) : RewriteEngine optionnel pour la bascule
        // prod. Si non null, Resolve délègue ; sinon legacy main loop.
        private readonly RewriteEngine? _rewriteEngine;

        /// <summary>True si ce MathEngine a été construit avec le
        /// RewriteEngine (= bascule prod activée).</summary>
        public bool UsesRewriteEngine => _rewriteEngine != null;

        public MathEngine(LocaleVocabulary vocab, IReadOnlyList<RuleSpec> rules)
            : this(vocab, rules, rewriteEngine: null) { }

        public MathEngine(LocaleVocabulary vocab, IReadOnlyList<RuleSpec> rules,
            RewriteEngine? rewriteEngine)
        {
            _vocab = vocab ?? throw new System.ArgumentNullException(nameof(vocab));
            _rules = rules ?? throw new System.ArgumentNullException(nameof(rules));
            _rewriteEngine = rewriteEngine;
            _tokenizer = new Tokenizer(_vocab);
            _matcher = new ShapeMatcher(_vocab);
            _templateEmitter = new TemplateEmitter(_vocab);
            _parser = new StackParser(_vocab);
            _flatEmitter = new LatexEmitter(preferSubscript: false, vocab: _vocab);
            _prefixMatchResolver = new PrefixMatchResolver(_vocab, _rules, _templateEmitter);
            _preResolvers = new IPreResolver[]
            {
                new MultiLineBlockResolver(_vocab, _parser, _flatEmitter),
                _prefixMatchResolver,
            };

            // Inject l'anchor matcher dans StackParser pour que les ancres
            // (lim/sum/int/cos/…) soient reconnues PARTOUT dans l'AST, y
            // compris à l'intérieur des groupes. Sans ça, `(somme k 0 1 k)`
            // produit `(sommek01k)` au lieu de `(\sum_{k=0}^{1} k)`.
            // Cf. user-report 2026-05-23 (bug F).
            // Propagate le parser configuré au TemplateEmitter pour que les
            // slots d'expression imbriqués (= body de FuncDef, slot expr de
            // sum, etc.) bénéficient aussi du anchor matcher. Cf. P3.
            _templateEmitter.SetParser(_parser);
            _parser.SetAnchorMatcher((tokens, startIdx) =>
            {
                var matches = TryAllAnchorMatchesWithPartialFallback(tokens, startIdx);
                if (matches.Count > 0)
                {
                    matches.Sort((a, b) =>
                    {
                        // Prioritise full match (non-partial) avant partial.
                        int dPartial = (a.IsPartial ? 1 : 0).CompareTo(b.IsPartial ? 1 : 0);
                        if (dPartial != 0) return dPartial;
                        int dSpan = (b.End - b.Start).CompareTo(a.End - a.Start);
                        if (dSpan != 0) return dSpan;
                        return b.Slots.Count.CompareTo(a.Slots.Count);
                    });
                    var best = matches[0];
                    return (_templateEmitter.Emit(best), best.End);
                }
                // Prefix-match : dans un groupe, le sub-flux contient juste 1
                // Word puis CloseDelim (= `f(som)`, `(somme dans groupe)`).
                // Couvre aussi `som` seul au top-level si appelé là. User-request
                // 2026-05-25 : « fais les DEUX », comportement uniforme.
                if (startIdx < tokens.Count
                    && tokens[startIdx].Kind == TokenKind.Word
                    && tokens[startIdx].Text.Length >= 3
                    && (startIdx + 1 == tokens.Count
                        || tokens[startIdx + 1].Kind == TokenKind.CloseDelim))
                {
                    var pm = _prefixMatchResolver.FindMatches(tokens[startIdx].Text);
                    if (pm.Count == 1)
                        return (pm[0].Latex, startIdx + 1);
                }
                return ((string?)null, startIdx);
            });
            _collisionScores = CollisionScores.LoadEmbedded();
            // P28 (2026-05-22) : pipeline collision déclarée via détecteurs
            // composables. Chaque détecteur scanne le contexte et émet 0+
            // alts. La règle s'applique uniformément partout dans l'expression.
            _detectors = new ICollisionDetector[]
            {
                new SlurpFractionDetector(_parser, _flatEmitter),
                new SlurpSupSubDetector(_parser, _flatEmitter),
                new LetterSupSubDetector(_parser),
                new VecLetterDetector(),
                new DotVecDetector(),
                new TripleUpperDetector(),
                new VectorCoordsDetector(),
            };
        }

        public EngineResult Resolve(string source)
        {
            // Phase D-6 : si bascule activée, délègue au RewriteEngine.
            if (_rewriteEngine != null)
                return AdaptRewriteResult(_rewriteEngine.Resolve(source));

            if (string.IsNullOrEmpty(source)) return EngineResult.Empty;
            var tokens = _tokenizer.Tokenize(source);
            if (tokens.Count == 0) return EngineResult.Empty;

            // Pre-resolvers (Chantier 3, 2026-05-25) : multi-line align*/cases,
            // prefix-match popup as-you-type. Premier match wins. Si tous null,
            // fallback main loop ci-dessous.
            //
            // FuncDef : maintenant data-driven via data-v2/concepts/funcdef.yml
            // (= rule `{name:var} : {arg:var} -> {body}`). Migré 2026-05-24 P3.
            // Pattern matché en top-level via TryAllAnchorMatches — pas de
            // pre-resolver dédié. Cas 2-var en attente d'extension `{varlist}`.
            //
            // P30 (2026-05-22) : détection angle `^<word>` est faite au
            // tokenizer (= MergeLeadingCaretAngle). Plus de hardcoded ici.
            foreach (var preResolver in _preResolvers)
            {
                var pre = preResolver.TryResolve(tokens);
                if (pre != null) return pre;
            }

            int ti = 0;
            var operandLatex = new List<string>();
            var operandTokens = new List<List<Token>>(); // P24 : pour détection slurp
            var operandRuleIds = new List<string>();
            var opTokens = new List<Token>();
            bool anyAnchorMatch = false;
            string? firstAnchorRuleId = null;

            // P15.1 (2026-05-22) : collecte les candidats alternatifs sur la
            // 1ère ancre rencontrée pour les exposer comme Collisions.
            // Brief v5 §2.4 + ergo IDE-style (= "2 + voir plus" dans popup).
            var firstAnchorAlternatives = new List<ShapeMatch>();

            SkipSep(tokens, ref ti);
            while (ti < tokens.Count)
            {
                var allMatches = TryAllAnchorMatchesWithPartialFallback(tokens, ti);
                if (allMatches.Count > 0)
                {
                    // Best = full match (non-partial), puis span le plus large,
                    // puis nb slots remplis. User-request 2026-05-24 : popup
                    // guidée avec \square sur slots manquants si pattern
                    // partiellement reconnu.
                    allMatches.Sort((a, b) =>
                    {
                        int dPartial = (a.IsPartial ? 1 : 0).CompareTo(b.IsPartial ? 1 : 0);
                        if (dPartial != 0) return dPartial;
                        int dSpan = (b.End - b.Start).CompareTo(a.End - a.Start);
                        if (dSpan != 0) return dSpan;
                        return b.Slots.Count.CompareTo(a.Slots.Count);
                    });
                    var match = allMatches[0];
                    operandLatex.Add(_templateEmitter.Emit(match));
                    operandTokens.Add(new List<Token>()); // span ancre, pas slurp
                    operandRuleIds.Add(match.Rule.Id);
                    anyAnchorMatch = true;
                    if (firstAnchorRuleId == null)
                    {
                        firstAnchorRuleId = match.Rule.Id;
                        if (allMatches.Count > 1)
                            firstAnchorAlternatives.AddRange(allMatches);
                    }
                    ti = match.End;
                }
                else
                {
                    int tiBefore = ti;
                    var (flatLatex, newTi) = ParseFlatOperand(tokens, ti);
                    if (newTi == ti)
                    {
                        ti++;
                        continue;
                    }
                    // Capture tokens de l'operand pour détection slurp P24.
                    var bucket = new List<Token>();
                    for (int k = tiBefore; k < newTi; k++) bucket.Add(tokens[k]);
                    operandLatex.Add(flatLatex);
                    operandTokens.Add(bucket);
                    operandRuleIds.Add("");
                    ti = newTi;
                }

                SkipSep(tokens, ref ti);
                // Top-level operator : seulement ceux de tier ≥ Addsub
                // (= +, -, comp, rel, connecteurs). Sinon (= /, ^, _) doit
                // être consommé dans l'operand précédent.
                if (ti < tokens.Count
                    && (tokens[ti].Kind == TokenKind.Symbol
                        || tokens[ti].Kind == TokenKind.Glue)
                    && _vocab.Relations.TryGetValue(tokens[ti].Text, out var topRel)
                    && (int)topRel.Tier >= (int)MathCursor.Engine.Vocabulary.PrecedenceTier.Addsub)
                {
                    opTokens.Add(tokens[ti]);
                    ti++;
                    SkipSep(tokens, ref ti);
                }
                else
                {
                    break;
                }
            }

            if (operandLatex.Count == 0) return EngineResult.Empty;

            // Compose les operands + opTokens en LaTeX final.
            var sb = new StringBuilder();
            for (int i = 0; i < operandLatex.Count; i++)
            {
                if (i > 0)
                {
                    var op = opTokens[i - 1];
                    if (_vocab.Relations.TryGetValue(op.Text, out var r))
                    {
                        // P29 (2026-05-22) : Relation.Wrap = rend `<tex>{<arg>}`
                        // au lieu d'infixe binaire. Déclaratif via fr.yml,
                        // pas de `if` hardcoded dans l'engine.
                        if (r.Wrap)
                        {
                            sb.Append(' ').Append(r.Tex).Append('{')
                              .Append(operandLatex[i]).Append('}');
                            continue;
                        }
                        if (op.Text == "+" || op.Text == "-")
                            sb.Append(r.Tex);
                        else
                            sb.Append(' ').Append(r.Tex).Append(' ');
                    }
                    else
                    {
                        sb.Append(' ').Append(op.Text).Append(' ');
                    }
                }
                sb.Append(operandLatex[i]);
            }
            string finalLatex = sb.ToString();
            bool complete = !finalLatex.Contains(@"\square");
            string ruleId = anyAnchorMatch ? firstAnchorRuleId ?? "" : "";

            // P15.1 : expose les candidats alternatifs sur la 1ère ancre.
            var collisions = new List<EngineCandidate>(BuildCandidates(firstAnchorAlternatives));

            // P28 (2026-05-22) : pipeline collision déclarée. Chaque détecteur
            // scanne le contexte et émet ses alts. Pas de duplication entre
            // top-level et expression composée — c'est uniforme.
            var collisionCtx = new CollisionContext(
                operandTokens, operandLatex, opTokens, finalLatex, _vocab,
                _collisionScores);
            foreach (var detector in _detectors)
            {
                foreach (var cand in detector.Detect(collisionCtx))
                    collisions.Add(cand);
            }
            // P29 : trie par score décroissant pour que les alts les plus
            // pertinentes (= dot-vec, slurp) apparaissent avant les alts
            // simples (= vec sur lettre isolée).
            collisions.Sort((a, b) => b.Score.CompareTo(a.Score));

            return new EngineResult(
                topLatex: finalLatex,
                isComplete: complete,
                collisions: collisions,
                ruleId: ruleId);
        }

        // P28 (2026-05-22) : helpers de détecteurs extraits dans
        // MathCursor.Engine.Collision.Detectors/. MathEngine reste maigre.

        private IReadOnlyList<EngineCandidate> BuildCandidates(IReadOnlyList<ShapeMatch> matches)
        {
            if (matches == null || matches.Count == 0)
                return System.Array.Empty<EngineCandidate>();
            var candidates = new List<EngineCandidate>(matches.Count);
            foreach (var m in matches)
            {
                int span = m.End - m.Start;
                int filledSlots = m.Slots.Count;
                int score = span * 10 + filledSlots;
                candidates.Add(new EngineCandidate(
                    latex: _templateEmitter.Emit(m),
                    description: m.Rule.Id,
                    ruleId: m.Rule.Id,
                    score: score));
            }
            return candidates;
        }

        private List<ShapeMatch> TryAllAnchorMatches(IReadOnlyList<Token> tokens, int startIndex, bool allowPartial = false)
        {
            // Cherche toutes les règles qui matchent à startIndex. Retourne
            // brut (non trié) — le caller trie selon ses critères.
            var matches = new List<ShapeMatch>();
            foreach (var rule in _rules)
            {
                var m = _matcher.TryMatch(rule, tokens, startIndex, allowPartial: allowPartial);
                if (m != null) matches.Add(m);
            }
            return matches;
        }

        /// <summary>
        /// Tente un anchor match en priorité full, fallback partial (= pour
        /// la popup guidée avec <c>\square</c> sur les slots manquants).
        /// User-request 2026-05-24 « quand un truc comme somme ou limite est
        /// reperé/reconnu, je veux la popup avec les carrés jusqu'a la fin
        /// de la reconnaissance ».
        /// </summary>
        private List<ShapeMatch> TryAllAnchorMatchesWithPartialFallback(IReadOnlyList<Token> tokens, int startIndex)
        {
            var matches = TryAllAnchorMatches(tokens, startIndex);
            if (matches.Count > 0) return matches;
            return TryAllAnchorMatches(tokens, startIndex, allowPartial: true);
        }

        private (string Latex, int NewTi) ParseFlatOperand(IReadOnlyList<Token> tokens, int startIndex)
        {
            // Consomme un atom (Word/Number) ou un groupe (= parenthèses).
            // Stop sur infixe top-level ou EOF.
            if (startIndex >= tokens.Count) return (string.Empty, startIndex);
            int ti = startIndex;
            var bucket = new List<Token>();
            bool first = true;
            while (ti < tokens.Count)
            {
                var t = tokens[ti];
                if (t.Kind == TokenKind.Sep)
                {
                    // Cas `+ y2`, `- y2`, `<=> x+1`, `=> y2`, `= 1/2` : si le
                    // bucket contient juste un leading marker en attente
                    // d'opérande, on traverse le Sep pour absorber l'opérande
                    // qui suit. Sinon break normalement.
                    // Cf. StackParser.IsLeadingUnaryAllowed pour la liste.
                    // Skip aussi Sep("\n") qui est traité par le pre-pass
                    // multi-line en amont.
                    if (t.Text != "\n"
                        && bucket.Count == 1
                        && (bucket[0].Kind == TokenKind.Symbol || bucket[0].Kind == TokenKind.Glue)
                        && _parser.IsLeadingUnaryAllowed(bucket[0].Text))
                    {
                        ti++;
                        continue;
                    }
                    // Cas `cos x`, `sin t`, `\ln y` : si le bucket finit par
                    // une function known (= Word avec text reclassed en LaTeX
                    // cmd `\sin`/`\cos`/…), on traverse le Sep pour absorber
                    // l'argument à droite. Cf. user-report 2026-05-23 « Cos x ».
                    if (t.Text != "\n"
                        && bucket.Count >= 1
                        && bucket[bucket.Count - 1].Kind == TokenKind.Word
                        && _vocab.FunctionLatexValues.Contains(bucket[bucket.Count - 1].Text))
                    {
                        ti++;
                        continue;
                    }
                    break;
                }
                if (t.Kind == TokenKind.CloseDelim) break;
                // P23.4 (2026-05-22) : break uniquement sur ops de tier
                // ≥ Addsub (= composeurs top-level : +, -, =, =>, …). Les
                // ops inner (/, ^, _, *) restent dans l'operand pour que
                // `1/x` rende `\frac{1}{x}` via LatexEmitter.
                if (!first && (t.Kind == TokenKind.Symbol || t.Kind == TokenKind.Glue)
                    && _vocab.Relations.TryGetValue(t.Text, out var rel)
                    && (int)rel.Tier >= (int)MathCursor.Engine.Vocabulary.PrecedenceTier.Addsub)
                    break;
                if (t.Kind == TokenKind.OpenDelim)
                {
                    int depth = 0;
                    while (ti < tokens.Count)
                    {
                        bucket.Add(tokens[ti]);
                        if (tokens[ti].Kind == TokenKind.OpenDelim) depth++;
                        else if (tokens[ti].Kind == TokenKind.CloseDelim) depth--;
                        ti++;
                        if (depth == 0) break;
                    }
                    first = false;
                    continue;
                }
                bucket.Add(t);
                ti++;
                first = false;
            }
            if (bucket.Count == 0) return (string.Empty, startIndex);
            var ast = _parser.Parse(bucket);
            ast = ListCombinator.Promote(ast);
            return (_flatEmitter.Emit(ast), ti);
        }

        private static void SkipSep(IReadOnlyList<Token> tokens, ref int ti)
        {
            while (ti < tokens.Count
                   && tokens[ti].Kind == TokenKind.Sep
                   && tokens[ti].Text == " ")
                ti++;
        }

        // Pre-passes multi-line (align*/cases) et prefix-match : extraits en
        // Resolution/MultiLineBlockResolver et Resolution/PrefixMatchResolver
        // (Chantier 3, 2026-05-25). Cf. ADR 2026-05-25-Refactor-chantier3-preresolvers.

        /// <summary>Adapte un <see cref="RewriteResult"/> en
        /// <see cref="EngineResult"/> (= Phase D-6 bascule). Mapping :
        /// TopLatex direct, IsComplete = !Contains(\square), Alternatives
        /// → Collisions via emit du template.</summary>
        private EngineResult AdaptRewriteResult(RewriteResult r)
        {
            var collisions = new List<EngineCandidate>();
            foreach (var alt in r.Alternatives)
            {
                var altLatex = RewriteMatcher.ApplyTemplate(
                    alt.Rule.EmitTemplate, alt.Slots, alt.Lists, alt.Blocks);
                collisions.Add(new EngineCandidate(
                    latex: altLatex,
                    description: alt.Rule.Id,
                    ruleId: alt.Rule.Id,
                    score: alt.Span * 10));
            }
            return new EngineResult(
                topLatex: r.TopLatex,
                isComplete: !r.TopLatex.Contains(@"\square"),
                collisions: collisions,
                ruleId: r.RuleId);
        }

        // ─── Factory ──────────────────────────────────────────────────

        public static MathEngine BuildDefault(string localeCode = "fr")
        {
            // Phase D-6 (2026-05-26) — BASCULE FRANCHE : le RewriteEngine
            // est désormais le moteur par défaut. User-feedback :
            // « le legacy n'était pas stable sinon j'aurais pas migré .. donc
            // j'ai pas envie que ça traîne ».
            //
            // Le legacy main loop reste accessible via BuildDefaultLegacy
            // pour comparaison / regression tests temporaires.
            return BuildDefaultWithRewriteEngine(localeCode);
        }

        /// <summary>Construit un MathEngine qui utilise le legacy main
        /// loop (= ancien comportement Phase D-5). Conservé pour comparaison
        /// et pour les tests legacy non encore migrés vers la convention
        /// rewriting. À supprimer après stabilisation Phase D-6.</summary>
        public static MathEngine BuildDefaultLegacy(string localeCode = "fr")
        {
            var vocab = LocaleVocabulary.LoadEmbedded(localeCode);
            var concepts = RuleLoader.LoadAllEmbedded();
            var rules = new List<RuleSpec>();
            foreach (var c in concepts)
                rules.AddRange(c.Rules);
            return new MathEngine(vocab, rules);
        }

        /// <summary>Construit un MathEngine qui délègue au RewriteEngine
        /// (= bascule prod Phase D-6).</summary>
        public static MathEngine BuildDefaultWithRewriteEngine(string localeCode = "fr")
        {
            var vocab = LocaleVocabulary.LoadEmbedded(localeCode);
            var concepts = RuleLoader.LoadAllEmbedded();
            var ruleSpecs = new List<RuleSpec>();
            foreach (var c in concepts)
                ruleSpecs.AddRange(c.Rules);

            var rewriteRules = new List<RewriteRule>();
            rewriteRules.AddRange(PrimitiveRules.All);
            rewriteRules.AddRange(RewriteRuleLoader.LoadAllEmbedded(vocab));
            var rewriteEngine = new RewriteEngine(vocab, rewriteRules);

            return new MathEngine(vocab, ruleSpecs, rewriteEngine);
        }
    }
}
