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
        private System.Func<IReadOnlyList<Token>, int, (string? latex, int newEnd)>? _tryAnchor;

        public StackParser(LocaleVocabulary vocab)
        {
            _vocab = vocab ?? throw new System.ArgumentNullException(nameof(vocab));
        }

        /// <summary>
        /// Injecte un callback de matching d'ancres (= règles YAML lim/sum/int/…)
        /// utilisable PARTOUT dans l'AST (top-level + dans les groupes). Le
        /// callback retourne <c>(latex pré-rendu, newEnd)</c> ou
        /// <c>(null, startIdx)</c> si pas de match. Injecté par
        /// <see cref="MathEngine"/> au ctor. Sans ce callback, les ancres ne
        /// sont pas reconnues à l'intérieur des groupes (= bug F user-report
        /// 2026-05-23 « somme dans matrice » = en réalité n'importe quelle
        /// ancre dans un sous-contexte délimité).
        /// </summary>
        public void SetAnchorMatcher(System.Func<IReadOnlyList<Token>, int, (string? latex, int newEnd)> tryAnchor)
        {
            _tryAnchor = tryAnchor;
        }

        public AstNode? Parse(IReadOnlyList<Token> tokens)
        {
            if (tokens == null || tokens.Count == 0) return null;

            // Descente récursive : parser top-level + parens en récursif,
            // puis appliquer PrecedenceClimber à la séquence plate obtenue.
            // Les ancres sont reconnues en amont (MathEngine.Resolve) — ici
            // on ne voit que des operands flat.
            int i = 0;
            return ParseExpression(tokens, ref i, depth: 0, parentOpen: null);
        }

        // Parse une expression jusqu'à un séparateur top-level ou EOF, sans
        // dépasser un délimiteur fermant courant. <paramref name="parentOpen"/>
        // = open-char du group englobant (`[`/`(`/`{`/null). Sert à détecter
        // les close-non-canoniques (= intervalle FR half-open `[0,1[`).
        private AstNode? ParseExpression(IReadOnlyList<Token> tokens, ref int i, int depth, string? parentOpen)
        {
            var operands = new List<AstNode>();
            var ops = new List<Relation>();

            while (i < tokens.Count)
            {
                var tok = tokens[i];

                if (tok.Kind == TokenKind.CloseDelim) break;
                // Intervalle FR half-open : si on est dans un group `[...` et
                // qu'on rencontre `[` (OpenDelim) ou `]` (CloseDelim déjà géré
                // au-dessus), c'est le close non-canonique. Break pour
                // laisser ParseDelimitedGroup consommer le delim.
                if (parentOpen != null && IsBracketCloseForInterval(parentOpen, tok)) break;

                // Anchor matching (= règles YAML lim/sum/int/cos/…). Tenté
                // PARTOUT dans l'AST, y compris à l'intérieur des groupes —
                // c'est le fix générique du bug F (user-report 2026-05-23
                // « somme dans matrice »). Le callback est injecté par
                // MathEngine ; null si pas configuré (= tests parser pur).
                if (_tryAnchor != null)
                {
                    var (anchorLatex, newEnd) = _tryAnchor(tokens, i);
                    if (anchorLatex != null && newEnd > i)
                    {
                        EnsureImplicitMulIfNeeded(operands, ops);
                        operands.Add(new AtomNode(anchorLatex, "anchor"));
                        i = newEnd;
                        continue;
                    }
                }

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
                            // Leading unary : (a) `+`/`-` signe math compact,
                            // (b) markers comp/rel `=`, `<=>`, `=>`, `<=` qui
                            // signifient "continuation de chaîne d'équivalences"
                            // (user-report 2026-05-23 « le = saute au commit »).
                            // Pour (b) on stocke `rel.Tex + " "` (= LaTeX cmd
                            // avec espace trailing pour ne pas coller au prochain
                            // token). Pour (a) on stocke `tok.Text` brut.
                            // Cf. ADR 2026-05-23-Fix-engine-leading-unary-prefix
                            // + 2026-05-23-Fix-engine-leading-relation-prefix.
                            if (IsLeadingUnaryAllowed(tok.Text))
                            {
                                string opRender = (tok.Text == "+" || tok.Text == "-")
                                    ? tok.Text
                                    : rel.Tex + " ";
                                i++;
                                // L'opérande de l'unary consomme tous les ops de
                                // tier STRICTEMENT supérieur au tier du leading
                                // marker (= conv math standard : `+y2+1` =
                                // `(+y²)+1` mais `<=> x+1` = `<=> (x+1)`). Le
                                // tier-1 sert de borne d'arrêt (= stop dès qu'on
                                // rencontre un op de tier ≤ tier du leading).
                                var operand = ParseUnaryOperand(tokens, ref i, depth, rel.Tier);
                                if (operand != null)
                                {
                                    operands.Add(new UnaryPrefixNode(opRender, operand));
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
                var item = ParseExpression(tokens, ref i, depth, parentOpen: openChar);
                items.Add(item ?? PlaceholderNode.Instance);

                if (i >= tokens.Count) break;
                if (tokens[i].Kind == TokenKind.CloseDelim) break;
                // Intervalle FR half-open : à l'intérieur d'un group `[`, un
                // second `[` (= OpenDelim) en position de close est en fait
                // la borne droite ouverte de l'intervalle (= `[0,1[`).
                // Cf. user-report 2026-05-23 `[0,1[` rend `[0,1[]]`.
                if (IsBracketCloseForInterval(openChar, tokens[i])) break;

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
            // Generic 2026-05-23 : accepte aussi `[` comme close-non-canonique
            // d'un group `[...` (= intervalle FR half-open `[0,1[`).
            string actualClose = expectedClose;
            if (i < tokens.Count)
            {
                if (tokens[i].Kind == TokenKind.CloseDelim)
                {
                    actualClose = tokens[i].Text;
                    i++;
                }
                else if (IsBracketCloseForInterval(openChar, tokens[i]))
                {
                    actualClose = tokens[i].Text;
                    i++;
                }
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
        /// True si <paramref name="tok"/> est un bracket char (`[` ou `]`) qui
        /// peut fermer un group ouvert par `[` (= intervalle FR half-open
        /// `[0,1[`). Le tokenizer ne distingue pas open/close pour `[`/`]`
        /// dans ce cas — c'est au parser de reconnaître le pattern.
        /// </summary>
        private static bool IsBracketCloseForInterval(string openChar, Token tok)
            => openChar == "["
               && (tok.Kind == TokenKind.OpenDelim || tok.Kind == TokenKind.CloseDelim)
               && (tok.Text == "[" || tok.Text == "]");

        /// <summary>
        /// True si <paramref name="text"/> est une relation déclarée comme
        /// préfixe unaire autorisé en début d'expression (= flag YAML
        /// <c>allow_leading: true</c>). Data-driven, pas de whitelist
        /// hardcoded. Cf. P2 migration 2026-05-24.
        /// </summary>
        public bool IsLeadingUnaryAllowed(string text)
        {
            return _vocab.Relations.TryGetValue(text, out var rel) && rel.AllowLeading;
        }

        /// <summary>
        /// Parse l'opérande consommé par un <see cref="UnaryPrefixNode"/>.
        /// Identique à <see cref="ParseExpression"/> sauf qu'on s'arrête au
        /// premier opérateur de tier &lt;= <paramref name="leadingTier"/> :
        /// l'opérande inclut tous les ops STRICTEMENT plus serrés que l'unary
        /// leading. Exemples :
        /// <list type="bullet">
        ///   <item><c>-y2+1</c> avec leading tier Addsub → operand=<c>y²</c>
        ///     (break sur <c>+</c> tier Addsub), résultat=<c>(-y²)+1</c>.</item>
        ///   <item><c>&lt;=&gt; x+1</c> avec leading tier Comp/Rel → operand=
        ///     <c>x+1</c> entier (le <c>+</c> Addsub est strictement plus
        ///     fort), résultat=<c>\Leftrightarrow x+1</c>.</item>
        /// </list>
        /// </summary>
        private AstNode? ParseUnaryOperand(IReadOnlyList<Token> tokens, ref int i, int depth, PrecedenceTier leadingTier)
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
                    if (tok.Text == "\n") break;
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
                        // STOP si tier op ≥ tier leading (en (int), où plus
                        // PETIT = plus FORT, cf. PrecedenceTier.cs). L'op
                        // revient au parent. Inclure si STRICTEMENT plus
                        // fort (= int strictement plus petit = lié plus serré).
                        if ((int)rel.Tier >= (int)leadingTier) break;
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
