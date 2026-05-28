using System;
using System.Collections.Generic;
using MathCursor.Engine.Ast;
using MathCursor.Engine.Emit;
using MathCursor.Engine.Parsing;
using MathCursor.Engine.Parsing.List;
using MathCursor.Engine.Tokenization;
using MathCursor.Engine.Vocabulary;

namespace MathCursor.Engine.Resolution
{
    /// <summary>
    /// Pre-resolver « bloc multi-ligne » (= align* / cases) : si le source
    /// contient des boundaries <c>\n</c> (= <see cref="Token"/> Sep("\n"))
    /// et un pattern align ou cases reconnaissable, on construit un
    /// <see cref="MultiLineBlockNode"/> et on emit directement.
    ///
    /// <para>Port direct du legacy <c>Parser.TryParseMultiLineBlock</c>.
    /// Cf. ADR 2026-05-23-Feat-engine-v2-multiline-port.</para>
    ///
    /// <para>Migration Chantier 3 (2026-05-25) : extrait de
    /// <c>MathEngine.Resolve</c> vers un module dédié, implémente
    /// <see cref="IPreResolver"/>.</para>
    /// </summary>
    public sealed class MultiLineBlockResolver : IPreResolver
    {
        private readonly LocaleVocabulary _vocab;
        private readonly StackParser _parser;
        private readonly LatexEmitter _emitter;

        public MultiLineBlockResolver(
            LocaleVocabulary vocab,
            StackParser parser,
            LatexEmitter emitter)
        {
            _vocab = vocab ?? throw new ArgumentNullException(nameof(vocab));
            _parser = parser ?? throw new ArgumentNullException(nameof(parser));
            _emitter = emitter ?? throw new ArgumentNullException(nameof(emitter));
        }

        public EngineResult? TryResolve(IReadOnlyList<Token> tokens)
        {
            var block = TryBuildBlock(tokens);
            if (block == null) return null;
            var latex = _emitter.Emit(block);
            return new EngineResult(
                topLatex: latex,
                isComplete: !latex.Contains(@"\square"),
                collisions: Array.Empty<EngineCandidate>(),
                ruleId: block.Mode == "cases" ? "multiline-cases" : "multiline-align");
        }

        /// <summary>
        /// Détecte un bloc multi-ligne (align* ou cases) et construit un
        /// <see cref="MultiLineBlockNode"/>. Retourne <c>null</c> si pas de
        /// pattern multi-ligne (= fallback single-line).
        /// </summary>
        private MultiLineBlockNode? TryBuildBlock(IReadOnlyList<Token> tokens)
        {
            var lineStarts = new List<int> { 0 };
            for (int i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Kind == TokenKind.Sep && tokens[i].Text == "\n")
                {
                    int next = i + 1;
                    if (next < tokens.Count) lineStarts.Add(next);
                }
            }
            if (lineStarts.Count < 2) return null;

            int firstLineEnd = lineStarts[1] - 1;
            if (IsCasesLineStart(tokens, lineStarts[0], firstLineEnd))
                return TryBuildCasesBlock(tokens, lineStarts);

            return TryBuildAlignBlock(tokens, lineStarts);
        }

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

        private MultiLineBlockNode? TryBuildAlignBlock(IReadOnlyList<Token> tokens, List<int> lineStarts)
        {
            var prefixes = new List<string> { "" };
            for (int li = 1; li < lineStarts.Count; li++)
            {
                int s = lineStarts[li];
                if (s >= tokens.Count) return null;
                var first = tokens[s];
                if (first.Kind == TokenKind.Sep && first.Text == " " && s + 1 < tokens.Count)
                {
                    s++;
                    first = tokens[s];
                }
                var prefix = MapAlignMarkerToLatex(first);
                if (prefix == null) return null;
                prefixes.Add(prefix);
            }

            var lines = new List<AstNode>();
            for (int li = 0; li < lineStarts.Count; li++)
            {
                int s = lineStarts[li];
                int e = (li + 1 < lineStarts.Count) ? lineStarts[li + 1] - 1 : tokens.Count;
                if (li > 0)
                {
                    while (s < e && tokens[s].Kind == TokenKind.Sep && tokens[s].Text == " ") s++;
                    s++; // skip marker
                }
                var lineAst = ParseTokenRange(tokens, s, e);
                lines.Add(lineAst ?? PlaceholderNode.Instance);
            }
            return new MultiLineBlockNode("align", lines, prefixes);
        }

        private static bool IsCasesLineStart(IReadOnlyList<Token> tokens, int lineStart, int lineEndExcl)
        {
            int s = lineStart;
            while (s < lineEndExcl && tokens[s].Kind == TokenKind.Sep && tokens[s].Text == " ") s++;
            if (s >= lineEndExcl) return false;
            if (tokens[s].Kind != TokenKind.OpenDelim || tokens[s].Text != "{") return false;
            for (int i = s + 1; i < lineEndExcl; i++)
            {
                if (tokens[i].Kind == TokenKind.CloseDelim && tokens[i].Text == "}") return false;
            }
            return true;
        }

        /// <summary>Mappe un token de marker align vers son préfixe LaTeX.
        /// Data-driven via le champ YAML <c>align_prefix</c> des relations.
        /// Retourne <c>null</c> si le token n'est pas une relation ou si
        /// elle n'a pas de <c>align_prefix</c> (= pas un marker align).</summary>
        private string? MapAlignMarkerToLatex(Token tok)
        {
            if (tok.Kind != TokenKind.Symbol && tok.Kind != TokenKind.Glue) return null;
            if (!_vocab.Relations.TryGetValue(tok.Text, out var rel)) return null;
            if (rel.AlignPrefix == null) return null;
            return rel.AlignPrefix.Length > 0 ? rel.AlignPrefix + " " : "";
        }

        private AstNode? ParseTokenRange(IReadOnlyList<Token> tokens, int start, int endExcl)
        {
            if (start >= endExcl) return null;
            var slice = new List<Token>(endExcl - start);
            for (int i = start; i < endExcl; i++) slice.Add(tokens[i]);
            var ast = _parser.Parse(slice);
            return ListCombinator.Promote(ast);
        }
    }
}
