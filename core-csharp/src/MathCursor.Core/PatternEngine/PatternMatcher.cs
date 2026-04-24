using System;
using System.Collections.Generic;
using System.Linq;

namespace MathCursor.Core.PatternEngine
{
    /// <summary>
    /// Résultat d'un match réussi : capture des slots + nombre de tokens consommés.
    /// </summary>
    public sealed class MatchResult
    {
        public Dictionary<string, List<CanonicalToken>> Slots { get; } = new();
        public Dictionary<string, SlotType> SlotTypes { get; } = new();
        public int ConsumedTokens { get; set; }
        /// <summary>True si <see cref="PatternMatcher.TryMatchPrefix"/> a accepté le
        /// match en consommant seulement une partie des éléments du pattern (les
        /// tokens d'entrée se sont épuisés en cours de route). Les slots non
        /// atteints ne sont pas dans <see cref="Slots"/>.</summary>
        public bool IsPartial { get; set; }
    }

    /// <summary>
    /// Matche une <see cref="PatternDef"/> contre une séquence de <see cref="CanonicalToken"/>.
    /// Algorithme : backtracking récursif, avec énumération des longueurs pour les slots EXPR.
    /// </summary>
    public static class PatternMatcher
    {
        // Limite arbitraire pour éviter les explosions combinatoires
        private const int MaxExprLength = 30;

        public static MatchResult? TryMatch(PatternDef pattern, List<CanonicalToken> tokens)
            => TryMatchCore(pattern, tokens, allowPartial: false);

        /// <summary>
        /// Variante "préfixe" : accepte que les tokens s'épuisent avant la fin du
        /// pattern. Renvoie un <see cref="MatchResult"/> avec <c>IsPartial=true</c>
        /// et les slots qu'on a pu capturer ; ceux qu'on n'a pas atteints sont
        /// absents du dictionnaire (le TemplateRenderer les rendra en <c>\ldots</c>).
        /// Retourne null si le pattern ne matche pas même en préfixe (ex. premier
        /// élément = LITERAL qui ne matche pas le premier token).
        /// </summary>
        public static MatchResult? TryMatchPrefix(PatternDef pattern, List<CanonicalToken> tokens)
            => TryMatchCore(pattern, tokens, allowPartial: true);

        private static MatchResult? TryMatchCore(PatternDef pattern, List<CanonicalToken> tokens, bool allowPartial)
        {
            var elements = MatchDslParser.Parse(pattern.Match);
            var slots = new Dictionary<string, List<CanonicalToken>>();
            if (TryMatchRec(elements, 0, tokens, 0, slots, allowPartial, out int consumed))
            {
                var slotTypes = new Dictionary<string, SlotType>();
                foreach (var el in elements)
                    if (el.Kind == ElementKind.Slot && !string.IsNullOrEmpty(el.CaptureName))
                        slotTypes[el.CaptureName] = el.Slot;

                // Validation sémantique : signe fort qu'un slot EXPR a été mal
                // découpé — soit il commence par un opérateur binaire isolé de type
                // "*", "/", "^", "_", "=", etc. (jamais valide en tête d'expression),
                // soit il commence par "+" ET contient aussi un "=" ailleurs (le "+"
                // aurait dû être un suffix, typique "lim x→0+ f(x)=..." où le "+"
                // devrait être le side d'une limite unilatérale).
                // "-" est toléré car souvent unaire (-2, -inf).
                foreach (var kv in slots)
                {
                    if (!slotTypes.TryGetValue(kv.Key, out var st)) continue;
                    if (st != SlotType.Expr && st != SlotType.ExprList && st != SlotType.EquationList) continue;
                    var content = kv.Value.Where(t => !t.IsConnector).ToList();
                    if (content.Count == 0) continue;
                    string firstRaw = content[0].Raw;
                    // Opérateurs jamais valides en tête
                    if (firstRaw == "*" || firstRaw == "/" || firstRaw == "^" || firstRaw == "_"
                        || firstRaw == "=" || firstRaw == "<" || firstRaw == ">")
                        return null;
                    // "+" en tête : autorisé seulement pour un unaire court (+inf, +1).
                    // Plus de 2 tokens → le "+" est presque toujours un opérateur binaire
                    // qui aurait dû rester hors du slot (ex. "+ f(x)" pour une limite
                    // où le "+" devrait être le side).
                    if (firstRaw == "+" && content.Count > 2)
                        return null;
                }

                var r = new MatchResult { ConsumedTokens = consumed };
                foreach (var kv in slots) r.Slots[kv.Key] = kv.Value;
                foreach (var kv in slotTypes) r.SlotTypes[kv.Key] = kv.Value;
                // Partial = au moins un slot nommé du pattern n'a pas été capturé.
                // Déterminé en comparant les captures obtenues aux slots définis
                // dans le pattern (Slot ou Alternative avec CaptureName).
                if (allowPartial)
                {
                    int expectedCaptures = 0;
                    foreach (var el in elements)
                        if ((el.Kind == ElementKind.Slot || el.Kind == ElementKind.Alternative)
                            && !string.IsNullOrEmpty(el.CaptureName))
                            expectedCaptures++;
                    r.IsPartial = slots.Count < expectedCaptures;
                }
                return r;
            }
            return null;
        }


        private static bool TryMatchRec(
            List<MatchElement> elems, int e,
            List<CanonicalToken> tokens, int t,
            Dictionary<string, List<CanonicalToken>> slots,
            bool allowPartial,
            out int finalT)
        {
            // Avancer sur les connectors tant que le pattern ne les demande pas
            // (lookup pour savoir si l'élément courant matche un connector)
            while (t < tokens.Count && tokens[t].IsConnector)
            {
                // Si l'élément courant peut matcher ce connector (littéral ou token name), on s'arrête
                if (e < elems.Count && CanMatchAtom(elems[e], tokens[t])) break;
                t++;
            }

            if (e >= elems.Count)
            {
                // Tous les éléments du pattern ont été consommés → match réussi,
                // même s'il reste des tokens (match partiel autorisé).
                finalT = t;
                return true;
            }

            // Mode préfixe : on s'est épuisé en tokens alors qu'il reste des éléments
            // à matcher. On accepte comme un succès partiel, les slots non atteints
            // resteront absents du dictionnaire et le TemplateRenderer émettra \ldots.
            if (allowPartial && t >= tokens.Count)
            {
                finalT = t;
                return true;
            }

            var el = elems[e];

            // Pour les éléments optionnels : on essaie d'abord de CONSOMMER (greedy),
            // et seulement si ça échoue on skip. Sinon on risque de laisser un LPAREN?
            // non consommé alors que le paren est bien présent dans l'input.
            switch (el.Kind)
            {
                case ElementKind.Space:
                    // SPACE : zero-width. Matche si le token à la position courante a
                    // HadSpaceBefore=true. Ne consomme pas de token (juste une assertion
                    // de frontière, comme \b en regex).
                    bool hasSpaceHere = t < tokens.Count && tokens[t].HadSpaceBefore;
                    if (hasSpaceHere && TryMatchRec(elems, e + 1, tokens, t, slots, allowPartial, out finalT)) return true;
                    if (el.Optional && TryMatchRec(elems, e + 1, tokens, t, slots, allowPartial, out finalT)) return true;
                    finalT = t;
                    return false;

                case ElementKind.LiteralToken:
                case ElementKind.LiteralWord:
                    if (t < tokens.Count && CanMatchAtom(el, tokens[t]))
                    {
                        if (TryMatchRec(elems, e + 1, tokens, t + 1, slots, allowPartial, out finalT)) return true;
                    }
                    if (el.Optional)
                    {
                        if (TryMatchRec(elems, e + 1, tokens, t, slots, allowPartial, out finalT)) return true;
                    }
                    finalT = t;
                    return false;

                case ElementKind.Alternative:
                    if (t < tokens.Count)
                    {
                        foreach (var alt in el.Alternatives)
                        {
                            // Matche par Name (token canonique en MAJ), par Generic
                            // ("NUMBER", "IDENT"...), OU par Raw case-insensitive
                            // (mot littéral comme "to" ou "a").
                            bool matched = tokens[t].Name == alt
                                || tokens[t].Generic == alt
                                || string.Equals(tokens[t].Raw, alt, StringComparison.OrdinalIgnoreCase);
                            if (matched)
                            {
                                slots[el.CaptureName] = new List<CanonicalToken> { tokens[t] };
                                if (TryMatchRec(elems, e + 1, tokens, t + 1, slots, allowPartial, out finalT)) return true;
                                slots.Remove(el.CaptureName);
                            }
                        }
                    }
                    if (el.Optional)
                    {
                        if (TryMatchRec(elems, e + 1, tokens, t, slots, allowPartial, out finalT)) return true;
                    }
                    finalT = t;
                    return false;

                case ElementKind.Slot:
                    if (TryMatchSlot(el, elems, e, tokens, t, slots, allowPartial, out finalT)) return true;
                    if (el.Optional)
                    {
                        if (TryMatchRec(elems, e + 1, tokens, t, slots, allowPartial, out finalT)) return true;
                    }
                    finalT = t;
                    return false;

                default:
                    finalT = t;
                    return false;
            }
        }

        private static bool CanMatchAtom(MatchElement el, CanonicalToken tok)
        {
            if (el.Kind == ElementKind.LiteralToken)
                return tok.Name == el.Literal;
            if (el.Kind == ElementKind.LiteralWord)
                return string.Equals(tok.Raw, el.Literal, StringComparison.OrdinalIgnoreCase);
            if (el.Kind == ElementKind.Alternative)
            {
                foreach (var alt in el.Alternatives)
                {
                    if (tok.Name == alt) return true;
                    if (tok.Generic == alt) return true;
                    if (string.Equals(tok.Raw, alt, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }

        private static bool TryMatchSlot(
            MatchElement el,
            List<MatchElement> elems, int e,
            List<CanonicalToken> tokens, int t,
            Dictionary<string, List<CanonicalToken>> slots,
            bool allowPartial,
            out int finalT)
        {
            switch (el.Slot)
            {
                case SlotType.Ident:
                case SlotType.Number:
                case SlotType.IdentSeq:
                case SlotType.IdentUpperPair:
                case SlotType.IdentUpperTriple:
                case SlotType.VxShort:
                case SlotType.CfShort:
                case SlotType.IdentBar:
                case SlotType.DfShort:
                case SlotType.CoordPoint:
                    if (t >= tokens.Count) { finalT = t; return false; }
                    var tok = tokens[t];
                    if (el.Slot == SlotType.Number && tok.Generic != "NUMBER") { finalT = t; return false; }
                    if (el.Slot == SlotType.IdentSeq && !IsSequenceIdent(tok.Raw)) { finalT = t; return false; }
                    if (el.Slot == SlotType.IdentUpperPair && !IsUpperPair(tok.Raw)) { finalT = t; return false; }
                    if (el.Slot == SlotType.IdentUpperTriple && !IsUpperTriple(tok.Raw)) { finalT = t; return false; }
                    if (el.Slot == SlotType.VxShort && !IsVxShort(tok.Raw)) { finalT = t; return false; }
                    if (el.Slot == SlotType.CfShort && !IsCfShort(tok.Raw)) { finalT = t; return false; }
                    if (el.Slot == SlotType.IdentBar && !IsIdentBar(tok.Raw)) { finalT = t; return false; }
                    if (el.Slot == SlotType.DfShort && !IsDfShort(tok.Raw)) { finalT = t; return false; }
                    if (el.Slot == SlotType.CoordPoint && !IsCoordPoint(tok.Raw)) { finalT = t; return false; }
                    slots[el.CaptureName] = new List<CanonicalToken> { tok };
                    if (TryMatchRec(elems, e + 1, tokens, t + 1, slots, allowPartial, out finalT)) return true;
                    slots.Remove(el.CaptureName);
                    finalT = t;
                    return false;

                case SlotType.Atom:
                case SlotType.SetAtom:
                    // Atom = groupe parenthésé/bracketé OU token simple non-opérateur,
                    // éventuellement suivi d'un appel de fonction "f(args)".
                    // SET_ATOM = comme Atom mais exige que le premier token soit soit
                    // un ensemble math (R/N/Z/Q/C) soit un bracket d'intervalle FR ([ ou ]).
                    if (t >= tokens.Count) { finalT = t; return false; }
                    if (el.Slot == SlotType.SetAtom)
                    {
                        var tk = tokens[t];
                        bool isSet = IsMathSetName(tk.Name);
                        bool isInterval = tk.Raw == "[" || tk.Raw == "]"
                            || tk.Name == "LBRACKET" || tk.Name == "RBRACKET";
                        if (!isSet && !isInterval) { finalT = t; return false; }
                    }
                    int atomEnd = FindAtomEnd(tokens, t);
                    if (atomEnd <= t) { finalT = t; return false; }
                    var atomTokens = tokens.GetRange(t, atomEnd - t);
                    slots[el.CaptureName] = atomTokens;
                    if (TryMatchRec(elems, e + 1, tokens, atomEnd, slots, allowPartial, out finalT)) return true;
                    slots.Remove(el.CaptureName);
                    finalT = t;
                    return false;

                case SlotType.ListParams:
                {
                    // LIST_PARAMS : 1-N identifiants, séparés par virgule OU espace,
                    // éventuellement enveloppés dans des parenthèses.
                    // Formes acceptées : "x", "x,y", "x y", "(x,y,z)".
                    if (t >= tokens.Count) { finalT = t; return false; }
                    int listEnd = FindListParamsEnd(tokens, t);
                    if (listEnd <= t) { finalT = t; return false; }
                    slots[el.CaptureName] = tokens.GetRange(t, listEnd - t);
                    if (TryMatchRec(elems, e + 1, tokens, listEnd, slots, allowPartial, out finalT)) return true;
                    slots.Remove(el.CaptureName);
                    finalT = t;
                    return false;
                }

                case SlotType.Interval:
                {
                    // INTERVAL : SET_ATOM, éventuellement suivi d'une chaîne
                    //   (U|∪|UNION|INTER|∩) SET_ATOM répétée.
                    // Formes : "R", "R*", "[0;1]", "[0;1]U[3;4]", "R-{0}".
                    if (t >= tokens.Count) { finalT = t; return false; }
                    var firstTk = tokens[t];
                    bool firstIsSet = IsMathSetName(firstTk.Name);
                    bool firstIsBracket = firstTk.Raw == "[" || firstTk.Raw == "]"
                        || firstTk.Name == "LBRACKET" || firstTk.Name == "RBRACKET";
                    if (!firstIsSet && !firstIsBracket) { finalT = t; return false; }
                    int cursor = FindAtomEnd(tokens, t);
                    if (cursor <= t) { finalT = t; return false; }
                    // Chaîne d'unions / intersections
                    while (cursor < tokens.Count && IsIntervalConnector(tokens[cursor]))
                    {
                        if (cursor + 1 >= tokens.Count) break;
                        var nextTk = tokens[cursor + 1];
                        bool nextIsSet = IsMathSetName(nextTk.Name);
                        bool nextIsBracket = nextTk.Raw == "[" || nextTk.Raw == "]"
                            || nextTk.Name == "LBRACKET" || nextTk.Name == "RBRACKET";
                        if (!nextIsSet && !nextIsBracket) break;
                        int nextEnd = FindAtomEnd(tokens, cursor + 1);
                        if (nextEnd <= cursor + 1) break;
                        cursor = nextEnd;
                    }
                    slots[el.CaptureName] = tokens.GetRange(t, cursor - t);
                    if (TryMatchRec(elems, e + 1, tokens, cursor, slots, allowPartial, out finalT)) return true;
                    slots.Remove(el.CaptureName);
                    finalT = t;
                    return false;
                }

                case SlotType.Expr:
                case SlotType.ExprList:
                case SlotType.EquationList:
                    int startT = t;
                    int maxLen = Math.Min(MaxExprLength, tokens.Count - startT);

                    // Stratégie : on parcourt du plus court au plus long.
                    // - Premier match qui consomme TOUT l'input → on le prend immédiatement
                    //   (la plus courte EXPR qui épuise l'input est la bonne : elle laisse
                    //    les littéraux optionnels suivants matcher s'ils existent).
                    // - Sinon, on garde le meilleur match partiel (finalT le plus grand).
                    int bestFinalT = -1;
                    int bestLen = -1;
                    int totalTokens = tokens.Count;

                    for (int len = 1; len <= maxLen; len++)
                    {
                        int endT = startT + len;
                        if (!ParensBalanced(tokens, startT, endT)) continue;
                        // Brackets balanced too : un EXPR ne peut pas avoir un ']'
                        // non-apparié qui précède un '[' non-apparié. Ex: "1]U[3"
                        // est malformé dans un contexte EXPR. Cf. "[0;1]U[3;4]"
                        // qui ne doit PAS matcher interval_closed_closed au niveau
                        // top avec EXPR:b = "1]U[3;4".
                        if (!BracketsWellFormed(tokens, startT, endT)) continue;
                        // EXPR ne doit pas finir sur un opérateur binaire isolé :
                        // "[-2;5]" ne doit pas splitter en a=[-], b=[2;5]. On veut
                        // que l'opérateur soit soit unaire (len>=2) soit absent.
                        var lastTok = tokens[endT - 1];
                        if (IsBinaryOperatorRaw(lastTok.Raw)) continue;
                        var captured = tokens.GetRange(startT, len);
                        if (el.Slot == SlotType.EquationList && !HasSeparator(captured)) continue;
                        slots[el.CaptureName] = captured;
                        if (TryMatchRec(elems, e + 1, tokens, endT, slots, allowPartial, out int tryFinalT))
                        {
                            if (tryFinalT >= totalTokens)
                            {
                                // match qui épuise tout : la plus courte EXPR gagne
                                finalT = tryFinalT;
                                return true;
                            }
                            if (tryFinalT > bestFinalT)
                            {
                                bestFinalT = tryFinalT;
                                bestLen = len;
                            }
                        }
                        slots.Remove(el.CaptureName);
                    }

                    if (bestLen > 0)
                    {
                        slots[el.CaptureName] = tokens.GetRange(startT, bestLen);
                        if (TryMatchRec(elems, e + 1, tokens, startT + bestLen, slots, allowPartial, out finalT))
                            return true;
                        slots.Remove(el.CaptureName);
                    }
                    finalT = t;
                    return false;

                default:
                    finalT = t;
                    return false;
            }
        }

        // ============================================================
        // Détection d'atomes (pour SlotType.Atom et autres usages)
        // ============================================================

        internal static int FindAtomEnd(List<CanonicalToken> tokens, int start)
        {
            if (start >= tokens.Count) return start;
            var t = tokens[start];
            if (t.Raw == "(" || t.Name == "LPAREN")
                return FindMatchingParen(tokens, start);
            // Brackets FR-style : "[a;b]", "[a;b[", "]a;b]", "]a;b[" sont tous des
            // atomes "interval" avec deux brackets délimiteurs. On cherche le
            // prochain bracket (quelle que soit sa direction).
            if (t.Raw == "[" || t.Raw == "]" || t.Name == "LBRACKET" || t.Name == "RBRACKET")
                return FindNextBracket(tokens, start);
            // Unaire +/- : si suivi d'un atome, on englobe dans l'atome (ex. "-2", "+inf").
            if ((t.Raw == "+" || t.Raw == "-") && start + 1 < tokens.Count)
            {
                int innerEnd = FindAtomEnd(tokens, start + 1);
                if (innerEnd > start + 1) return innerEnd;
                return start;
            }
            if (!IsAtomToken(t)) return start;
            int next = start + 1;
            // On n'enchaîne "atome + (args)" en appel de fonction QUE si l'atome
            // commence par une lettre. "0(2x+1)" = multiplication implicite, pas un appel.
            if (next < tokens.Count
                && (tokens[next].Raw == "(" || tokens[next].Name == "LPAREN")
                && t.Raw.Length > 0 && char.IsLetter(t.Raw[0]))
            {
                int parenEnd = FindMatchingParen(tokens, next);
                if (parenEnd > next) return parenEnd;
            }
            // Extension ensembles mathématiques : R*, R+, R-, R*+, R-{0}, R\{0}
            // On absorbe les suffixes dans l'atome pour que ATOM:set capture tout.
            if (IsMathSetName(t.Name))
            {
                int end = next;
                // Jusqu'à 2 suffixes parmi *, +, -
                int suffixCount = 0;
                while (end < tokens.Count && suffixCount < 2
                    && (tokens[end].Raw == "*" || tokens[end].Raw == "+" || tokens[end].Raw == "-"))
                {
                    // Un '-' suivi de '{' est une exclusion R-{a}, pas un suffixe simple
                    if (tokens[end].Raw == "-" && end + 1 < tokens.Count
                        && (tokens[end + 1].Raw == "{" || tokens[end + 1].Name == "LBRACE"))
                        break;
                    end++;
                    suffixCount++;
                }
                // Extension "- { val }", "\ { val }" ou "/ { val }" : R-{0}, R\{-1}, R/{2}
                if (end < tokens.Count
                    && (tokens[end].Raw == "-" || tokens[end].Raw == "\\" || tokens[end].Raw == "/")
                    && end + 1 < tokens.Count
                    && (tokens[end + 1].Raw == "{" || tokens[end + 1].Name == "LBRACE"))
                {
                    int braceClose = FindMatchingBrace(tokens, end + 1);
                    if (braceClose > end + 1) end = braceClose;
                }
                return end;
            }
            return next;
        }

        // Fin d'une LIST_PARAMS : soit "(x,y,z)" parenthésé, soit une suite d'idents
        // séparés par virgule ou espace ("x", "x,y", "x y").
        private static int FindListParamsEnd(List<CanonicalToken> tokens, int start)
        {
            if (start >= tokens.Count) return start;
            var t0 = tokens[start];
            // Forme parenthésée : "(x,y,z)" — on prend tout le groupe, on laissera
            // le renderer dépaqueter.
            if (t0.Raw == "(" || t0.Name == "LPAREN")
            {
                int closeIdx = FindMatchingParen(tokens, start);
                if (closeIdx <= start) return start;
                // Vérifie que le contenu est bien une liste d'idents
                bool ok = true;
                int identCount = 0;
                for (int i = start + 1; i < closeIdx - 1; i++)
                {
                    var tk = tokens[i];
                    if (IsParamIdent(tk)) { identCount++; continue; }
                    if (tk.Raw == "," || tk.Raw == ";") continue;
                    ok = false; break;
                }
                if (!ok || identCount == 0) return start;
                return closeIdx;
            }
            // Forme plate : au moins 1 ident, puis éventuellement (sep ident)*
            if (!IsParamIdent(t0)) return start;
            int cursor = start + 1;
            while (cursor < tokens.Count)
            {
                var tk = tokens[cursor];
                // Séparateur explicite : virgule ou point-virgule
                if (tk.Raw == "," || tk.Raw == ";")
                {
                    if (cursor + 1 < tokens.Count && IsParamIdent(tokens[cursor + 1]))
                    { cursor += 2; continue; }
                    break;
                }
                // Séparateur espace : ident directement adjacent avec HadSpaceBefore
                if (IsParamIdent(tk) && tk.HadSpaceBefore) { cursor++; continue; }
                break;
            }
            return cursor;
        }

        // Un ident capturable comme paramètre : identifiant simple (pas opérateur,
        // pas chiffre, pas structure, pas mot-clé sémantique, pas raccourci math).
        private static bool IsParamIdent(CanonicalToken t)
        {
            if (t.IsConnector) return false;
            if (string.IsNullOrEmpty(t.Raw)) return false;
            if (t.Generic == "NUMBER") return false;
            if (!char.IsLetter(t.Raw[0])) return false;
            // Rejet : tout token CANONIQUE (Name non-nul) ne peut pas être un
            // paramètre. VAR, FORALL, LIMIT, SIN, REALS, COMPLEX...
            if (!string.IsNullOrEmpty(t.Name)) return false;
            // Rejet : raccourcis qui matchent un slot spécialisé dans d'autres
            // patterns (VX_SHORT "Vx", CF_SHORT "Cf", IDENT_BAR "xbar"...).
            // Ces idents ont un usage dédié, pas comme variables libres.
            if (IsVxShort(t.Raw)) return false;
            if (IsCfShort(t.Raw)) return false;
            if (IsIdentBar(t.Raw)) return false;
            return true;
        }

        // Token qui connecte deux morceaux d'un intervalle composé.
        // "U" / "∪" / "UNION" / "INTER" / "∩" / "INTERSECTION".
        private static bool IsIntervalConnector(CanonicalToken t)
        {
            if (t.Name == "UNION" || t.Name == "INTERSECTION") return true;
            if (t.Raw == "U" || t.Raw == "u" || t.Raw == "∪" || t.Raw == "∩") return true;
            return false;
        }

        private static bool IsMathSetName(string? name)
        {
            return name == "REALS" || name == "NATURALS" || name == "INTEGERS"
                || name == "RATIONALS" || name == "COMPLEX";
        }

        private static int FindMatchingBrace(List<CanonicalToken> tokens, int openIdx)
        {
            int depth = 1;
            for (int i = openIdx + 1; i < tokens.Count; i++)
            {
                if (tokens[i].Raw == "{" || tokens[i].Name == "LBRACE") depth++;
                else if (tokens[i].Raw == "}" || tokens[i].Name == "RBRACE")
                {
                    depth--;
                    if (depth == 0) return i + 1;
                }
            }
            return openIdx;
        }

        // Trouve le prochain bracket (']' ou '[') à partir de openIdx+1, en ignorant
        // le contenu parenthésé. Sert pour les intervalles FR où les brackets peuvent
        // aller dans les deux sens ([a;b[, ]a;b]).
        private static int FindNextBracket(List<CanonicalToken> tokens, int openIdx)
        {
            int parenDepth = 0;
            for (int i = openIdx + 1; i < tokens.Count; i++)
            {
                if (tokens[i].Raw == "(" || tokens[i].Name == "LPAREN") parenDepth++;
                else if (tokens[i].Raw == ")" || tokens[i].Name == "RPAREN") parenDepth--;
                else if (parenDepth == 0
                    && (tokens[i].Raw == "[" || tokens[i].Raw == "]"
                        || tokens[i].Name == "LBRACKET" || tokens[i].Name == "RBRACKET"))
                    return i + 1;
            }
            return openIdx; // pas de fermeture trouvée
        }

        private static int FindMatchingParen(List<CanonicalToken> tokens, int openIdx)
        {
            int depth = 1;
            for (int i = openIdx + 1; i < tokens.Count; i++)
            {
                if (tokens[i].Raw == "(" || tokens[i].Name == "LPAREN") depth++;
                else if (tokens[i].Raw == ")" || tokens[i].Name == "RPAREN")
                {
                    depth--;
                    if (depth == 0) return i + 1;
                }
            }
            return openIdx;
        }

        private static bool IsAtomToken(CanonicalToken t)
        {
            if (t.IsConnector) return false;
            switch (t.Raw)
            {
                case "+": case "-": case "*": case "/": case "^": case "_":
                case "=": case "<": case ">": case ",": case ";":
                case "(": case ")": case "{": case "}": case "[": case "]":
                case "|":
                    return false;
            }
            return true;
        }

        private static bool IsBinaryOperatorRaw(string raw)
        {
            switch (raw)
            {
                case "+": case "-": case "*": case "/":
                case "^": case "_":
                case "=": case "<": case ">":
                case ",": case ";":
                    return true;
            }
            return false;
        }

        private static bool HasSeparator(List<CanonicalToken> toks)
        {
            foreach (var t in toks)
                if (t.Raw == "," || t.Raw == ";") return true;
            return false;
        }

        // Reconnaît les formes "Vx", "Vy", "Vt"... : V suivi d'exactement une
        // lettre minuscule. Sert au raccourci "Vx(R" = "pour tout x ∈ R".
        private static bool IsVxShort(string raw)
        {
            return raw.Length == 2 && raw[0] == 'V' && char.IsLower(raw[1]);
        }

        // Reconnaît les formes "Cf", "Cg", "Ch"... : C majuscule + lettre(s) minuscule(s).
        // Sert au raccourci "Cf" = "courbe de la fonction f" (mathcal C subscript f).
        private static bool IsCfShort(string raw)
        {
            if (raw.Length < 2 || raw[0] != 'C') return false;
            for (int i = 1; i < raw.Length; i++)
                if (!char.IsLower(raw[i])) return false;
            return true;
        }

        // Reconnaît les paires de lettres majuscules : "AB", "BC", "XYZ"... typiques de
        // la notation géométrique (points, segments, vecteurs).
        private static bool IsUpperPair(string raw)
        {
            if (raw.Length < 2) return false;
            foreach (var c in raw) if (!char.IsUpper(c)) return false;
            return true;
        }

        // Reconnaît EXACTEMENT 3 lettres majuscules : "ABC", "XYZ". Spécifique à la
        // notation d'angle géométrique (sommet au milieu). Strict sur la taille pour
        // ne pas confondre avec un couple de points "AB" (paire) ou autre.
        private static bool IsUpperTriple(string raw)
        {
            if (raw.Length != 3) return false;
            foreach (var c in raw) if (!char.IsUpper(c)) return false;
            return true;
        }

        // Reconnaît un identifiant "moyenne" : 1 lettre + "bar"/"barre" (ex. "xbarre",
        // "yBar"). Sert au raccourci "xbarre" = "\overline{x}".
        private static bool IsIdentBar(string raw)
        {
            if (raw.Length < 4) return false;
            if (!char.IsLetter(raw[0])) return false;
            string suffix = raw.Substring(1).ToLowerInvariant();
            return suffix == "bar" || suffix == "barre";
        }

        // Reconnaît "Df", "Dg", "Dh"... : D majuscule + 1 lettre minuscule.
        // Sert au raccourci ensemble de définition (Df → D_f).
        private static bool IsDfShort(string raw)
        {
            return raw.Length == 2 && raw[0] == 'D' && char.IsLower(raw[1]);
        }

        // Reconnaît une coordonnée indexée par un point : x/y/z suivi d'une
        // lettre désignant un point (majuscule ou minuscule — la convention FR
        // tolère "xa" pour "x_A"). Minimum 2 caractères, maximum 2.
        private static bool IsCoordPoint(string raw)
        {
            if (raw.Length != 2) return false;
            char first = raw[0];
            if (first != 'x' && first != 'y' && first != 'z'
                && first != 'X' && first != 'Y' && first != 'Z') return false;
            return char.IsLetter(raw[1]);
        }

        // Reconnaît les identifiants "suite compacte" : u, v, w, s, t + 'n' (ex. "un", "vn")
        private static bool IsSequenceIdent(string raw)
        {
            if (raw.Length < 2 || raw.Length > 3) return false;
            if (raw[raw.Length - 1] != 'n' && raw[raw.Length - 1] != 'N') return false;
            char first = raw[0];
            return "uvwstUVWST".IndexOf(first) >= 0;
        }

        // Brackets "bien formés" : la profondeur ne doit jamais descendre sous zéro,
        // et on tolère une fin à profondeur non-nulle (pour les intervalles FR).
        // C'est moins strict que "balanced" : on refuse juste les ']' qui précèdent
        // leur '[', pas les différences de count.
        private static bool BracketsWellFormed(List<CanonicalToken> tokens, int start, int end)
        {
            int depth = 0;
            for (int i = start; i < end; i++)
            {
                var raw = tokens[i].Raw;
                var name = tokens[i].Name;
                if (raw == "[" || name == "LBRACKET") depth++;
                else if (raw == "]" || name == "RBRACKET")
                {
                    depth--;
                    if (depth < 0) return false;
                }
            }
            return true;
        }

        private static bool ParensBalanced(List<CanonicalToken> tokens, int start, int end)
        {
            // Note : on ne vérifie que les parenthèses, pas les brackets. Les intervalles
            // "à la française" (]a;b], [a;b[) ont des brackets non balancés par convention
            // et on ne veut pas les rejeter.
            int depth = 0;
            for (int i = start; i < end; i++)
            {
                if (tokens[i].Name == "LPAREN" || tokens[i].Raw == "(") depth++;
                else if (tokens[i].Name == "RPAREN" || tokens[i].Raw == ")")
                {
                    depth--;
                    if (depth < 0) return false;
                }
            }
            return depth == 0;
        }
    }

}
