using System.Collections.Generic;
using MathCursor.Engine.Tokenization;
using MathCursor.Engine.Vocabulary;

namespace MathCursor.Engine.Rules
{
    /// <summary>
    /// Matche une <see cref="RuleSpec.Shape"/> contre une séquence de tokens.
    ///
    /// <para><b>P11 syntax</b> (legacy, conservée) : mots littéraux + <c>$slot</c>
    /// + <c>&lt;classe&gt;</c> + <c>?</c> + <c>(a|b)</c>.</para>
    ///
    /// <para><b>P12 syntax</b> (slots typés + quantificateurs) :</para>
    /// <list type="bullet">
    ///   <item><c>{var}</c> — 1 token <see cref="TokenKind.Word"/>.</item>
    ///   <item><c>{const}</c> — 1 token <see cref="TokenKind.Number"/>.</item>
    ///   <item><c>{expr}</c> — expression bornée par heuristique token-run
    ///     (cf. <see cref="MatchExpr"/>). 0 backtracking.</item>
    ///   <item><c>{name:type}</c> — slot typé nommé (= référence emit <c>$name</c>).</item>
    ///   <item><c>?</c> <c>*</c> <c>+</c> — quantificateurs regexp-like.</item>
    /// </list>
    ///
    /// <para>Cf. ADR <c>2026-05-22-Feat-typed-slots</c> (P12).</para>
    /// </summary>
    public sealed class ShapeMatcher
    {
        private readonly LocaleVocabulary _vocab;

        public ShapeMatcher(LocaleVocabulary vocab)
        {
            _vocab = vocab ?? throw new System.ArgumentNullException(nameof(vocab));
        }

        public ShapeMatch? TryMatch(RuleSpec rule, IReadOnlyList<Token> tokens, int startIndex,
            bool allowPartial = false)
        {
            var parts = ParseShape(rule.Shape);
            var slots = new Dictionary<string, List<Token>>();
            int typedSlotIndex = 0;
            int ti = startIndex;

            // Vérifie l'anchor (= 1er Literal) AVANT toute tolérance partielle.
            // Partial match autorisé UNIQUEMENT si la rule a un literal en tête
            // (= ancres `sum`, `lim`, `int`, …). Les rules commençant par un
            // typed slot (= `{name:var}` dans funcdef) ne produisent JAMAIS de
            // partial match : sinon tout Word standalone matcherait FuncDef
            // partiellement (= `1/x`, `a+b`, etc. interceptés).
            bool anchorMatched = false;
            bool hasLiteralAnchor = parts.Count > 0 && parts[0].Kind == ShapePartKind.Literal;

            for (int pi = 0; pi < parts.Count; pi++)
            {
                var part = parts[pi];
                bool isLast = pi == parts.Count - 1;

                // P13.2 : avant chaque part, skip les Sep (= whitespace boundaries).
                // L'espace est une boundary entre slots, pas dedans (cf. brief v5 §1).
                SkipSep(tokens, ref ti);

                if (part.Quantifier == ShapeQuantifier.Star
                    || part.Quantifier == ShapeQuantifier.Plus)
                {
                    int matchedCount = 0;
                    while (ti < tokens.Count)
                    {
                        SkipSep(tokens, ref ti);
                        int tiBefore = ti;
                        if (!TryMatchOne(part, tokens, ref ti, slots, ref typedSlotIndex, isLast))
                            break;
                        if (ti == tiBefore) break;
                        matchedCount++;
                    }
                    if (part.Quantifier == ShapeQuantifier.Plus && matchedCount == 0)
                    {
                        if (allowPartial && anchorMatched && hasLiteralAnchor)
                            return new ShapeMatch(rule, startIndex, ti, ToReadonlyTokens(slots), isPartial: true);
                        return null;
                    }
                    continue;
                }

                if (!TryMatchOne(part, tokens, ref ti, slots, ref typedSlotIndex, isLast))
                {
                    if (part.Optional || part.Quantifier == ShapeQuantifier.Optional) continue;
                    // Partial match : si l'anchor a déjà matché (= au moins le
                    // 1er literal), on retourne un match incomplet. Les slots
                    // manquants seront émis comme `\square` par TemplateEmitter.
                    if (allowPartial && anchorMatched && hasLiteralAnchor)
                        return new ShapeMatch(rule, startIndex, ti, ToReadonlyTokens(slots), isPartial: true);
                    return null;
                }
                // Le 1er part qui a matché est typiquement l'anchor literal.
                // À partir de là, on peut produire un partial match si le user
                // n'a pas encore tapé tous les args.
                if (!anchorMatched) anchorMatched = true;
            }
            return new ShapeMatch(rule, startIndex, ti, ToReadonlyTokens(slots));
        }

        private static void SkipSep(IReadOnlyList<Token> tokens, ref int ti)
        {
            while (ti < tokens.Count && tokens[ti].Kind == TokenKind.Sep
                   && tokens[ti].Text == " ")
                ti++;
        }

        // ─── Parsing de la shape ──────────────────────────────────────

        internal static IReadOnlyList<ShapePart> ParseShape(string shape)
        {
            var parts = new List<ShapePart>();
            var pieces = TokenizeShape(shape);
            foreach (var p in pieces)
            {
                parts.Add(ClassifyPiece(p));
            }
            return parts;
        }

        private static ShapePart ClassifyPiece(string piece)
        {
            // Extrait le quantificateur final (?, *, +).
            ShapeQuantifier quant = ShapeQuantifier.None;
            string body = piece;
            if (body.Length > 1)
            {
                char last = body[body.Length - 1];
                if (last == '?') { quant = ShapeQuantifier.Optional; body = body.Substring(0, body.Length - 1); }
                else if (last == '*') { quant = ShapeQuantifier.Star; body = body.Substring(0, body.Length - 1); }
                else if (last == '+') { quant = ShapeQuantifier.Plus; body = body.Substring(0, body.Length - 1); }
            }
            bool opt = quant == ShapeQuantifier.Optional;

            // P12 typed slot {name:type} ou {type} pur (= var/const/expr).
            if (body.Length >= 2 && body[0] == '{' && body[body.Length - 1] == '}')
            {
                var inner = body.Substring(1, body.Length - 2);
                string slotName, slotType;
                int colon = inner.IndexOf(':');
                if (colon >= 0)
                {
                    slotName = inner.Substring(0, colon).Trim();
                    slotType = inner.Substring(colon + 1).Trim();
                }
                else
                {
                    // {var} {const} {expr} → nom == type.
                    slotName = inner.Trim();
                    slotType = inner.Trim();
                }
                var typed = ParseSlotType(slotType);
                return ShapePart.TypedSlot(slotName, typed, opt, quant);
            }

            // P11 $slot legacy.
            if (body.StartsWith("$"))
            {
                return ShapePart.Slot(body.Substring(1), opt, quant);
            }

            // P11 <class>.
            if (body.Length >= 2 && body[0] == '<' && body[body.Length - 1] == '>')
            {
                return ShapePart.Class(body.Substring(1, body.Length - 2), opt, quant);
            }

            // P11 (a|b) alternative.
            if (body.Length >= 2 && body[0] == '(' && body[body.Length - 1] == ')')
            {
                var alts = body.Substring(1, body.Length - 2).Split('|');
                return ShapePart.Alt(alts, opt, quant);
            }

            // Literal.
            return ShapePart.Literal(body, opt, quant);
        }

        private static SlotType ParseSlotType(string raw)
        {
            // P13.3 (2026-05-22) : aliases sémantiques + {expr:T} tier précis.
            // Cf. brief v5 §2.
            var lower = raw.ToLowerInvariant();
            switch (lower)
            {
                case "var": return SlotType.Var;
                case "const": return SlotType.Const;
                case "bound": return SlotType.ExprAddsub; // alias addsub
                case "term": return SlotType.ExprMuldiv;  // alias muldiv
                case "body": return SlotType.Body;        // greedy-jusqu'à-ancre
                case "expr": return SlotType.Expr;
                case "expr:addsub": return SlotType.ExprAddsub;
                case "expr:muldiv": return SlotType.ExprMuldiv;
                case "expr:funcpow": return SlotType.ExprFuncpow;
                case "expr:comp": return SlotType.ExprComp;
                default: return SlotType.Expr;
            }
        }

        private static IReadOnlyList<string> TokenizeShape(string shape)
        {
            var result = new List<string>();
            int i = 0;
            while (i < shape.Length)
            {
                if (char.IsWhiteSpace(shape[i])) { i++; continue; }
                int s = i;
                if (shape[i] == '(')
                {
                    while (i < shape.Length && shape[i] != ')') i++;
                    if (i < shape.Length) i++;
                }
                else if (shape[i] == '<')
                {
                    while (i < shape.Length && shape[i] != '>') i++;
                    if (i < shape.Length) i++;
                }
                else if (shape[i] == '{')
                {
                    while (i < shape.Length && shape[i] != '}') i++;
                    if (i < shape.Length) i++;
                }
                else
                {
                    while (i < shape.Length && !char.IsWhiteSpace(shape[i]))
                    {
                        if ((shape[i] == '?' || shape[i] == '*' || shape[i] == '+') && i > s)
                        {
                            i++; break;
                        }
                        i++;
                    }
                }
                // Trailing quantificateur après ) > }.
                if (i < shape.Length && (shape[i] == '?' || shape[i] == '*' || shape[i] == '+'))
                {
                    i++;
                }
                result.Add(shape.Substring(s, i - s));
            }
            return result;
        }

        // ─── Matching d'une part ──────────────────────────────────────

        private bool TryMatchOne(
            ShapePart part, IReadOnlyList<Token> tokens, ref int ti,
            Dictionary<string, List<Token>> slots, ref int typedSlotIndex,
            bool isLast)
        {
            if (ti >= tokens.Count) return false;

            switch (part.Kind)
            {
                case ShapePartKind.Literal:
                    // Match direct littéral.
                    if (string.Equals(tokens[ti].Text, part.Value, System.StringComparison.OrdinalIgnoreCase))
                    {
                        ti++;
                        return true;
                    }
                    // P17 (2026-05-22) : match aussi via synonymes d'ancres
                    // (= "V" → "forall", "E" → "exists", "intégrale" → "int", …).
                    if (_vocab.Anchors.TryGetValue(tokens[ti].Text, out var canonical)
                        && string.Equals(canonical, part.Value, System.StringComparison.OrdinalIgnoreCase))
                    {
                        ti++;
                        return true;
                    }
                    return false;

                case ShapePartKind.Class:
                    {
                        var className = _vocab.FindClass(tokens[ti].Text);
                        if (string.Equals(className, part.Value, System.StringComparison.OrdinalIgnoreCase))
                        {
                            ti++;
                            return true;
                        }
                        return false;
                    }

                case ShapePartKind.TypedSlot:
                    return TryMatchTypedSlot(part, tokens, ref ti, slots, ref typedSlotIndex, isLast);

                case ShapePartKind.Slot:
                    return TryMatchLegacySlot(part, tokens, ref ti, slots, isLast);

                case ShapePartKind.Alt:
                    foreach (var alt in part.AltValues!)
                    {
                        if (string.Equals(tokens[ti].Text, alt, System.StringComparison.OrdinalIgnoreCase))
                        {
                            ti++;
                            return true;
                        }
                    }
                    return false;

                default:
                    return false;
            }
        }

        private bool TryMatchTypedSlot(
            ShapePart part, IReadOnlyList<Token> tokens, ref int ti,
            Dictionary<string, List<Token>> slots, ref int typedSlotIndex,
            bool isLast)
        {
            List<Token>? bucket = part.SlotType switch
            {
                SlotType.Var => MatchVar(tokens, ref ti),
                SlotType.Const => MatchConst(tokens, ref ti),
                SlotType.Body => MatchBody(tokens, ref ti, _vocab),
                SlotType.ExprAddsub => MatchExprPratt(tokens, ref ti, PrecedenceTier.Addsub, _vocab),
                SlotType.ExprMuldiv => MatchExprPratt(tokens, ref ti, PrecedenceTier.Muldiv, _vocab),
                SlotType.ExprFuncpow => MatchExprPratt(tokens, ref ti, PrecedenceTier.Funcpow, _vocab),
                SlotType.ExprComp => MatchExprPratt(tokens, ref ti, PrecedenceTier.Comp, _vocab),
                SlotType.Expr => MatchExprPratt(tokens, ref ti, PrecedenceTier.Iff, _vocab),
                _ => null,
            };
            if (bucket == null || bucket.Count == 0) return false;

            slots[part.Value] = bucket;
            typedSlotIndex++;
            slots["$" + typedSlotIndex.ToString()] = bucket;
            return true;
        }

        private static List<Token>? MatchVar(IReadOnlyList<Token> tokens, ref int ti)
        {
            if (ti >= tokens.Count) return null;
            if (tokens[ti].Kind != TokenKind.Word) return null;
            var bucket = new List<Token> { tokens[ti] };
            ti++;
            return bucket;
        }

        private static List<Token>? MatchConst(IReadOnlyList<Token> tokens, ref int ti)
        {
            if (ti >= tokens.Count) return null;
            if (tokens[ti].Kind != TokenKind.Number) return null;
            var bucket = new List<Token> { tokens[ti] };
            ti++;
            return bucket;
        }

        /// <summary>
        /// P13.3 (brief v5 §2) : Pratt-like avec borne par tier de précédence.
        /// <c>{expr:T}</c> consomme :
        /// <list type="bullet">
        ///   <item><c>Number</c> / <c>Word</c> / <c>OpenDelim</c> (= atomes
        ///     ou groupes) — sans contrainte de "op à gauche" car l'espace
        ///     est désormais une boundary explicite (Sep token).</item>
        ///   <item><c>Symbol</c> / <c>Glue</c> dont le tier ≤ <paramref name="maxTier"/>
        ///     — sinon stop (= laisse l'op pour le niveau du dessus).</item>
        ///   <item>Stop sur <c>Sep</c> (= whitespace boundary brief v5 §1).</item>
        ///   <item>Stop sur <c>CloseDelim</c> top-level.</item>
        /// </list>
        /// 0 backtracking, O(n).
        /// </summary>
        private static List<Token>? MatchExprPratt(
            IReadOnlyList<Token> tokens, ref int ti, PrecedenceTier maxTier,
            LocaleVocabulary vocab)
        {
            if (ti >= tokens.Count) return null;
            var bucket = new List<Token>();

            while (ti < tokens.Count)
            {
                var t = tokens[ti];

                if (t.Kind == TokenKind.Sep) break;
                if (t.Kind == TokenKind.CloseDelim) break;

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
                    continue;
                }

                if (t.Kind == TokenKind.Symbol || t.Kind == TokenKind.Glue)
                {
                    // Borne tier : si le symbol est une relation connue de tier
                    // > maxTier, on stoppe (= laisse au niveau supérieur).
                    if (vocab.Relations.TryGetValue(t.Text, out var rel)
                        && (int)rel.Tier > (int)maxTier)
                        break;
                    bucket.Add(t); ti++; continue;
                }

                if (t.Kind == TokenKind.Number || t.Kind == TokenKind.Word)
                {
                    bucket.Add(t); ti++; continue;
                }

                break;
            }

            if (bucket.Count == 0) return null;
            return bucket;
        }

        /// <summary>
        /// P13.4 (brief v5 §3) : {body} = greedy-jusqu'à-prochaine-ancre.
        /// Consomme tout jusqu'à : terminateur de cadre (Sep/CloseDelim/EOF)
        /// OU une ancre rencontrée comme NOUVEL opérande (= 2e ancre+,
        /// le 1er opérande peut être une ancre pour permettre l'imbrication
        /// <c>lim x 0 sum k 1 n a</c>).
        /// </summary>
        private static List<Token>? MatchBody(
            IReadOnlyList<Token> tokens, ref int ti, LocaleVocabulary vocab)
        {
            if (ti >= tokens.Count) return null;
            var bucket = new List<Token>();
            int operandCount = 0;
            // Sep INTERNES au body sont tolérés (= pas une boundary stricte,
            // le body est greedy par défaut). Brief v5 §3.

            while (ti < tokens.Count)
            {
                var t = tokens[ti];

                if (t.Kind == TokenKind.CloseDelim) break;

                if (t.Kind == TokenKind.Sep)
                {
                    // Sep top-level dans le body : on consomme sans stocker
                    // (= les Sep sont des frontières internes, pas du contenu).
                    ti++;
                    continue;
                }

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
                    operandCount++;
                    continue;
                }

                if (t.Kind == TokenKind.Word)
                {
                    // Détection ancre : Word qui est dans vocab.Anchors.Values.
                    bool isAnchor = IsAnchor(t.Text, vocab);
                    if (isAnchor && operandCount > 0)
                    {
                        // Nouvelle ancre comme opérande non-initial → stop.
                        break;
                    }
                    bucket.Add(t); ti++; operandCount++;
                    continue;
                }

                if (t.Kind == TokenKind.Number)
                {
                    bucket.Add(t); ti++; operandCount++;
                    continue;
                }

                if (t.Kind == TokenKind.Symbol || t.Kind == TokenKind.Glue)
                {
                    // Lookahead : si l'op est suivi (modulo Sep) d'une ancre
                    // et qu'on a déjà au moins 1 operand → l'op repart au
                    // niveau supérieur (= `f + lim g` = (f) + (lim g)).
                    int j = ti + 1;
                    while (j < tokens.Count && tokens[j].Kind == TokenKind.Sep) j++;
                    if (operandCount > 0 && j < tokens.Count
                        && tokens[j].Kind == TokenKind.Word
                        && IsAnchor(tokens[j].Text, vocab))
                        break;
                    bucket.Add(t); ti++;
                    continue;
                }

                break;
            }

            // Trim Sep et Symbol trailing du bucket pour rendu propre.
            while (bucket.Count > 0
                   && (bucket[bucket.Count - 1].Kind == TokenKind.Sep
                       || bucket[bucket.Count - 1].Kind == TokenKind.Symbol
                       || bucket[bucket.Count - 1].Kind == TokenKind.Glue))
                bucket.RemoveAt(bucket.Count - 1);

            if (bucket.Count == 0) return null;
            return bucket;
        }

        private static bool IsAnchor(string text, LocaleVocabulary vocab)
        {
            // Match valeurs canoniques (= "lim", "sum", "forall") OU keys
            // (= "V", "limite", "intégrale" qui sont des synonymes).
            foreach (var v in vocab.Anchors.Values)
                if (string.Equals(v, text, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            return vocab.Anchors.ContainsKey(text);
        }

        private bool TryMatchLegacySlot(
            ShapePart part, IReadOnlyList<Token> tokens, ref int ti,
            Dictionary<string, List<Token>> slots, bool isLast)
        {
            var bucket = new List<Token>();
            if (isLast)
            {
                while (ti < tokens.Count) { bucket.Add(tokens[ti]); ti++; }
            }
            else
            {
                if (tokens[ti].Kind == TokenKind.OpenDelim)
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
                }
                else
                {
                    bucket.Add(tokens[ti]);
                    ti++;
                }
            }
            if (bucket.Count == 0) return false;
            slots[part.Value] = bucket;
            return true;
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<Token>> ToReadonlyTokens(
            Dictionary<string, List<Token>> slots)
        {
            var dict = new Dictionary<string, IReadOnlyList<Token>>(slots.Count);
            foreach (var kv in slots) dict[kv.Key] = kv.Value;
            return dict;
        }
    }

    /// <summary>
    /// Types de slots typés P13 (= brief v5 §2). Alias sémantiques :
    /// <c>{bound}</c>=<c>ExprAddsub</c>, <c>{term}</c>=<c>ExprMuldiv</c>,
    /// <c>{body}</c>=<c>Body</c>.
    /// </summary>
    public enum SlotType
    {
        Var, Const,
        Expr,            // tier max (= toute la palette infixe autorisée)
        ExprAddsub,      // alias {bound} — stop sur op tier > addsub
        ExprMuldiv,      // alias {term} — stop sur op tier > muldiv
        ExprFuncpow,     // stop sur op tier > funcpow
        ExprComp,        // stop sur op tier > comp
        Body,            // greedy-jusqu'à-ancre (= brief v5 §3)
    }
    internal enum ShapePartKind { Literal, Class, Slot, Alt, TypedSlot }
    internal enum ShapeQuantifier { None, Optional, Star, Plus }

    internal sealed class ShapePart
    {
        public ShapePartKind Kind { get; }
        public string Value { get; }
        public bool Optional { get; }
        public IReadOnlyList<string>? AltValues { get; }
        public SlotType SlotType { get; }
        public ShapeQuantifier Quantifier { get; }

        private ShapePart(ShapePartKind kind, string value, bool optional,
            IReadOnlyList<string>? altValues = null,
            SlotType slotType = SlotType.Expr,
            ShapeQuantifier quantifier = ShapeQuantifier.None)
        {
            Kind = kind; Value = value; Optional = optional;
            AltValues = altValues; SlotType = slotType; Quantifier = quantifier;
        }

        public static ShapePart Literal(string v, bool opt, ShapeQuantifier q = ShapeQuantifier.None)
            => new ShapePart(ShapePartKind.Literal, v, opt, quantifier: q);
        public static ShapePart Class(string v, bool opt, ShapeQuantifier q = ShapeQuantifier.None)
            => new ShapePart(ShapePartKind.Class, v, opt, quantifier: q);
        public static ShapePart Slot(string v, bool opt, ShapeQuantifier q = ShapeQuantifier.None)
            => new ShapePart(ShapePartKind.Slot, v, opt, quantifier: q);
        public static ShapePart Alt(IReadOnlyList<string> alts, bool opt, ShapeQuantifier q = ShapeQuantifier.None)
            => new ShapePart(ShapePartKind.Alt, "alt", opt, alts, quantifier: q);
        public static ShapePart TypedSlot(string name, SlotType type, bool opt, ShapeQuantifier q = ShapeQuantifier.None)
            => new ShapePart(ShapePartKind.TypedSlot, name, opt, null, type, q);
    }

    /// <summary>
    /// Résultat d'un match de shape : la règle source + le span (start..end)
    /// + les slots remplis (= sous-séquences de tokens). Les slots typés
    /// sont accessibles à la fois par nom et par index positionnel
    /// (<c>$1</c>, <c>$2</c>, …) — utile pour les emits anonymes.
    /// </summary>
    public sealed class ShapeMatch
    {
        public RuleSpec Rule { get; }
        public int Start { get; }
        public int End { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<Token>> Slots { get; }

        /// <summary>
        /// True si le match est partiel (= certains slots non-optionnels
        /// sont vides, à émettre comme <c>\square</c> pour guider la
        /// saisie). User-request 2026-05-24 : « quand un truc comme somme
        /// ou limite est reperé/reconnu, je veux la popup avec les carrés
        /// jusqu'a la fin de la reconnaissance, pour aider et montrer les
        /// arguments en cours de frappe ». L'utilisateur tape <c>sum k 0</c>
        /// → popup affiche <c>\sum_{k=0}^{\square} \square</c>.
        /// </summary>
        public bool IsPartial { get; }

        public ShapeMatch(RuleSpec rule, int start, int end,
            IReadOnlyDictionary<string, IReadOnlyList<Token>> slots,
            bool isPartial = false)
        {
            Rule = rule; Start = start; End = end; Slots = slots;
            IsPartial = isPartial;
        }
    }
}
