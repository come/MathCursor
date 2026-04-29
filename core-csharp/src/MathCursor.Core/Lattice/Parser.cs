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
    public sealed class Parser
    {
        private readonly IReadOnlyList<LatticeEdge> _toks;
        private int _i;

        // Compteur d'intervalles en cours de parsing. Sert à distinguer un
        // bracket `[`/`]` qui OUVRE un intervalle (CanStartFactor accepté)
        // d'un bracket qui FERME un intervalle déjà ouvert (le parent doit
        // le consommer). Sans ce compteur, le `[` fermant de `[0,1[` serait
        // pris comme début d'un nouvel intervalle imbriqué.
        private int _intervalDepth;

        public Parser(IReadOnlyList<LatticeEdge> tokens)
        {
            // Filtrer les Space ici plutôt que d'imposer au caller — moins fragile.
            var filtered = new List<LatticeEdge>(tokens.Count);
            foreach (var t in tokens)
                if (t.Type != EdgeType.Space) filtered.Add(t);
            _toks = filtered;
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
                // `(-` = alias clavier de `\in`. Accepté en mult implicite
                // pour permettre `forall x (- R` → `\forall x \in R`.
                if (t.Value == "(-") return true;
            }
            return false;
        }

        private static Hole Hole(int idx) => new Hole(idx);

        // ---------------- Entry ----------------

        public AstNode Parse()
        {
            // Tente d'abord de reconnaître une définition de fonction au pattern
            // `Ident ':' Ident (',' Ident)* '->' body` (ADR 29-04). Si match,
            // on consomme et retourne FuncDef. Si pas match (échec à n'importe
            // quelle étape), _i est restauré et ParseRelation prend le relais.
            var fd = TryParseFuncDef();
            if (fd != null) return fd;
            var e = ParseRelation();
            return e ?? Hole(1);
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
            var lhs = ParseExpr();
            if (lhs == null) return null;
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
                if (IsOp("*", "/"))
                {
                    var op = Consume();
                    AstNode rhs;
                    // ADR 29-04 tight-as-grouping : si `/` tight, le rhs absorbe
                    // toute la chaîne tight (ex: AB/DC → \frac{AB}{DC}, pas \frac{AB}{D}*C).
                    // Pour `*` ou `/` non-tight, comportement inchangé : ParsePostfix.
                    if (op.Value == "/" && (op.Tight ?? false))
                        rhs = ParseTightChain() ?? (AstNode)Hole(1);
                    else
                        rhs = ParsePostfix() ?? (AstNode)Hole(1);
                    lhs = new Bin(op.Value, op.Tight ?? false, false, lhs, rhs);
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
                    var arg = ParseArgument() ?? (AstNode)Hole(1);
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
            // Notation clavier `(-` pour `\in` (alias de `dans`/`in`/`appartient`).
            // Le lexer l'émet comme Op multi-char.
            if (t.Type == EdgeType.Op && t.Value == "(-")
            {
                Consume();
                return new Const(" \\in ");
            }
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
                var arg = ParseArgument() ?? (AstNode)Hole(1);
                // Application de la règle générique "Number tight après nom =
                // exposant" au cas particulier d'une fonction. Le flow de
                // ParseArgument absorbe le Number ET le Group qui suit dans
                // un Bin(*, implicit, tight) — on remap ici en Sup(Func, Num)
                // quand le second opérande est un Group (parens explicites).
                // Sans parens (cos2x), on conserve la mult implicite arg-of.
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
