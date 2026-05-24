using System.Collections.Generic;
using System.Text;
using MathCursor.Engine.Ast;
using MathCursor.Engine.Collision;
using MathCursor.Engine.Collision.Detectors;
using MathCursor.Engine.Emit;
using MathCursor.Engine.Parsing;
using MathCursor.Engine.Parsing.List;
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
        private readonly Tokenizer _tokenizer;
        private readonly ShapeMatcher _matcher;
        private readonly TemplateEmitter _templateEmitter;
        private readonly StackParser _parser;
        private readonly LatexEmitter _flatEmitter;

        private readonly IReadOnlyList<ICollisionDetector> _detectors;
        private readonly CollisionScores _collisionScores;

        public MathEngine(LocaleVocabulary vocab, IReadOnlyList<RuleSpec> rules)
        {
            _vocab = vocab ?? throw new System.ArgumentNullException(nameof(vocab));
            _rules = rules ?? throw new System.ArgumentNullException(nameof(rules));
            _tokenizer = new Tokenizer(_vocab);
            _matcher = new ShapeMatcher(_vocab);
            _templateEmitter = new TemplateEmitter(_vocab);
            _parser = new StackParser(_vocab);
            _flatEmitter = new LatexEmitter();
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
            if (string.IsNullOrEmpty(source)) return EngineResult.Empty;
            var tokens = _tokenizer.Tokenize(source);
            if (tokens.Count == 0) return EngineResult.Empty;

            // Pre-pass multi-line (= align*/cases) : si le source contient
            // des boundaries `\n` (= Sep("\n") tokens) et un pattern align
            // ou cases reconnaissable → construit un MultiLineBlockNode et
            // emit directement. Sinon → loop top-level normal pour single-line.
            // Cf. ADR 2026-05-23-Feat-engine-v2-multiline-port.
            var multiLineBlock = TryBuildMultiLineBlock(tokens);
            if (multiLineBlock != null)
            {
                var multiLineLatex = _flatEmitter.Emit(multiLineBlock);
                return new EngineResult(
                    topLatex: multiLineLatex,
                    isComplete: !multiLineLatex.Contains(@"\square"),
                    collisions: System.Array.Empty<EngineCandidate>(),
                    ruleId: multiLineBlock.Mode == "cases" ? "multiline-cases" : "multiline-align");
            }

            // P30 (2026-05-22) : la détection angle `^<word>` est faite au
            // tokenizer (= MergeLeadingCaretAngle). Plus de hardcoded ici.

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
                var allMatches = TryAllAnchorMatches(tokens, ti);
                if (allMatches.Count > 0)
                {
                    // Best = span le plus large, puis nb slots remplis.
                    allMatches.Sort((a, b) =>
                    {
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

        private string RenderTokens(IReadOnlyList<Token> tokens)
        {
            if (tokens == null || tokens.Count == 0) return string.Empty;
            var ast = _parser.Parse(tokens);
            ast = ListCombinator.Promote(ast);
            return _flatEmitter.Emit(ast);
        }

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

        private List<ShapeMatch> TryAllAnchorMatches(IReadOnlyList<Token> tokens, int startIndex)
        {
            // Cherche toutes les règles qui matchent à startIndex. Retourne
            // brut (non trié) — le caller trie selon ses critères.
            var matches = new List<ShapeMatch>();
            foreach (var rule in _rules)
            {
                var m = _matcher.TryMatch(rule, tokens, startIndex);
                if (m != null) matches.Add(m);
            }
            return matches;
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
                        && Parsing.StackParser.IsLeadingUnaryAllowed(bucket[0].Text))
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

        // ─── Multi-line block (align*/cases) ──────────────────────────
        // Cf. ADR 2026-05-23-Feat-engine-v2-multiline-port. Port direct du
        // legacy Parser.TryParseMultiLineBlock.

        /// <summary>
        /// Détecte un bloc multi-ligne (align* ou cases) à partir du flux de
        /// tokens et construit un <see cref="MultiLineBlockNode"/>. Retourne
        /// <c>null</c> si pas de pattern multi-ligne (= fallback single-line).
        /// </summary>
        private MultiLineBlockNode? TryBuildMultiLineBlock(IReadOnlyList<Token> tokens)
        {
            // Identifier les indices où Sep("\n") apparaît → boundaries de ligne.
            var lineStarts = new List<int> { 0 };
            for (int i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Kind == TokenKind.Sep && tokens[i].Text == "\n")
                {
                    int next = i + 1;
                    if (next < tokens.Count) lineStarts.Add(next);
                }
            }
            if (lineStarts.Count < 2) return null; // single-line

            // Cases en priorité : si line[0] commence par `{ ` (open delim non
            // refermé dans la ligne) ET toutes les lignes commencent par `{ ` aussi.
            int firstLineEnd = lineStarts[1] - 1; // exclut le Sep("\n")
            if (IsCasesLineStart(tokens, lineStarts[0], firstLineEnd))
            {
                return TryBuildCasesBlock(tokens, lineStarts);
            }

            return TryBuildAlignBlock(tokens, lineStarts);
        }

        /// <summary>Cases : toutes les lignes doivent commencer par `{` non
        /// refermé. Skip le `{` initial de chaque ligne (porté implicitement
        /// dans <c>\begin{cases}</c>).</summary>
        private MultiLineBlockNode? TryBuildCasesBlock(IReadOnlyList<Token> tokens, List<int> lineStarts)
        {
            for (int li = 0; li < lineStarts.Count; li++)
            {
                int lineEnd = (li + 1 < lineStarts.Count) ? lineStarts[li + 1] - 1 : tokens.Count;
                if (!IsCasesLineStart(tokens, lineStarts[li], lineEnd)) return null;
            }

            var lines = new List<AstNode>();
            var prefixes = new List<string>();
            for (int li = 0; li < lineStarts.Count; li++)
            {
                int s = lineStarts[li] + 1; // skip `{`
                int e = (li + 1 < lineStarts.Count) ? lineStarts[li + 1] - 1 : tokens.Count;
                var lineAst = ParseTokenRange(tokens, s, e);
                lines.Add(lineAst ?? PlaceholderNode.Instance);
                prefixes.Add("");
            }
            return new MultiLineBlockNode("cases", lines, prefixes);
        }

        /// <summary>Align : ligne 0 quelconque, lignes 1+ doivent commencer
        /// par un marker align (= <c>=</c>, <c>&lt;=&gt;</c>, <c>=&gt;</c>,
        /// <c>&lt;=</c> et variants). Skip le marker initial de chaque ligne
        /// 2+ (porté dans <c>LinePrefix</c>).</summary>
        private MultiLineBlockNode? TryBuildAlignBlock(IReadOnlyList<Token> tokens, List<int> lineStarts)
        {
            var prefixes = new List<string> { "" };
            for (int li = 1; li < lineStarts.Count; li++)
            {
                int s = lineStarts[li];
                if (s >= tokens.Count) return null;
                var first = tokens[s];
                // Sauter un éventuel Sep(" ") interne en début de ligne
                if (first.Kind == TokenKind.Sep && first.Text == " " && s + 1 < tokens.Count)
                {
                    s++;
                    first = tokens[s];
                }
                var prefix = MapAlignMarkerToLatex(first);
                if (prefix == null) return null; // pas un align block
                prefixes.Add(prefix);
            }

            var lines = new List<AstNode>();
            for (int li = 0; li < lineStarts.Count; li++)
            {
                int s = lineStarts[li];
                int e = (li + 1 < lineStarts.Count) ? lineStarts[li + 1] - 1 : tokens.Count;
                if (li > 0)
                {
                    // skip Sep(" ") puis marker
                    while (s < e && tokens[s].Kind == TokenKind.Sep && tokens[s].Text == " ") s++;
                    s++; // skip marker
                }
                var lineAst = ParseTokenRange(tokens, s, e);
                lines.Add(lineAst ?? PlaceholderNode.Instance);
            }
            return new MultiLineBlockNode("align", lines, prefixes);
        }

        /// <summary>True si le 1er token (modulo Sep) à <paramref name="lineStart"/>
        /// est un <c>{</c> qui n'est PAS refermé par un <c>}</c> dans la
        /// même ligne (= marker système cases, vs. délimiteur de set).</summary>
        private static bool IsCasesLineStart(IReadOnlyList<Token> tokens, int lineStart, int lineEndExcl)
        {
            int s = lineStart;
            while (s < lineEndExcl && tokens[s].Kind == TokenKind.Sep && tokens[s].Text == " ") s++;
            if (s >= lineEndExcl) return false;
            if (tokens[s].Kind != TokenKind.OpenDelim || tokens[s].Text != "{") return false;
            // Vérifier qu'aucun `}` ne ferme le set sur cette ligne.
            for (int i = s + 1; i < lineEndExcl; i++)
            {
                if (tokens[i].Kind == TokenKind.CloseDelim && tokens[i].Text == "}") return false;
            }
            return true;
        }

        /// <summary>Mappe un token de marker align vers son préfixe LaTeX.
        /// Retourne <c>null</c> si pas un marker reconnu.</summary>
        private static string? MapAlignMarkerToLatex(Token tok)
        {
            if (tok.Kind != TokenKind.Symbol && tok.Kind != TokenKind.Glue) return null;
            switch (tok.Text)
            {
                case "=": return ""; // chaîne d'égalités, aligné via &
                case "<=>": case "<==>": case "⇔": case "↔": case "⟺":
                    return "\\Leftrightarrow ";
                case "=>": case "==>": case "⇒": case "⟹":
                    return "\\Rightarrow ";
                case "<=": case "<==": case "⇐": case "⟸":
                    return "\\Leftarrow ";
                default: return null;
            }
        }

        /// <summary>Parse une sous-séquence de tokens via StackParser. Utilisé
        /// par les helpers multi-line pour parser chaque ligne indépendamment.</summary>
        private AstNode? ParseTokenRange(IReadOnlyList<Token> tokens, int start, int endExcl)
        {
            if (start >= endExcl) return null;
            var slice = new List<Token>(endExcl - start);
            for (int i = start; i < endExcl; i++) slice.Add(tokens[i]);
            var ast = _parser.Parse(slice);
            return ListCombinator.Promote(ast);
        }

        // ─── Factory ──────────────────────────────────────────────────

        public static MathEngine BuildDefault(string localeCode = "fr")
        {
            var vocab = LocaleVocabulary.LoadEmbedded(localeCode);
            var concepts = RuleLoader.LoadAllEmbedded();
            var rules = new List<RuleSpec>();
            foreach (var c in concepts)
                rules.AddRange(c.Rules);
            return new MathEngine(vocab, rules);
        }
    }
}
