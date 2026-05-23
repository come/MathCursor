using System.Collections.Generic;
using MathCursor.Engine.Ast;
using MathCursor.Engine.Tokenization;
using MathCursor.Engine.Vocabulary;

namespace MathCursor.Engine.Parsing
{
    /// <summary>
    /// Parser à descente récursive O(n) — produit l'AST d'une expression plate
    /// (sans ancres). Les ancres sont reconnues en amont par
    /// <see cref="MathEngine.Resolve"/> via <see cref="Rules.ShapeMatcher"/> ;
    /// ce que voit StackParser, c'est uniquement le contenu d'un operand flat
    /// ou d'un slot d'ancre. Brief v4 §1.2 (algo passe-pile) a été remplacé
    /// par cette descente récursive en P11.4-6 pour rester lisible — la pile
    /// implicite est celle de la machine.
    ///
    /// <para>Dispatch :</para>
    /// <list type="bullet">
    ///   <item><b>opérande</b> (Word/Number) → atom ;</item>
    ///   <item><b>délimiteur ouvrant</b> → récursion via <see cref="ParseDelimitedGroup"/> ;</item>
    ///   <item><b>délimiteur fermant</b> → fin de récursion ;</item>
    ///   <item><b>infixe</b> → empile op + opérande pour précédence ultérieure
    ///     via <see cref="PrecedenceClimber"/> ;</item>
    ///   <item><b>séparateur</b> (,/;/whitespace) → boundary contextuelle ;</item>
    ///   <item><b>opérateur unaire en début</b> (<c>+</c>, <c>-</c>) →
    ///     <see cref="UnaryPrefixNode"/> via <see cref="ParseUnaryOperand"/>
    ///     (ADR 2026-05-23-Fix-engine-leading-unary-prefix).</item>
    /// </list>
    /// </summary>
    public sealed class StackParser
    {
        private readonly LocaleVocabulary _vocab;

        public StackParser(LocaleVocabulary vocab)
        {
            _vocab = vocab ?? throw new System.ArgumentNullException(nameof(vocab));
        }

        public AstNode? Parse(IReadOnlyList<Token> tokens)
        {
            if (tokens == null || tokens.Count == 0) return null;

            // Descente récursive : parser top-level + parens en récursif,
            // puis appliquer PrecedenceClimber à la séquence plate obtenue.
            // Les ancres sont reconnues en amont (MathEngine.Resolve) — ici
            // on ne voit que des operands flat.
            int i = 0;
            return ParseExpression(tokens, ref i, depth: 0);
        }

        // Parse une expression jusqu'à un séparateur top-level ou EOF, sans
        // dépasser un délimiteur fermant courant.
        private AstNode? ParseExpression(IReadOnlyList<Token> tokens, ref int i, int depth)
        {
            var operands = new List<AstNode>();
            var ops = new List<Relation>();

            while (i < tokens.Count)
            {
                var tok = tokens[i];

                if (tok.Kind == TokenKind.CloseDelim) break;
                // P13 (2026-05-22) : Sep whitespace internes au sous-flux du
                // ShapeMatcher ne sont pas des boundaries pour le parser AST
                // (= le ShapeMatcher a déjà décidé du slot, on parse le contenu).
                // Sep dans une liste délimitée (= virgule ou ';' interne à un
                // groupe) reste une boundary qu'on gère via "," et ";" text.
                if (tok.Kind == TokenKind.Sep)
                {
                    if (tok.Text == "," || tok.Text == ";") break;
                    i++; // skip whitespace Sep
                    continue;
                }

                if (tok.Kind == TokenKind.OpenDelim)
                {
                    EnsureImplicitMulIfNeeded(operands, ops);
                    i++;
                    var group = ParseDelimitedGroup(tokens, ref i, tok.Text, depth + 1);
                    operands.Add(group);
                    continue;
                }

                if (tok.Kind == TokenKind.Word || tok.Kind == TokenKind.Number)
                {
                    EnsureImplicitMulIfNeeded(operands, ops);
                    operands.Add(new AtomNode(tok.Text,
                        tok.Kind == TokenKind.Number ? "number" : "word"));
                    i++;
                    continue;
                }

                if (tok.Kind == TokenKind.Symbol || tok.Kind == TokenKind.Glue)
                {
                    // Glue traitée comme un infixe générique via Relations
                    // (= `=` comp, `->` rel, etc.). Les ancres ont leur propre
                    // shape qui consomme la glue spécifiquement en amont.
                    if (_vocab.Relations.TryGetValue(tok.Text, out var rel))
                    {
                        if (operands.Count == 0)
                        {
                            // Leading unary `+` ou `-` : whitelist explicite
                            // (sémantique math valide en préfixe). Consomme le
                            // prochain operand récursivement et l'encapsule dans
                            // un UnaryPrefixNode. Cf. ADR
                            // 2026-05-23-Fix-engine-leading-unary-prefix.
                            //
                            // Autres operators (=, *, /, <, >, etc.) : leur
                            // sémantique unaire n'a pas de sens math → skip
                            // (= robustesse historique, conservée).
                            if (IsLeadingUnaryAllowed(tok.Text))
                            {
                                i++;
                                var operand = ParseUnaryOperand(tokens, ref i, depth);
                                if (operand != null)
                                {
                                    operands.Add(new UnaryPrefixNode(tok.Text, operand));
                                }
                                continue;
                            }
                            i++;
                            continue;
                        }
                        ops.Add(rel);
                        i++;
                        continue;
                    }
                    // Symbol non listé → ignore (= robustesse, ne casse pas
                    // sur un caractère exotique).
                    i++;
                    continue;
                }

                // Fallback : avance.
                i++;
            }

            if (operands.Count == 0) return null;
            if (operands.Count == 1 && ops.Count == 0) return operands[0];

            // Si désaccord (= ops.Count ≠ operands.Count - 1) on coupe le surplus
            // d'opérateurs en fin pour rester robuste face à entrée incomplète.
            while (ops.Count >= operands.Count) ops.RemoveAt(ops.Count - 1);
            return PrecedenceClimber.Climb(operands, ops);
        }

        /// <summary>
        /// Brief v4 §4 : <c>(a b) = PRODUIT</c> (espace cosmétique). 2 opérandes
        /// adjacents sans op infixe entre → injecte un <c>\cdot</c> implicite
        /// au tier <c>Muldiv</c>.
        ///
        /// <para>P28 : utilise un sentinel <c>\cdotIM</c> pour distinguer
        /// le produit implicite (= rendu sans symbol, juxtaposition) du
        /// produit explicite (= `.` ou `*` → rendu avec espaces autour
        /// de <c>\cdot</c>).</para>
        /// </summary>
        private void EnsureImplicitMulIfNeeded(List<AstNode> operands, List<Relation> ops)
        {
            if (operands.Count > 0 && operands.Count > ops.Count)
            {
                ops.Add(new Relation("*", @"\cdotIM", PrecedenceTier.Muldiv));
            }
        }

        private AstNode ParseDelimitedGroup(
            IReadOnlyList<Token> tokens, ref int i, string openChar, int depth)
        {
            string expectedClose = openChar switch
            {
                "(" => ")", "[" => "]", "{" => "}",
                _   => "",
            };

            var items = new List<AstNode>();
            string? sep = null;

            while (i < tokens.Count)
            {
                var item = ParseExpression(tokens, ref i, depth);
                items.Add(item ?? PlaceholderNode.Instance);

                if (i >= tokens.Count) break;
                if (tokens[i].Kind == TokenKind.CloseDelim) break;

                if (tokens[i].Kind == TokenKind.Sep)
                {
                    sep = tokens[i].Text;
                    i++;
                    continue;
                }
                break;
            }

            // P20 (2026-05-22) : utilise le close réel tapé (= `]`, `)`, `}`)
            // pour préserver les intervalles half-open `[0,1)`.
            string actualClose = expectedClose;
            if (i < tokens.Count && tokens[i].Kind == TokenKind.CloseDelim)
            {
                actualClose = tokens[i].Text;
                i++;
            }

            // Résolution du construit : on enveloppe TOUJOURS dans un GroupNode
            // pour préserver les délimiteurs (= info nécessaire au rendu LaTeX
            // \left( vs \left[). Le body est un AtomNode/InfixNode si singleton,
            // un ListNode sinon (et le ListCombinator promoit en MatrixNode si
            // rowsep ;).
            if (items.Count == 1 && sep == null)
                return new GroupNode(openChar, actualClose, items[0]);

            return new GroupNode(openChar, actualClose, new ListNode(sep ?? "", items));
        }

        /// <summary>
        /// Whitelist des opérateurs valides en préfixe unaire au début d'une
        /// expression. Limité à <c>+</c> et <c>-</c> qui ont une sémantique
        /// math claire (signe). Les autres opérateurs (=, *, /, &lt;, &gt;,
        /// etc.) restent skipped en début (= robustesse, sémantique unaire
        /// non définie). Cf. ADR
        /// <c>2026-05-23-Fix-engine-leading-unary-prefix</c>.
        /// </summary>
        private static bool IsLeadingUnaryAllowed(string text)
            => text == "+" || text == "-";

        /// <summary>
        /// Parse l'opérande consommé par un <see cref="UnaryPrefixNode"/>.
        /// Identique à <see cref="ParseExpression"/> sauf qu'on s'arrête au
        /// premier opérateur de tier ≥ <see cref="PrecedenceTier.Addsub"/> :
        /// l'unary lie plus fort que <c>+/-</c> binaire (cf. <c>-y2+1</c>
        /// = <c>(-y²) + 1</c>, pas <c>-(y² + 1)</c>).
        /// </summary>
        private AstNode? ParseUnaryOperand(IReadOnlyList<Token> tokens, ref int i, int depth)
        {
            var operands = new List<AstNode>();
            var ops = new List<Relation>();

            while (i < tokens.Count)
            {
                var tok = tokens[i];

                if (tok.Kind == TokenKind.CloseDelim) break;
                if (tok.Kind == TokenKind.Sep)
                {
                    if (tok.Text == "," || tok.Text == ";") break;
                    i++;
                    continue;
                }

                if (tok.Kind == TokenKind.OpenDelim)
                {
                    EnsureImplicitMulIfNeeded(operands, ops);
                    i++;
                    operands.Add(ParseDelimitedGroup(tokens, ref i, tok.Text, depth + 1));
                    continue;
                }

                if (tok.Kind == TokenKind.Word || tok.Kind == TokenKind.Number)
                {
                    EnsureImplicitMulIfNeeded(operands, ops);
                    operands.Add(new AtomNode(tok.Text,
                        tok.Kind == TokenKind.Number ? "number" : "word"));
                    i++;
                    continue;
                }

                if (tok.Kind == TokenKind.Symbol || tok.Kind == TokenKind.Glue)
                {
                    if (_vocab.Relations.TryGetValue(tok.Text, out var rel))
                    {
                        // STOP sur Addsub (et plus haut) : referme l'unary,
                        // l'op sera traité par le parent.
                        if ((int)rel.Tier >= (int)PrecedenceTier.Addsub) break;
                        if (operands.Count == 0) { i++; continue; }
                        ops.Add(rel);
                        i++;
                        continue;
                    }
                    i++;
                    continue;
                }

                i++;
            }

            if (operands.Count == 0) return null;
            if (operands.Count == 1 && ops.Count == 0) return operands[0];
            while (ops.Count >= operands.Count) ops.RemoveAt(ops.Count - 1);
            return PrecedenceClimber.Climb(operands, ops);
        }
    }
}
