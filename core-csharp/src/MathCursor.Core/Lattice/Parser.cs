using System;
using System.Collections.Generic;
using MathCursor.Core.Lattice.Ast;

namespace MathCursor.Core.Lattice
{
    /// <summary>
    /// Parser récursif descendant qui consomme une séquence d'arêtes (top-1 du
    /// lattice) et produit un AST avec Holes pour les slots manquants.
    ///
    /// Grammaire (cf. algorithm.md §4) :
    /// <code>
    /// Expr     = Term ((+|-) Term)*
    /// Term     = Postfix ((*|/) Postfix | implicit)*
    /// Postfix  = Primary (^ Argument | _ Argument)*
    /// Primary  = Unary | Atom | Group | Scope | Func
    /// Argument = Atom | Group | TightChain | Unary
    /// Body     = Postfix (TightOp Postfix | adjacent Postfix)*  // s'arrête au binop loose
    /// Scope    = Lim | Sum | Int | Sqrt | Frac | Vec
    /// </code>
    ///
    /// Les <see cref="LatticeEdge"/> de type Space doivent être filtrés AVANT
    /// d'entrer dans le parser (ils servent uniquement à calculer le drapeau
    /// Tight au lexer).
    /// </summary>
    [System.Obsolete("DEPRECATED P32 — replaced by MathCursor.Engine.Parsing.StackParser " +
        "(passe-pile O(n) déterministe brief v5). Kept as fallback. Do not extend.")]
    public sealed class Parser
    {
        private readonly IReadOnlyList<LatticeEdge> _toks;
        // _hasSpaceBefore[i] = true si l'arête Space précédait immédiatement le
        // i-e token filtré dans la liste brute d'origine. Sert au pattern
        // VectorCoordinates (cf. brief 2026-04-29) : le séparateur interne des
        // coords (espace = colonne / virgule = ligne) doit être détecté APRÈS
        // le filtrage des Spaces. Sans ce flag, on ne pourrait pas distinguer
        // `u(1 2)` (espace = colonne) de `u(12)` (un seul number).
        private readonly bool[] _hasSpaceBefore;
        // _hasLineBreakBefore[i] = true si l'arête LineBreak précédait
        // immédiatement le i-e token filtré dans la liste brute. Sert à
        // TryParseMultiLineBlock pour détecter les frontières de ligne dans
        // un source multi-ligne (cf. brief 30-04 multiline-systems).
        private readonly bool[] _hasLineBreakBefore;
        private int _i;

        /// <summary>
        /// Mode "tight chain absorbe aussi les opérateurs tight" — utilisé
        /// uniquement pour générer l'alternative désambig (cf. ADR
        /// 2026-04-30-Feat-tight-implicit-mult-grouping). Default `false` :
        /// la chaîne tight pour rhs de `/`, `^`, `_` consomme UNIQUEMENT la
        /// mult implicite (juxtaposition). En mode `true` : consomme aussi
        /// les ops tight (`+`, `-`, `*`) pour produire l'élargissement
        /// type `\frac{1}{x+1}` ou `x^{a+b}`.
        /// </summary>
        public bool TightExtendsToOps { get; set; } = false;

        /// <summary>
        /// Bascule l'associativité de `*` explicite par rapport à sa
        /// tightness. Default `false` :
        /// - `*` tight (collé) → gauche-assoc PEMDAS (rhs = ParsePostfix).
        ///   `1/2*3/4` → `\frac{(1/2)\cdot 3}{4}`.
        /// - `*` loose (espace) → droite-récursive (rhs = ParseTerm).
        ///   `1/2 * 3/4` → `\frac{1}{2} \cdot \frac{3}{4}`.
        /// Mode `true` (utilisé par AlternativeGenerator pour l'alt cascade) :
        /// les rôles sont inversés. Permet à l'utilisateur de switcher
        /// rapidement entre les deux groupements via la popup. Cf. ADR
        /// 2026-04-30-Feat-asterisk-tightness-associativity.
        /// </summary>
        public bool FlipAsteriskAssociativity { get; set; } = false;

        // Compteur d'intervalles en cours de parsing. Sert à distinguer un
        // bracket `[`/`]` qui OUVRE un intervalle (CanStartFactor accepté)
        // d'un bracket qui FERME un intervalle déjà ouvert (le parent doit
        // le consommer). Sans ce compteur, le `[` fermant de `[0,1[` serait
        // pris comme début d'un nouvel intervalle imbriqué.
        private int _intervalDepth;

        public Parser(IReadOnlyList<LatticeEdge> tokens)
        {
            // Filtrer les Space ET les LineBreak ici — moins fragile que d'imposer
            // au caller. En parallèle, on mémorise par token filtré :
            // - si une arête Space le précédait (`_hasSpaceBefore`)
            // - si une arête LineBreak le précédait (`_hasLineBreakBefore`)
            // Le LineBreak est un Space "fort" : il sert au pattern
            // MultiLineBlock pour identifier les frontières de ligne. Hors de
            // ce pattern, il est traité comme un Space ordinaire par le reste
            // du parser (les drapeaux Tight des Op sont déjà false sur des
            // ops adjacents à `\n` cf. IsTightOp côté Lexer).
            var filtered = new List<LatticeEdge>(tokens.Count);
            var spaceFlags = new List<bool>(tokens.Count);
            var lineBreakFlags = new List<bool>(tokens.Count);
            bool pendingSpace = false;
            bool pendingLineBreak = false;
            foreach (var t in tokens)
            {
                if (t.Type == EdgeType.Space) { pendingSpace = true; continue; }
                if (t.Type == EdgeType.LineBreak)
                {
                    pendingSpace = true;       // LineBreak est aussi un Space
                    pendingLineBreak = true;
                    continue;
                }
                filtered.Add(t);
                spaceFlags.Add(pendingSpace);
                lineBreakFlags.Add(pendingLineBreak);
                pendingSpace = false;
                pendingLineBreak = false;
            }
            _toks = filtered;
            _hasSpaceBefore = spaceFlags.ToArray();
            _hasLineBreakBefore = lineBreakFlags.ToArray();
            _i = 0;
        }

        // ---------------- Helpers ----------------

        private LatticeEdge? Peek(int off = 0)
            => (_i + off) < _toks.Count ? _toks[_i + off] : null;

        private LatticeEdge Consume() => _toks[_i++];

        private int PrevEnd() => _i > 0 ? _toks[_i - 1].End : -1;

        private bool IsOp(params string[] values)
        {
            var t = Peek();
            if (t == null || t.Type != EdgeType.Op) return false;
            foreach (var v in values) if (t.Value == v) return true;
            return false;
        }

        // Matche un keyword par sa valeur CANONIQUE (post-lookup dans Vocabulary).
        // Le Lexer émet la clé brute du dict (ex: "somme", "dans"), pas la
        // valeur canonique ("sum", "in"). Pour les sites où on veut tous les
        // alias d'un keyword (ex: `in` matché par "in", "appartient", "dans"),
        // on passe par cette méthode plutôt que d'énumérer chaque alias.
        private bool IsKwCanon(string canonical)
        {
            var t = Peek();
            if (t == null || t.Type != EdgeType.Keyword) return false;
            return Vocabulary.Keywords.TryGetValue(t.Value, out var c) && c == canonical;
        }

        private bool IsTightAdjacent()
        {
            var t = Peek();
            return t != null && t.Start == PrevEnd();
        }

        private bool CanStartFactor()
        {
            var t = Peek();
            if (t == null) return false;
            if (t.Type == EdgeType.Number || t.Type == EdgeType.Ident
                || t.Type == EdgeType.Greek || t.Type == EdgeType.Function
                || t.Type == EdgeType.Keyword) return true;
            if (t.Type == EdgeType.Op)
            {
                if (t.Value == "(") return true;
                // `[` / `]` peuvent ouvrir un intervalle, MAIS uniquement quand
                // on n'est PAS déjà dans un intervalle en cours de parsing
                // (sinon le bracket fermant serait pris comme nouveau primary).
                if ((t.Value == "[" || t.Value == "]") && _intervalDepth == 0) return true;
            }
            return false;
        }

        private static Hole Hole(int idx) => new Hole(idx);

        // ---------------- Entry ----------------

        public AstNode Parse()
        {
            // Tente d'abord MultiLineBlock (système, équivalences, chaîne `=`)
            // Cf. brief 30-04 multiline-systems-equivalences. Détecte un pattern
            // `expr LF marker expr (...)` et construit un MultiLineBlock unifié.
            var mb = TryParseMultiLineBlock();
            if (mb != null) return mb;

            // Tente ensuite de reconnaître une définition de fonction au pattern
            // `Ident ':' Ident (',' Ident)* '->' body` (ADR 29-04). Si match,
            // on consomme et retourne FuncDef. Si pas match (échec à n'importe
            // quelle étape), _i est restauré et ParseRelation prend le relais.
            var fd = TryParseFuncDef();
            if (fd != null) return fd;
            var e = ParseRelation();
            return e ?? Hole(1);
        }

        /// <summary>
        /// Détecte un pattern multi-ligne et construit un MultiLineBlock.
        /// Deux modes supportés :
        /// <list type="bullet">
        /// <item><b>align*</b> (Phase 1, brief 30-04 §10) : ligne 1 expression
        /// quelconque, lignes 2+ commencent par marker align (<c>&lt;=&gt;</c>,
        /// <c>=&gt;</c>, <c>&lt;=</c>, <c>=</c>).</item>
        /// <item><b>cases</b> (Phase 2, ADR 05-05) : TOUTES les lignes
        /// commencent par <c>{ </c> (avec espace obligatoire). Multi-ligne ou
        /// single-line. Pas de mix avec align (cf. brief 30-04 §3.4).</item>
        /// </list>
        /// <para>Stratégie spéculative : sauvegarde <c>_i</c>, tente le
        /// pattern, restaure en cas d'échec pour laisser ParseRelation
        /// traiter normalement la source comme une ligne unique.</para>
        /// </summary>
        private MultiLineBlock? TryParseMultiLineBlock()
        {
            int save = _i;
            // Trouver toutes les positions de LineBreak dans le flux des
            // tokens filtrés. Une frontière de ligne est un index i tel que
            // _hasLineBreakBefore[i] == true (= un LineBreak précédait le
            // i-e token dans le flux brut).
            var lineStarts = new List<int> { 0 };
            for (int i = 1; i < _toks.Count; i++)
            {
                if (_hasLineBreakBefore[i]) lineStarts.Add(i);
            }

            // Si la 1re ligne commence par marker cases (`{ `), on tente
            // cases en priorité — pas de mix avec align (cf. brief 30-04 §3.4).
            int firstLineEnd = (lineStarts.Count > 1) ? lineStarts[1] : _toks.Count;
            if (StartsWithCasesMarkerAt(lineStarts[0], firstLineEnd))
            {
                var cases = TryParseCasesBlock(lineStarts);
                if (cases != null) return cases;
                _i = save;
                return null;
            }

            // Single-line non-cases → pas de MultiLineBlock
            if (lineStarts.Count < 2) return null;

            // Multi-ligne align* (Phase 1)
            return TryParseAlignBlock(lineStarts, save);
        }

        /// <summary>
        /// Phase 1 align* : chaque ligne 2+ doit commencer par un marker align.
        /// Extrait de <see cref="TryParseMultiLineBlock"/> pour clarté.
        /// </summary>
        private MultiLineBlock? TryParseAlignBlock(List<int> lineStarts, int save)
        {
            // Pour chaque ligne 2+, vérifier qu'elle commence par un marqueur
            // align. Si UNE SEULE ligne 2+ ne commence pas par marqueur align,
            // ce n'est pas un align block.
            var prefixes = new List<string> { "" }; // Première ligne = pas de préfixe
            for (int li = 1; li < lineStarts.Count; li++)
            {
                var firstTokenOfLine = _toks[lineStarts[li]];
                var prefix = MapAlignMarkerToLatex(firstTokenOfLine);
                if (prefix == null) { _i = save; return null; }
                prefixes.Add(prefix);
            }

            // Parser chaque ligne via une sous-séquence (ParseSubrangeLine).
            // Pour les lignes 2+, on consomme le marqueur en tête (1 token)
            // puisqu'il est porté par LinePrefix, pas dans l'AST de la ligne.
            var lines = new List<AstNode>();
            for (int li = 0; li < lineStarts.Count; li++)
            {
                int s = lineStarts[li];
                int e = (li + 1 < lineStarts.Count) ? lineStarts[li + 1] : _toks.Count;
                if (li > 0)
                {
                    // Skip le marqueur en tête (1 token)
                    s++;
                }
                if (s >= e) { _i = save; return null; }
                var lineAst = ParseSubrangeLine(s, e);
                lines.Add(lineAst);
            }

            // Avancer _i au-delà de tous les tokens consommés
            _i = _toks.Count;
            return new MultiLineBlock("align", lines, prefixes);
        }

        /// <summary>
        /// Phase 2 cases : TOUTES les lignes doivent commencer par <c>{ </c>
        /// (espace obligatoire, cf. ADR 05-05). Single-line ou multi-line.
        /// Le <c>{</c> en tête est consommé (1 token) puisqu'il est implicite
        /// dans <c>\begin{cases}</c>. <c>LinePrefix</c> = <c>""</c> pour
        /// chaque ligne (pas de préfixe inline en mode cases).
        /// </summary>
        private MultiLineBlock? TryParseCasesBlock(List<int> lineStarts)
        {
            int save = _i;

            // Toutes les lignes doivent commencer par marker cases `{` (avec
            // ou sans espace après — cf. heuristique dans StartsWithCasesMarkerAt).
            for (int li = 0; li < lineStarts.Count; li++)
            {
                int lineEnd = (li + 1 < lineStarts.Count) ? lineStarts[li + 1] : _toks.Count;
                if (!StartsWithCasesMarkerAt(lineStarts[li], lineEnd)) { _i = save; return null; }
            }

            var lines = new List<AstNode>();
            var prefixes = new List<string>();
            for (int li = 0; li < lineStarts.Count; li++)
            {
                // Skip le `{` en tête (1 token, l'espace après est déjà filtrée)
                int s = lineStarts[li] + 1;
                int e = (li + 1 < lineStarts.Count) ? lineStarts[li + 1] : _toks.Count;
                if (s >= e) { _i = save; return null; }
                var lineAst = ParseSubrangeLine(s, e);
                lines.Add(lineAst);
                prefixes.Add("");
            }

            _i = _toks.Count;
            return new MultiLineBlock("cases", lines, prefixes);
        }

        /// <summary>
        /// True si le token à <paramref name="lineStart"/> est l'op <c>{</c>
        /// reconnu comme marker système (et non comme délimiteur de set).
        /// <para>
        /// Critère unique (cf. fix user 2026-05-11) : <b>présence d'un
        /// <c>}</c> fermant dans la ligne → set en extension</b> (ex.
        /// <c>{1, 2}</c>, <c>{x, y}</c>, <c>{x = 1, y = 2}</c> par
        /// compréhension). <b>Pas de <c>}</c> dans la ligne → cases</b>
        /// (système d'équations ouvert, l'utilisateur tape de gauche à
        /// droite et le <c>}</c> est implicite, ajouté par le commit).
        /// </para>
        /// <para>
        /// Couvre proprement <c>{ x+1=3</c> (avec espace) ET <c>{x+1=3</c>
        /// (sans espace) sans heuristique sur le contenu — l'utilisateur
        /// peut taper son système avec ou sans espace après le marker.
        /// </para>
        /// </summary>
        private bool StartsWithCasesMarkerAt(int lineStart, int lineEnd)
        {
            if (lineStart >= _toks.Count) return false;
            var tok = _toks[lineStart];
            if (tok.Type != EdgeType.Op || tok.Value != "{") return false;
            int next = lineStart + 1;
            if (next >= _toks.Count) return false;
            // Set fermé par `}` quelque part dans la ligne → pas un cases.
            int effectiveEnd = Math.Min(lineEnd, _toks.Count);
            for (int i = next; i < effectiveEnd; i++)
            {
                var t = _toks[i];
                if (t.Type == EdgeType.Op && t.Value == "}") return false;
            }
            return true;
        }

        /// <summary>
        /// Mappe un token de marqueur align vers son préfixe LaTeX. Retourne
        /// null si le token n'est pas un marqueur align reconnu.
        /// Marqueurs supportés : `=`, `<=>`, `=>`, `<=`, et leurs variants
        /// Unicode (`⇔`, `⇒`, `⇐`).
        /// </summary>
        private static string? MapAlignMarkerToLatex(LatticeEdge tok)
        {
            if (tok.Type != EdgeType.Op) return null;
            switch (tok.Value)
            {
                case "=": return ""; // Chaîne d'égalités : pas de préfixe (= aligné via &)
                case "<=>": case "<==>": case "⇔": case "↔": case "⟺":
                    return "\\Leftrightarrow ";
                case "=>": case "==>": case "⇒": case "⟹":
                    return "\\Rightarrow ";
                case "<==": case "⇐": case "⟸":
                    return "\\Leftarrow ";
                default: return null;
            }
        }

        /// <summary>
        /// Parse la sous-séquence [start, end) comme une expression complète
        /// (ParseRelation). Sauvegarde/restaure _i pour ne pas perturber le
        /// parser englobant.
        /// </summary>
        private AstNode ParseSubrangeLine(int start, int end)
        {
            if (start >= end) return Hole(1);
            int saveI = _i;
            var slice = new List<LatticeEdge>(end - start);
            var sliceSpaces = new List<bool>(end - start);
            var sliceLineBreaks = new List<bool>(end - start);
            for (int i = start; i < end; i++)
            {
                slice.Add(_toks[i]);
                sliceSpaces.Add(i == start ? false : _hasSpaceBefore[i]);
                sliceLineBreaks.Add(false); // pas de LineBreak interne dans une ligne
            }
            var sub = new Parser(slice, sliceSpaces, sliceLineBreaks);
            var ast = sub.ParseRelation() ?? (AstNode)Hole(1);
            _i = saveI;
            return ast;
        }

        /// <summary>
        /// Tente de reconnaître `Ident ':' Ident (',' Ident)* '->' body`.
        /// Stratégie spéculative : sauvegarde `_i` et restaure à null si le
        /// pattern ne matche pas, pour laisser ParseRelation traiter normalement.
        /// </summary>
        private AstNode? TryParseFuncDef()
        {
            int save = _i;
            if (Peek() is not { Type: EdgeType.Ident } nameTok) return null;
            Consume();
            if (!IsOp(":")) { _i = save; return null; }
            Consume();
            var vars = new List<AstNode>();
            if (Peek() is not { Type: EdgeType.Ident } v0) { _i = save; return null; }
            Consume();
            vars.Add(new Atom("ident", v0.Value));
            while (IsOp(","))
            {
                Consume();
                if (Peek() is not { Type: EdgeType.Ident } v) { _i = save; return null; }
                Consume();
                vars.Add(new Atom("ident", v.Value));
            }
            if (!IsOp("->")) { _i = save; return null; }
            Consume();
            var body = ParseTightChain() ?? (AstNode)Hole(1);
            return new FuncDef(nameTok.Value, vars, body);
        }

        // ---------------- Relation / Expr / Term / Postfix ----------------

        // Relation = Expr (RelOp Expr)*
        // RelOp ∈ {=, <, >, <=, >=, !=, <>}. Top-level UNIQUEMENT (à l'intérieur
        // d'un Group ou d'un Argument on reste sur Expr — convention math
        // classique : on n'écrit pas "lim x 0 (f(x) = 1)" mais "lim x 0 f(x)").
        private AstNode? ParseRelation()
        {
            AstNode lhs;
            if (IsRelOp())
            {
                // Zone qui ouvre par un opérateur de relation (ex. `<=> 4x = 2`
                // après un OMath déjà converti — destiné au cross-merge). LHS
                // implicite = Hole pour que le LaTeX rendu soit cohérent
                // (`\square \Leftrightarrow 4x = 2`) plutôt que tronqué à
                // `\square `.
                lhs = Hole(1);
            }
            else
            {
                var parsed = ParseExpr();
                if (parsed == null) return null;
                lhs = parsed;
            }
            while (IsRelOp())
            {
                var op = Consume();
                var rhs = ParseExpr() ?? (AstNode)Hole(1);
                lhs = new Bin(op.Value, op.Tight ?? false, false, lhs, rhs);
            }
            return lhs;
        }

        private bool IsRelOp()
        {
            var t = Peek();
            if (t == null || t.Type != EdgeType.Op) return false;
            switch (t.Value)
            {
                case "=":
                case "<":
                case ">":
                case "<=":
                case ">=":
                case "!=":
                case "<>":
                case "//":  // \parallel (∥) entre deux droites/vecteurs
                // Implication / équivalence (ADR 29-04)
                case "=>":
                case "==>":
                case "<=>":
                case "<==>":
                case "<==":
                case "⇒":
                case "⇔":
                case "⇐":
                case "↔":   // Word AutoCorrect FR pour <=>
                case "⟺":
                case "⟹":
                case "⟸":
                    return true;
                default:
                    return false;
            }
        }

        // Expr = Term ((+|-|union|inter|U_between_intervals) Term)*
        // L'union/intersection d'intervalles vit au même niveau que +/-,
        // priorité naturelle des opérations sur ensembles dans les programmes
        // lycée (parens explicites quand on combine avec autre chose).
        private AstNode? ParseExpr()
        {
            var lhs = ParseTerm();
            if (lhs == null) return null;
            while (true)
            {
                if (IsOp("+", "-"))
                {
                    var op = Consume();
                    var rhs = ParseTerm() ?? (AstNode)Hole(1);
                    lhs = new Bin(op.Value, op.Tight ?? false, false, lhs, rhs);
                    continue;
                }
                // Virgule au top-level (pas dans un intervalle) : opérateur
                // binaire qui rend littéralement `a,b`. Permet `V x,y (- R*`
                // → `\forall x,y \in \mathbb{R}^*`. À l'intérieur d'un
                // intervalle, la virgule reste séparatrice low/high (gérée
                // par ParsePrimary intervalle).
                if (IsOp(",") && _intervalDepth == 0)
                {
                    var op = Consume();
                    var rhs = ParseTerm() ?? (AstNode)Hole(1);
                    lhs = new Bin(",", false, false, lhs, rhs);
                    continue;
                }
                // Keyword union/inter en infix entre deux opérandes
                if (IsKwCanon("union") || IsKwCanon("inter"))
                {
                    var canon = IsKwCanon("union") ? "union" : "inter";
                    Consume();
                    var rhs = ParseTerm() ?? (AstNode)Hole(1);
                    lhs = new Bin(canon, false, false, lhs, rhs);
                    continue;
                }
                // Détection contextuelle : `U` (Ident isolé) entre deux intervalles
                // = union 100% (cf. ADR 2026-04-29-Feat-interval-union-intersection).
                // Ailleurs U reste variable. Critère strict : lhs finit par
                // Interval, prochain token = Ident "U", token suivant commence
                // par [ ou ] (= début d'intervalle).
                if (LhsEndsWithInterval(lhs)
                    && Peek() is { Type: EdgeType.Ident, Value: "U" }
                    && PeekAfterIsBracketStart())
                {
                    Consume(); // U
                    var rhs = ParseTerm() ?? (AstNode)Hole(1);
                    lhs = new Bin("union", false, false, lhs, rhs);
                    continue;
                }
                break;
            }
            return lhs;
        }

        // Helper : true si le node se termine par un Interval. Cas couverts :
        // direct (Interval), rhs d'un Bin (mult implicite, ex `forall x dans [0,1]`
        // se rend `Bin(*, ..., Interval)`). Sert à la détection contextuelle
        // « U entre intervalles = union ».
        private static bool LhsEndsWithInterval(AstNode node)
        {
            return node switch
            {
                Interval _ => true,
                Bin b => LhsEndsWithInterval(b.Rhs),
                _ => false,
            };
        }

        // Helper : true si le 2e token après la position courante commence un
        // intervalle (= bracket [ ou ]). Sert à la détection contextuelle U.
        private bool PeekAfterIsBracketStart()
        {
            var t = Peek(1);
            return t != null && t.Type == EdgeType.Op && (t.Value == "[" || t.Value == "]");
        }

        // Absorbe greedy les modificateurs tight d'un ensemble canonique :
        // `*`, `+`, `-` collés au keyword bbR/bbN/etc. La séquence (1 ou 2
        // signes) doit être terminée par un délim (espace, EOF, ponctuation,
        // op binaire mais sans opérande à droite) plutôt qu'un opérande
        // (number, ident…). Sinon c'est une expression arithmétique normale,
        // pas un modificateur typographique.
        //   ""  pas de modif → \mathbb{R}
        //   "^*" → \mathbb{R}^*
        //   "^+" / "^-" → \mathbb{R}^+ / \mathbb{R}^-
        //   "_+^*" → \mathbb{R}_+^* (combinaison * + ou + *, strictement positifs)
        //   "_-^*" → \mathbb{R}_-^* (* - ou - *, strictement négatifs)
        private string ParseSetModifiers()
        {
            var t1 = Peek();
            if (!IsModifierSign(t1)) return string.Empty;
            if (!IsTightAdjacent()) return string.Empty;

            var t2 = Peek(1);
            if (IsModifierSign(t2))
            {
                // 2 signes consécutifs candidats. Valides ssi t3 n'est pas
                // opérande (= la séquence se termine sur un délim).
                if (CanStartFactorAtToken(Peek(2))) return string.Empty;
                Consume(); Consume();
                return BuildModSuffix(t1!.Value, t2!.Value);
            }

            // 1 signe candidat. Valide ssi t2 n'est pas opérande.
            if (CanStartFactorAtToken(t2)) return string.Empty;
            Consume();
            return $"^{t1!.Value}";
        }

        private static bool IsModifierSign(LatticeEdge? t)
            => t != null && t.Type == EdgeType.Op
               && (t.Value == "*" || t.Value == "+" || t.Value == "-");

        // True si le token peut commencer un facteur (= opérande). Sert à
        // distinguer un modificateur d'ensemble (suivi de délim) d'un
        // opérateur arithmétique normal (suivi d'opérande).
        private static bool CanStartFactorAtToken(LatticeEdge? t)
        {
            if (t == null) return false;
            return t.Type == EdgeType.Number || t.Type == EdgeType.Ident
                || t.Type == EdgeType.Greek || t.Type == EdgeType.Function
                || t.Type == EdgeType.Keyword
                || (t.Type == EdgeType.Op && t.Value == "(");
        }

        private static string BuildModSuffix(string s1, string s2)
        {
            bool hasStar = s1 == "*" || s2 == "*";
            bool hasPlus = s1 == "+" || s2 == "+";
            bool hasMinus = s1 == "-" || s2 == "-";
            if (hasStar && hasPlus) return "_+^*";
            if (hasStar && hasMinus) return "_-^*";
            return $"^{{{s1}{s2}}}";
        }

        // Term = Postfix ((*|/) Postfix | implicitMult)*
        private AstNode? ParseTerm()
        {
            var lhs = ParsePostfix();
            if (lhs == null) return null;
            while (true)
            {
                if (IsOp("/"))
                {
                    var op = Consume();
                    // ADR 30-04 Feat-tight-implicit-mult-grouping :
                    // - `/` tight default → rhs absorbe la chaîne de MULT IMPLICITE
                    //   tight (juxtaposition) UNIQUEMENT, pas les ops `+ - *` tight.
                    //   Ex: AB/BC → \frac{AB}{BC} ; 1/x+1 → \frac{1}{x}+1 (PEMDAS).
                    // - Mode `TightExtendsToOps` (alt désambig) → ParseTightChain
                    //   absorbe TOUT le tight chain (ex: \frac{1}{x+1}).
                    AstNode rhs;
                    if (op.Tight ?? false)
                    {
                        rhs = (TightExtendsToOps
                            ? ParseTightChain()
                            : ParseTightImplicitMultChain()) ?? (AstNode)Hole(1);
                    }
                    else
                    {
                        rhs = ParsePostfix() ?? (AstNode)Hole(1);
                    }
                    lhs = new Bin("/", op.Tight ?? false, false, lhs, rhs);
                }
                else if (IsOp("*", "."))
                {
                    var op = Consume();
                    // ADR 30-04 Feat-asterisk-tightness-associativity :
                    // - `*`/`.` tight default → gauche-assoc PEMDAS (rhs = ParsePostfix).
                    //   `1/2*3/4` → \frac{1\cdot 2}{...}.
                    // - `*`/`.` loose default → droite-récursive (rhs = ParseTerm).
                    //   `1/2 * 3/4` → \frac{1}{2} \cdot \frac{3}{4} (sépare).
                    // Mode `FlipAsteriskAssociativity` inverse les rôles.
                    //
                    // `.` est conservé comme `Bin(".")` distinct de `Bin("*")`
                    // pour que LatexRenderer rende toujours `\cdot` (lecture
                    // littérale du point), indépendamment du setting culturel.
                    // Cf. ADR 30-04 Feat-dot-as-multiplier.
                    string opValue = op.Value;
                    bool tight = op.Tight ?? false;
                    bool useLeftAssoc = FlipAsteriskAssociativity ? !tight : tight;
                    if (useLeftAssoc)
                    {
                        var rhs = ParsePostfix() ?? (AstNode)Hole(1);
                        lhs = new Bin(opValue, tight, false, lhs, rhs);
                        // continue loop (gauche-associatif)
                    }
                    else
                    {
                        var rhs = ParseTerm() ?? (AstNode)Hole(1);
                        return new Bin(opValue, tight, false, lhs, rhs);
                    }
                }
                else if (CanStartFactor())
                {
                    // Stop avant union/inter (consommés au niveau ParseExpr
                    // comme opérateurs infix, cf. ADR 2026-04-29). Sans ce
                    // stop, la mult implicite avalerait le keyword en faisant
                    // Bin(*, lhs, Const("\\cup")).
                    if (IsKwCanon("union") || IsKwCanon("inter")) break;
                    // Stop avant `U` contextuel entre deux intervalles : la
                    // détection du U comme union se fait au niveau ParseExpr.
                    if (LhsEndsWithInterval(lhs)
                        && Peek() is { Type: EdgeType.Ident, Value: "U" }
                        && PeekAfterIsBracketStart()) break;

                    var tight = IsTightAdjacent();
                    var rhs = ParsePostfix();
                    if (rhs == null) break;
                    lhs = new Bin("*", tight, true, lhs, rhs);
                }
                else break;
            }
            return lhs;
        }

        // Postfix = Primary (^ Argument | _ Argument | NumberTight)*
        //
        // Règle "Number tight implicite = exposant" : si après un primary
        // non-Number on voit un Number adjacent tight (sans espace), on
        // l'interprète comme exposant (typo français : x² = x^2, cos²(x)).
        // Ne s'applique pas si le primary EST un Number — "23" reste "23",
        // pas "2 puissance 3".
        // C'est cette règle qui rend les exemples user :
        //   x2     → x²       (Atom + Number tight)
        //   cos2(x) → cos²(x)  (Func + Number tight, l'arg (x) suit comme
        //                       multiplication implicite tight = arg de la
        //                       fonction sublimée par la règle "Func arg")
        // Mais préserve :
        //   2x     → 2*x       (Number + atom : Number n'est pas le primary)
        //   23     → 23        (Number + Number : règle inactive)
        private AstNode? ParsePostfix()
        {
            var @base = ParsePrimary();
            if (@base == null) return null;
            while (true)
            {
                if (IsOp("^", "_"))
                {
                    var op = Consume();
                    // ADR 30-04 Feat-tight-implicit-mult-grouping : aligné sur `/`.
                    // L'argument de `^` / `_` consomme la chaîne MULT IMPLICITE tight
                    // par défaut (ex: x^2n → x^{2n}). Les ops tight (+, -, *) brisent
                    // la chaîne (ex: x^a+b → x^{a}+b en PEMDAS standard).
                    // Mode TightExtendsToOps (alt désambig) → x^{a+b}.
                    var arg = ParseSupSubArg() ?? (AstNode)Hole(1);
                    @base = op.Value == "^" ? (AstNode)new Sup(@base, arg) : new Sub(@base, arg);
                    continue;
                }
                if (Peek() is { Type: EdgeType.Number } numTok && IsTightAdjacent()
                    && !(@base is Atom a && a.Kind == "number"))
                {
                    Consume();
                    // isImplicit: true → la règle Number-tight a déclenché ce
                    // Sup, l'utilisateur n'a pas tapé `^` explicit. L'ambiguïté
                    // x² vs x_2 sera proposée par AlternativeGenerator.
                    @base = new Sup(@base, new Atom("number", numTok.Value), isImplicit: true);
                    continue;
                }
                // Notation limite à droite/gauche : "0+", "0-" collés au number,
                // mais SUIVIS d'un espace ou EOF (= pas d'opérande tight derrière).
                // Distinction critique avec "0+1" (addition tight) :
                //   IsTightAdjacent() : le signe est collé à @base (pas d'espace avant)
                //   sigTok.Tight == false : il y a un espace ou EOF après le signe
                // Permet `lim x 0+ f(x)` → \lim_{x → 0^+} f(x).
                if (Peek() is { Type: EdgeType.Op } sigTok
                    && (sigTok.Value == "+" || sigTok.Value == "-")
                    && IsTightAdjacent() && sigTok.Tight == false
                    && @base is Atom bA && bA.Kind == "number")
                {
                    Consume();
                    @base = new Sup(@base, new Atom("ident", sigTok.Value), isImplicit: true);
                    continue;
                }
                break;
            }
            return @base;
        }

        // Primary = Unary | Atom | Group | Scope | Func
        private AstNode? ParsePrimary()
        {
            var t = Peek();
            if (t == null) return null;

            // Pattern VectorCoordinates AVANT atom : `<ident>(...)` ou
            // `<ident_pair>(...)` ou même avec espace (`u (1 2)`). Tente de
            // matcher 4 critères stricts (§6.b du brief). En cas d'échec sur
            // n'importe quel critère, _i est restauré et on retombe sur le
            // flow normal (Atom + multiplication implicite avec Group).
            var vc = TryParseVectorCoordinates();
            if (vc != null) return vc;

            // Unary - ou +
            if (t.Type == EdgeType.Op && (t.Value == "-" || t.Value == "+"))
            {
                var op = Consume();
                var arg = ParsePrimary() ?? (AstNode)Hole(1);
                return new Unary(op.Value, arg);
            }
            // Group
            if (t.Type == EdgeType.Op && t.Value == "(")
            {
                Consume();
                var e = ParseExpr() ?? (AstNode)Hole(1);
                if (IsOp(")")) Consume();
                return new Group(e);
            }
            // (Anciennement : `(-` parsé comme `\in`. Retiré 2026-05-11,
            // cf. Vocabulary.cs.)
            // Intervalle français : [a,b] / [a,b[ / ]a,b] / ]a,b[
            // Le bracket d'ouverture peut être `[` (fermé) ou `]` (ouvert).
            // Idem pour le bracket fermant. Args manquants → Hole.
            if (t.Type == EdgeType.Op && (t.Value == "[" || t.Value == "]"))
            {
                bool leftClosed = (t.Value == "[");
                Consume();
                _intervalDepth++;
                var low = ParseExpr() ?? (AstNode)Hole(1);
                // La virgule sépare low/high. Si absente, on assume un
                // intervalle dégénéré (high = Hole) plutôt que d'échouer.
                if (IsOp(",")) Consume();
                var high = ParseExpr() ?? (AstNode)Hole(2);
                bool rightClosed = true;
                if (IsOp("]")) { rightClosed = true; Consume(); }
                else if (IsOp("[")) { rightClosed = false; Consume(); }
                // Sinon (pas de bracket fermant trouvé) on assume rightClosed=true.
                _intervalDepth--;
                return new Interval(low, high, leftClosed, rightClosed);
            }
            if (t.Type == EdgeType.Keyword) return ParseScope();
            if (t.Type == EdgeType.Function)
            {
                Consume();
                // Pattern dédié `Func Number Group` (puissance + appel) : si
                // les 2 tokens suivants sont Number + `(...)` (Group), on
                // consomme JUSTE ces 2 et on émet `Sup(Func(name, Group), Number)`.
                // Le reste reste pour le parser parent.
                //
                // Sans ce check précoce, `Cos2(x)+1` tombait dans ParseArgument
                // → ParseTightChain qui absorbait `+1` (le `+` est tight entre
                // `)` et `1`) et le Func gardait tout. Cf. ADR
                // 2026-04-30-Fix-trig-func-power-tight-arg.
                var t1 = Peek();
                if (t1 != null && t1.Type == EdgeType.Number)
                {
                    int save = _i;
                    var numTok = Consume();
                    var t2 = Peek();
                    if (t2 != null && t2.Type == EdgeType.Op && t2.Value == "("
                        && IsTightAdjacent())
                    {
                        var group = ParsePrimary();
                        if (group is Group g)
                            return new Sup(
                                new Func(t.Value, g),
                                new Atom("number", numTok.Value),
                                isImplicit: true);
                    }
                    _i = save; // pas le pattern → rollback
                }
                var arg = ParseArgument() ?? (AstNode)Hole(1);
                // Remap historique conservé : il fait fonctionner le cas
                // tolérant à l'espace `cos 2(x)` (où le check précoce ci-dessus
                // ne déclenche pas car `2` n'est pas tight au `cos`, mais
                // ParseTightChain regroupe quand même `2(x)` en Bin tight).
                if (arg is Bin bin && bin.Op == "*" && bin.Implicit && bin.Tight
                    && bin.Lhs is Atom lhsAtom && lhsAtom.Kind == "number"
                    && bin.Rhs is Group)
                {
                    return new Sup(new Func(t.Value, bin.Rhs), bin.Lhs, isImplicit: true);
                }
                return new Func(t.Value, arg);
            }
            if (t.Type == EdgeType.Number) { Consume(); return new Atom("number", t.Value); }
            if (t.Type == EdgeType.Ident)  { Consume(); return new Atom("ident",  t.Value); }
            if (t.Type == EdgeType.Greek)  { Consume(); return new Atom("greek",  t.Value); }
            return null;
        }

        // ---------------- Argument / TightChain / Body ----------------

        // Argument = Atom | Group | TightChain | Unary | Interval
        private AstNode? ParseArgument()
        {
            var t = Peek();
            if (t == null) return null;
            if (t.Type == EdgeType.Op && t.Value == "(") return ParsePrimary();
            // Intervalle accepté comme argument (set d'un quantif, etc.)
            // sans passer par CanStartFactor (qui exclut volontairement [ et ]
            // pour ne pas confondre avec les fermetures d'intervalle).
            if (t.Type == EdgeType.Op && (t.Value == "[" || t.Value == "]"))
                return ParsePrimary();
            if (t.Type == EdgeType.Op && (t.Value == "-" || t.Value == "+")) return ParseTightChain();
            if (CanStartFactor()) return ParseTightChain();
            return null;
        }

        // Argument de `^` / `_` : aligné sur `/` (ADR 30-04 tight-implicit-mult).
        // - `(expr)` ou `[expr]` : ParsePrimary (group / interval)
        // - `+/-` unaire ou facteur : ParseTightImplicitMultChain (default)
        //   ou ParseTightChain (mode alt désambig).
        // Différence avec `ParseArgument` (utilisé par les SCOPE keywords cos,
        // lim, sum, int, sqrt, frac) : ces derniers gardent ParseTightChain
        // pour préserver `frac a+b c` → `\frac{a+b}{c}` etc. Pour `^` et `_`
        // (opérateurs binaires), la convention math standard est plus
        // appropriée : `x^a+b` = `x^{a}+b` par défaut, l'élargissement à
        // `x^{a+b}` est accessible via cascade de désambig.
        private AstNode? ParseSupSubArg()
        {
            var t = Peek();
            if (t == null) return null;
            if (t.Type == EdgeType.Op && t.Value == "(") return ParsePrimary();
            if (t.Type == EdgeType.Op && (t.Value == "[" || t.Value == "]"))
                return ParsePrimary();
            System.Func<AstNode?> chainParser = TightExtendsToOps
                ? (System.Func<AstNode?>)ParseTightChain
                : ParseTightImplicitMultChain;
            if (t.Type == EdgeType.Op && (t.Value == "-" || t.Value == "+"))
                return chainParser();
            if (CanStartFactor()) return chainParser();
            return null;
        }

        // TightImplicitMultChain = Postfix (tightAdjacent Postfix)*
        // Variante restreinte de ParseTightChain : consomme UNIQUEMENT la
        // chaîne de mult implicite tight (juxtaposition), PAS les ops tight
        // `+ - * /`. Utilisée par défaut pour le rhs de `/` tight et pour
        // l'argument de `^` / `_`. Les ops tight cassent la chaîne en mode
        // par défaut (ADR 30-04). Le mode élargi (alt désambig) bascule vers
        // ParseTightChain qui consomme aussi les ops.
        private AstNode? ParseTightImplicitMultChain()
        {
            var lhs = ParsePostfix();
            if (lhs == null) return null;
            while (CanStartFactor() && IsTightAdjacent())
            {
                // Stop avant union/inter (consommés au niveau ParseExpr en
                // infix). Cohérent avec ParseTerm qui fait le même check.
                if (IsKwCanon("union") || IsKwCanon("inter")) break;
                var rhs = ParsePostfix();
                if (rhs == null) break;
                lhs = new Bin("*", true, true, lhs, rhs);
            }
            return lhs;
        }

        // TightChain = Postfix (TightOp Postfix | tightAdjacent Postfix)*
        private AstNode? ParseTightChain()
        {
            var lhs = ParsePostfix();
            if (lhs == null) return null;
            while (true)
            {
                var t = Peek();
                if (t == null) break;
                if (t.Type == EdgeType.Op
                    && (t.Value == "+" || t.Value == "-" || t.Value == "*" || t.Value == "/")
                    && t.Tight == true)
                {
                    var op = Consume();
                    var rhs = ParsePostfix() ?? (AstNode)Hole(1);
                    lhs = new Bin(op.Value, true, false, lhs, rhs);
                    continue;
                }
                if (CanStartFactor() && IsTightAdjacent())
                {
                    var rhs = ParsePostfix();
                    if (rhs == null) break;
                    lhs = new Bin("*", true, true, lhs, rhs);
                    continue;
                }
                break;
            }
            return lhs;
        }

        // Body = Postfix (TightOp Postfix | adjacent Postfix)*
        // S'arrête au premier opérateur binaire loose (cf. algorithm.md §4 « règle du body »).
        private AstNode? ParseBody()
        {
            var lhs = ParsePostfix();
            if (lhs == null) return null;
            while (true)
            {
                var t = Peek();
                if (t == null) break;
                // Stop au binop loose
                if (t.Type == EdgeType.Op
                    && (t.Value == "+" || t.Value == "-" || t.Value == "*" || t.Value == "/")
                    && t.Tight == false)
                    break;
                // Tight op
                if (t.Type == EdgeType.Op
                    && (t.Value == "+" || t.Value == "-" || t.Value == "*" || t.Value == "/")
                    && t.Tight == true)
                {
                    var op = Consume();
                    var rhs = ParsePostfix() ?? (AstNode)Hole(1);
                    lhs = new Bin(op.Value, true, false, lhs, rhs);
                    continue;
                }
                // Multiplication implicite (adjacence tight ou loose)
                if (CanStartFactor())
                {
                    var tight = IsTightAdjacent();
                    var rhs = ParsePostfix();
                    if (rhs == null) break;
                    lhs = new Bin("*", tight, true, lhs, rhs);
                    continue;
                }
                break;
            }
            return lhs;
        }

        // Atome simple uniquement, utilisé pour le var de Sum/Prod
        private AstNode? ParseAtomOnly()
        {
            var t = Peek();
            if (t == null) return null;
            if (t.Type == EdgeType.Number) { Consume(); return new Atom("number", t.Value); }
            if (t.Type == EdgeType.Ident)  { Consume(); return new Atom("ident",  t.Value); }
            if (t.Type == EdgeType.Greek)  { Consume(); return new Atom("greek",  t.Value); }
            return null;
        }

        // ---------------- VectorCoordinates (brief 2026-04-29) ----------------

        /// <summary>
        /// Tente de reconnaître le pattern <c>&lt;ident&gt;(...)</c> ou
        /// <c>&lt;ident&gt; (...)</c> où l'identifiant est 1 ou 2 lettres et
        /// les parens contiennent 2 ou 3 cellules avec séparateur HOMOGÈNE
        /// (que des espaces top-level → layout colonne, que des virgules
        /// top-level → layout ligne).
        ///
        /// Retourne null sur échec et restaure _i pour laisser le flow normal
        /// reprendre (Atom + multiplication implicite avec Group). C'est la
        /// stratégie « opt-in stricte » du §6.b du brief : aucun risque de
        /// régression sur f(x), (0,1), [0,1], etc.
        ///
        /// Critères STRICTS (tous obligatoires, sinon fallback) :
        /// <list type="number">
        /// <item>Identifiant 1 ou 2 lettres (Atom ident len=1, ou 2 idents
        ///   len=1 tight-adjacents). Pas de fonction reconnue (cos, sin, …)
        ///   en première position.</item>
        /// <item>Suivi de `(` immédiatement (avec ou sans espace avant — ne
        ///   change rien au layout, cf. spec §2.1).</item>
        /// <item>Contenu = exactement 2 ou 3 cellules.</item>
        /// <item>Séparateur interne homogène : que des espaces top-level OU
        ///   que des virgules top-level — pas de mélange.</item>
        /// </list>
        /// </summary>
        private VectorCoordinates? TryParseVectorCoordinates()
        {
            int save = _i;
            // Critère 1 : identifiant 1 ou 2 lettres en tête. On accepte aussi
            // le cas où le top-1 du Lexer est Function/Greek/Keyword au même
            // emplacement — pour `f(...)` la règle scan-source proposera l'alt
            // vec via AlternativeGenerator (cf. brief §3.1, ambig f-typique).
            // ICI on prend uniquement les Idents bruts pour ne pas écraser le
            // comportement function-call existant.
            var t0 = Peek();
            if (t0 == null || t0.Type != EdgeType.Ident) { _i = save; return null; }
            if (t0.Value.Length != 1 || !IsAlphaLetter(t0.Value[0])) { _i = save; return null; }

            string name = t0.Value;
            int afterIdent = _i + 1;

            // Cas 2 lettres : deuxième Ident len=1 immédiatement adjacent
            // (sans espace top-1 intermédiaire — pour préserver "u v" comme
            // produit, pas comme nom de vecteur).
            var t1 = Peek(1);
            if (t1 != null && t1.Type == EdgeType.Ident && t1.Value.Length == 1
                && IsAlphaLetter(t1.Value[0])
                && t1.Start == t0.End && !_hasSpaceBefore[_i + 1])
            {
                name += t1.Value;
                afterIdent = _i + 2;
            }

            // Critère 2 : `(` immédiatement après (avec ou sans espace).
            // Le lexer émet aussi `(-` comme Op multi-char (alias de \in), et
            // top-1 le préfère pour son coût négatif. Quand notre LHS est un
            // ident court (= candidat vecteur), `(-` doit être traité comme
            // `(` + unary minus pour permettre `v(-1 3)` → vec{v} pmatrix.
            int parenIdx = afterIdent;
            bool startsWithUnaryMinus = false;
            if (parenIdx >= _toks.Count || _toks[parenIdx].Type != EdgeType.Op)
            { _i = save; return null; }
            if (_toks[parenIdx].Value == "(-") startsWithUnaryMinus = true;
            else if (_toks[parenIdx].Value != "(") { _i = save; return null; }

            // Trouver le `)` fermant top-level. On scanne en gérant les parens
            // imbriquées pour calculer la profondeur 0 (séparateurs top-level).
            int closeIdx = FindMatchingClose(parenIdx);
            if (closeIdx < 0) { _i = save; return null; }

            // Critère 3 + 4 : split top-level en cellules par séparateur homogène.
            string layout;
            var cellRanges = SplitCells(parenIdx + 1, closeIdx, out layout);
            if (cellRanges == null) { _i = save; return null; }
            if (cellRanges.Count != 2 && cellRanges.Count != 3) { _i = save; return null; }

            // Layout=row + identifiant typique fonction (f, g, h) → on laisse
            // le comportement function-call par défaut. Le pattern coords sera
            // proposé en ALTERNATIVE via AlternativeGenerator.RuleVectorCoordsVsCall
            // pour donner le choix à l'utilisateur. Cf. brief §3.1 :
            // « 1 lettre minuscule typique fonction → fonction par défaut ».
            // En layout=column (séparateur espace), aucune fonction n'utilise
            // l'espace comme séparateur d'args → toujours coords, pas d'ambig.
            if (layout == "row" && IsFunctionTypicalIdent(name))
            { _i = save; return null; }

            // Pour le layout colonne, vérifier qu'aucune cellule ne commence par
            // un keyword scope (sin/cos/lim/frac/sum/…) NON parenthésé : ces
            // keywords consomment des args avec espaces, ce qui casserait le
            // découpage en cellules. Cf. spec §2.4.
            if (layout == "column")
            {
                foreach (var (s, e) in cellRanges)
                {
                    if (s == e) { _i = save; return null; } // cellule vide
                    var first = _toks[s];
                    if (first.Type == EdgeType.Keyword) { _i = save; return null; }
                    // Function suivie d'autre chose qu'un Group : `cos t`
                    // consomme `t` comme arg, casse le découpage. `cos(t)`
                    // est OK parce que l'arg est entièrement parenthésé.
                    if (first.Type == EdgeType.Function)
                    {
                        if (s + 1 >= e) { _i = save; return null; }
                        var next = _toks[s + 1];
                        if (next.Type != EdgeType.Op || next.Value != "(")
                        { _i = save; return null; }
                    }
                }
            }

            // Parser chaque cellule en sous-expression complète (ParseExpr
            // avec _i borné). Pour préserver l'isolation des cellules, on
            // appelle un parse de sous-séquence en sauvegardant/restaurant _i.
            var values = new List<AstNode>(cellRanges.Count);
            for (int idx = 0; idx < cellRanges.Count; idx++)
            {
                var (s, e) = cellRanges[idx];
                // Première cellule + `(-` consommé : injecter un `-` synthétique
                // en tête (l'op multi-char absorbait le `-` qui était logiquement
                // le signe unaire du premier nombre, ex: `v(-1 3)`).
                bool injectUnaryMinus = idx == 0 && startsWithUnaryMinus;
                var cell = ParseSubrange(s, e, injectUnaryMinus);
                values.Add(cell);
            }

            // Avancer _i au-delà du `)` consommé.
            _i = closeIdx + 1;

            bool isPoint = name.Length == 1 && char.IsUpper(name[0]);
            return new VectorCoordinates(name, values, layout, isPoint);
        }

        // Liste des idents 1 lettre considérés "typiques fonction" (f, g, h,
        // F, G, H). Ces noms par défaut → function call ; le pattern
        // VectorCoordinates est proposé en ALTERNATIVE par
        // AlternativeGenerator (RuleVectorCoordsVsCall). Cf. brief §3.1.
        private static bool IsFunctionTypicalIdent(string name)
            => name.Length == 1
               && (name == "f" || name == "g" || name == "h"
                   || name == "F" || name == "G" || name == "H");

        // True si le char est une lettre alphabétique (ASCII + accents FR).
        // Réplique d'IsAlphabetic du Lexer mais accessible côté parser sans
        // dépendance circulaire.
        private static bool IsAlphaLetter(char c)
        {
            if (c >= 'a' && c <= 'z') return true;
            if (c >= 'A' && c <= 'Z') return true;
            return c == 'é' || c == 'è' || c == 'à' || c == 'ù' || c == 'â'
                || c == 'ê' || c == 'î' || c == 'ô' || c == 'û' || c == 'ç';
        }

        /// <summary>
        /// Trouve l'index du `)` fermant qui matche la `(` à <paramref name="openIdx"/>.
        /// Gère les parens imbriquées et les brackets `[` / `]` (pour ne pas
        /// confondre `u([0,1] [2,3])` avec un séparateur space top-level).
        /// Retourne -1 si non trouvé jusqu'à la fin du flux.
        /// </summary>
        private int FindMatchingClose(int openIdx)
        {
            int depth = 1;
            for (int i = openIdx + 1; i < _toks.Count; i++)
            {
                var tok = _toks[i];
                if (tok.Type != EdgeType.Op) continue;
                if (tok.Value == "(") depth++;
                else if (tok.Value == ")") { depth--; if (depth == 0) return i; }
                else if (tok.Value == "[") depth++;
                else if (tok.Value == "]") depth--;
            }
            return -1;
        }

        /// <summary>
        /// Découpe la séquence [start, end) en cellules selon le séparateur
        /// HOMOGÈNE détecté au top-level (depth=0 dans les parens/brackets).
        /// Retourne null si mélange (espace ET virgule), ou si aucune cellule.
        /// Affecte <paramref name="layout"/> à "column" si séparateur=espace,
        /// "row" si séparateur=virgule.
        ///
        /// Critère séparateur=espace : deux tokens adjacents (depth=0) avec
        /// _hasSpaceBefore[i]=true sur le second. Critère séparateur=virgule :
        /// un token Op "," au depth=0.
        /// </summary>
        private List<(int start, int end)>? SplitCells(int start, int end, out string layout)
        {
            layout = string.Empty;
            if (start >= end) return null;

            var commaPositions = new List<int>();
            // "spaceBoundaries" = positions où un séparateur d'espace pur
            // (sans virgule attenante) existe au top-level. Pour `(1, 2)`,
            // l'espace après la virgule n'est PAS un séparateur — c'est juste
            // de la mise en forme. On filtre donc les boundaries dont le
            // token précédent (ou suivant immédiat sans espace) est `,` ou `;`.
            //
            // Note FR : `;` est traité comme alias de `,` pour les coordonnées
            // (cf. ADR 30-04 french-semicolon-coordinates + bug Etienne 30-04
            // « AB(1;2) doit proposer vec colonne »). Les Français écrivent
            // (1 ; 2) avec des points-virgules — la virgule est ambiguë avec
            // le séparateur décimal.
            var spaceBoundaries = new List<int>();
            int depth = 0;
            for (int i = start; i < end; i++)
            {
                var tok = _toks[i];
                if (tok.Type == EdgeType.Op)
                {
                    if (tok.Value == "(" || tok.Value == "[") { depth++; continue; }
                    if (tok.Value == ")" || tok.Value == "]") { depth--; continue; }
                    if (depth == 0 && (tok.Value == "," || tok.Value == ";"))
                    { commaPositions.Add(i); continue; }
                }
                if (depth == 0 && i > start && _hasSpaceBefore[i])
                {
                    // Ignore les espaces qui suivent immédiatement un `,`/`;` ou
                    // qui précèdent un `,`/`;` (ces espaces sont du whitespace de
                    // mise en forme, pas un séparateur de cellule colonne).
                    var prev = _toks[i - 1];
                    bool prevIsSep = prev.Type == EdgeType.Op
                        && (prev.Value == "," || prev.Value == ";");
                    bool nextIsSep = tok.Type == EdgeType.Op
                        && (tok.Value == "," || tok.Value == ";");
                    if (!prevIsSep && !nextIsSep)
                        spaceBoundaries.Add(i);
                }
            }

            // Critère 4 : séparateur HOMOGÈNE — soit que des virgules, soit que
            // des espaces. Mélange (ex: `u(1, 2 3)`) → rejet.
            bool hasComma = commaPositions.Count > 0;
            bool hasSpace = spaceBoundaries.Count > 0;
            if (hasComma && hasSpace) return null;

            var cells = new List<(int, int)>();
            if (hasComma)
            {
                layout = "row";
                int prev = start;
                foreach (var c in commaPositions)
                {
                    cells.Add((prev, c));
                    prev = c + 1;
                }
                cells.Add((prev, end));
            }
            else if (hasSpace)
            {
                layout = "column";
                int prev = start;
                foreach (var sp in spaceBoundaries)
                {
                    cells.Add((prev, sp));
                    prev = sp;
                }
                cells.Add((prev, end));
            }
            else
            {
                // Une seule cellule = pas de pattern coords valide (cardinalité ≠ 2/3).
                // Marqué row par défaut pour ne pas planter mais sera rejeté par
                // le check 2/3 cellules dans TryParseVectorCoordinates.
                layout = "row";
                cells.Add((start, end));
            }
            return cells;
        }

        /// <summary>
        /// Parse récursivement la sous-séquence [start, end) comme une
        /// expression complète (ParseExpr) et retourne l'AST. _i est
        /// sauvegardé/restauré pour ne pas perturber le parser englobant.
        /// Si la sous-séquence est vide (start == end) → Hole.
        ///
        /// <paramref name="injectUnaryMinus"/> = true → ajoute un `-` Op
        /// synthétique en tête de slice (cas `(-` collé pris comme
        /// `(` + unary minus). Vu en V1 sur `v(-1 3)`.
        /// </summary>
        private AstNode ParseSubrange(int start, int end, bool injectUnaryMinus = false)
        {
            if (start >= end && !injectUnaryMinus) return Hole(1);
            int saveI = _i;
            var slice = new List<LatticeEdge>(end - start + 1);
            var sliceSpaces = new List<bool>(end - start + 1);
            if (injectUnaryMinus)
            {
                // Synthétiser une arête Op "-" non-tight (cf. parser ParsePrimary
                // unary case). Position arbitraire (-1) puisque cette arête
                // n'est pas dans le DAG d'origine ; aucune règle parse n'utilise
                // les positions des tokens (seulement Tight).
                slice.Add(new LatticeEdge(-1, -1, EdgeType.Op, "-", 0, tight: false));
                sliceSpaces.Add(false);
            }
            for (int i = start; i < end; i++)
            {
                slice.Add(_toks[i]);
                // Premier token "vrai" de la cellule (après l'éventuelle
                // injection unary minus) : pas d'espace en tête.
                sliceSpaces.Add(i == start ? false : _hasSpaceBefore[i]);
            }
            var sub = new Parser(slice, sliceSpaces);
            var ast = sub.ParseExpr() ?? (AstNode)Hole(1);
            _i = saveI;
            return ast;
        }

        // Ctor interne utilisé par ParseSubrange : on a déjà la liste filtrée
        // (sans Spaces ni LineBreaks) et les tableaux parallèles de flags,
        // pas besoin de re-filtrer. Le caller doit fournir des LineBreakFlags
        // cohérents (typiquement tous false pour une sous-séquence VectorCoords
        // qui ne traverse pas de ¶).
        private Parser(List<LatticeEdge> filtered, List<bool> spaceFlags, List<bool>? lineBreakFlags = null)
        {
            _toks = filtered;
            _hasSpaceBefore = spaceFlags.ToArray();
            _hasLineBreakBefore = lineBreakFlags != null
                ? lineBreakFlags.ToArray()
                : new bool[filtered.Count];  // tous false par défaut
            _i = 0;
        }

        // ---------------- Scopes ----------------

        private AstNode ParseScope()
        {
            var kw = Consume();

            switch (kw.Value)
            {
                case "lim":
                {
                    var v = ParseArgument() ?? (AstNode)Hole(1);
                    if (IsOp("->")) Consume();
                    var target = ParseArgument() ?? (AstNode)Hole(2);
                    var body = ParseBody() ?? (AstNode)Hole(3);
                    return new Lim(v, target, body);
                }
                case "sum":
                case "somme":
                case "prod":
                case "produit":
                {
                    var symbol = (kw.Value == "sum" || kw.Value == "somme") ? "sum" : "prod";
                    var v = ParseAtomOnly() ?? (AstNode)Hole(1);
                    if (IsOp("=")) Consume();
                    var start = ParseArgument() ?? (AstNode)Hole(2);
                    var end = ParseArgument() ?? (AstNode)Hole(3);
                    var body = ParseBody() ?? (AstNode)Hole(4);
                    return new Sum(symbol, v, start, end, body);
                }
                case "int":
                case "integrale":
                case "intégrale":
                {
                    var low = ParseArgument() ?? (AstNode)Hole(1);
                    var high = ParseArgument() ?? (AstNode)Hole(2);
                    var body = ParseBody() ?? (AstNode)Hole(3);
                    return new Int(low, high, body);
                }
                case "racine":
                case "sqrt":
                case "rac":
                {
                    var arg = ParseArgument() ?? (AstNode)Hole(1);
                    return new Sqrt(arg);
                }
                case "frac":
                {
                    var num = ParseArgument() ?? (AstNode)Hole(1);
                    var den = ParseArgument() ?? (AstNode)Hole(2);
                    return new Frac(num, den);
                }
                case "vec":
                case "vecteur":
                {
                    var name = string.Empty;
                    while (Peek() is { Type: EdgeType.Ident } id)
                    {
                        name += id.Value;
                        Consume();
                    }
                    return new Vec(name.Length > 0 ? name : null);
                }
                case "angle":
                case "hat":
                case "widehat":
                case "chapeau":
                {
                    // Consomme les identifiants suivants (lettres collées,
                    // potentiellement entre parens).
                    var name = string.Empty;
                    bool openParen = false;
                    if (Peek() is { Type: EdgeType.Op, Value: "(" })
                    {
                        Consume();
                        openParen = true;
                    }
                    while (Peek() is { Type: EdgeType.Ident } id)
                    {
                        name += id.Value;
                        Consume();
                    }
                    if (openParen && Peek() is { Type: EdgeType.Op, Value: ")" })
                        Consume();
                    // 2 lettres exactement → on défaulte avec un placeholder
                    // pour inviter visuellement à compléter le 3ème point
                    // (cf. ADR 2026-05-11-Feat-angle-notation-caret-and-keyword).
                    // AlternativeGenerator proposera l'alt sans placeholder
                    // (= angle littéral 2 lettres) dans la popup.
                    bool placeholder = name.Length == 2;
                    return new Angle(name, hasPlaceholder: placeholder);
                }
                case "inf":
                case "infini":
                case "infinity":
                    return new Const("\\infty");
                case "forall":
                    // Trailing space pour la juxtaposition naturelle avec ce
                    // qui suit (forall + x = "\forall x"). Cf. ` \in ` qui
                    // utilise la même technique pour ses deux côtés.
                    return new Const("\\forall ");
                case "exists":
                    return new Const("\\exists ");
                case "in":
                case "appartient":
                case "dans":
                    // Relation : espaces dans la valeur pour qu'au render
                    // (Bin implicit concat) on ait "x \in R" et pas "x\inR".
                    return new Const(" \\in ");
                case "perp":
                case "perpendiculaire":
                    return new Const(" \\perp ");
                case "union":
                    // Cas dégénéré : `union` au début sans lhs (sinon ParseExpr
                    // l'a déjà consommé comme infix). Retour Const placeholder.
                    return new Const("\\cup");
                case "inter":
                case "intersection":
                    return new Const("\\cap");
                case "bbr":
                case "bbn":
                case "bbz":
                case "bbq":
                case "bbc":
                {
                    // Lettre canonique : 3e char du keyword (bbr → r → R uppercase).
                    // Le lexer émet la value lowercased (lookup case-insensitive),
                    // donc on remet en majuscule pour `\mathbb{R}`.
                    var letter = kw.Value.Substring(2).ToUpperInvariant();
                    var symbol = $"\\mathbb{{{letter}}}";
                    return new Const(symbol + ParseSetModifiers());
                }
            }

            // Fallback : keyword inconnu, on l'expose comme atome inconnu
            return new Atom("unknown", kw.Value);
        }
    }
}
