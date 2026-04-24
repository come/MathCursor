using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MathCursor.Core.PatternEngine
{
    /// <summary>
    /// Substitue les <c>{{slot}}</c> du template par le rendu canonique des tokens capturés.
    ///
    /// Règles de rendu des slots :
    /// - IDENT_SEQ : raw privé de son dernier caractère (ex. "un" → "u")
    /// - IDENT / NUMBER / alternative : canonical form
    /// - EXPR / EXPR_LIST : récursif si possible, sinon join canonique avec espaces adaptés
    ///
    /// Si le slot est contextuellement à l'intérieur d'un groupe LaTeX <c>{...}</c>
    /// (ex. <c>\frac{{{num}}}{{{denom}}}</c>) et que la capture est un groupe parenthésé
    /// cohérent, les parens externes sont supprimés (<c>(x+1)</c> → <c>x+1</c>).
    /// </summary>
    public sealed class TemplateRenderer
    {
        private static readonly Regex SlotRx = new Regex(@"\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*\}\}", RegexOptions.Compiled);

        private readonly PatternEngine? _recursiveEngine;
        private readonly string _sourceSpan;
        private readonly int _recursionDepth;

        public TemplateRenderer(PatternEngine? recursiveEngine, string sourceSpan, int recursionDepth = 0)
        {
            _recursiveEngine = recursiveEngine;
            _sourceSpan = sourceSpan;
            _recursionDepth = recursionDepth;
        }

        /// <summary>
        /// Rendu simple : une seule variante (top-1 pour chaque slot).
        /// </summary>
        public string Render(string template, MatchResult match)
        {
            return RenderAll(template, match).First();
        }

        /// <summary>
        /// Rendu multi-variantes : renvoie la variante "base" (top-1 pour chaque slot)
        /// puis, pour chaque slot qui a plusieurs alternatives via récursion, une
        /// variante qui swap UNIQUEMENT ce slot pour son top-2 (les autres restent top-1).
        /// Évite l'explosion du produit cartésien.
        /// </summary>
        public List<string> RenderAll(string template, MatchResult match)
        {
            // Mode partiel : slots non capturés (TryMatchPrefix) → placeholder \ldots.
            bool emitPlaceholders = match.IsPartial;

            // Collecte les alternatives possibles pour chaque slot
            var slotAlts = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var slotContext = new Dictionary<string, (bool insideBraces, int matchIndex, int matchLength)>();

            foreach (Match m in SlotRx.Matches(template))
            {
                var name = m.Groups[1].Value;
                if (!match.Slots.TryGetValue(name, out var tokens)) continue;
                if (slotAlts.ContainsKey(name)) continue;

                bool insideBraces = m.Index > 0 && template[m.Index - 1] == '{'
                                    && m.Index + m.Length < template.Length && template[m.Index + m.Length] == '}';
                var slotType = match.SlotTypes.TryGetValue(name, out var st) ? st : SlotType.Expr;
                var alts = RenderSlotAlternatives(tokens, slotType, insideBraces);
                slotAlts[name] = alts;
                slotContext[name] = (insideBraces, m.Index, m.Length);
            }

            // Nombre max d'alternatives de n'importe quel slot
            int maxAlts = slotAlts.Values.Count == 0 ? 1 : slotAlts.Values.Max(l => l.Count);
            // Cap à 3 variantes totales pour éviter l'explosion
            int variantCount = Math.Min(maxAlts, 3);

            var results = new List<string>(variantCount);
            for (int idx = 0; idx < variantCount; idx++)
            {
                // Variante idx : chaque slot utilise son alternative n°idx (ou top-1 si < idx options).
                // Ça applique la même lecture à tous les slots : "tout en vecteur" ou "tout en littéral".
                string v = SubstituteTemplate(template, slotAlts, idx, emitPlaceholders);
                if (!results.Contains(v)) results.Add(v);
            }
            return results;
        }

        private string SubstituteTemplate(string template, Dictionary<string, List<string>> slotAlts, int altIndex, bool emitPlaceholderForMissing)
        {
            return SlotRx.Replace(template, m =>
            {
                var name = m.Groups[1].Value;
                // Slot non rempli :
                // - Mode normal → on préserve "{{name}}" (signe de bug de template).
                // - Mode partiel → on émet "\ldots" pour signaler à l'utilisateur que
                //   cette position reste à remplir (WpfMath colorie en rouge côté popup).
                if (!slotAlts.TryGetValue(name, out var alts) || alts.Count == 0)
                    return emitPlaceholderForMissing ? "\\ldots" : m.Value;

                int pickIndex = altIndex < alts.Count ? altIndex : 0;
                string rendered = alts[pickIndex];

                bool insideBraces = m.Index > 0 && template[m.Index - 1] == '{'
                                    && m.Index + m.Length < template.Length && template[m.Index + m.Length] == '}';

                // Auto-wrap {} après _ ou ^ quand le rendu fait plus d'un caractère.
                if (!insideBraces && rendered.Length > 1 && m.Index > 0
                    && (template[m.Index - 1] == '_' || template[m.Index - 1] == '^'))
                {
                    return "{" + rendered + "}";
                }
                return rendered;
            });
        }

        /// <summary>
        /// Rendu d'un slot → liste d'alternatives (ordre : top-1, top-2, ...).
        /// Toujours au moins 1 élément. Pour les slots atomiques on renvoie 1 seul.
        /// Pour EXPR/ExprList/Atom, la récursion peut exposer plusieurs candidats.
        /// </summary>
        private List<string> RenderSlotAlternatives(List<CanonicalToken> tokens, SlotType slotType, bool insideBraces)
        {
            if (tokens.Count == 0) return new List<string> { "" };

            if (slotType == SlotType.IdentSeq)
            {
                var raw = tokens[0].Raw;
                return new List<string> { raw.Length <= 1 ? raw : raw.Substring(0, raw.Length - 1) };
            }
            if (slotType == SlotType.VxShort)
            {
                var raw = tokens[0].Raw;
                return new List<string> { raw.Length <= 1 ? raw : raw.Substring(1) };
            }
            if (slotType == SlotType.CfShort)
            {
                // "Cf" → "f" (raw privé du préfixe 'C'). Le template wrappe dans mathcal{C}.
                var raw = tokens[0].Raw;
                return new List<string> { raw.Length <= 1 ? raw : raw.Substring(1) };
            }
            if (slotType == SlotType.IdentBar)
            {
                // "xbarre" → "x" (raw privé du suffixe bar/barre).
                var raw = tokens[0].Raw;
                return new List<string> { raw.Length >= 1 ? raw.Substring(0, 1) : raw };
            }
            if (slotType == SlotType.DfShort)
            {
                // "Df" → "f" (raw privé du préfixe 'D'). Le template wrappe en D_f.
                var raw = tokens[0].Raw;
                return new List<string> { raw.Length <= 1 ? raw : raw.Substring(1) };
            }
            if (slotType == SlotType.CoordPoint)
            {
                // "xA" / "xa" → "x_A" (convention FR : point en majuscule).
                var raw = tokens[0].Raw;
                if (raw.Length < 2) return new List<string> { raw };
                char baseCh = raw[0];
                char pointCh = char.ToUpperInvariant(raw[1]);
                return new List<string> { baseCh + "_" + pointCh };
            }
            if (slotType == SlotType.Ident || slotType == SlotType.Number)
            {
                var t = tokens[0];
                return new List<string> { string.IsNullOrEmpty(t.Canonical) ? t.Raw : t.Canonical };
            }
            if (slotType == SlotType.ListParams)
            {
                // LIST_PARAMS : "x" / "x,y" / "x y" / "(x,y,z)" → idents joints par ", "
                int lo = 0, hi = tokens.Count;
                if (hi >= 2
                    && (tokens[0].Raw == "(" || tokens[0].Name == "LPAREN")
                    && (tokens[hi - 1].Raw == ")" || tokens[hi - 1].Name == "RPAREN"))
                {
                    lo = 1; hi--;
                }
                var idents = new List<string>();
                for (int i = lo; i < hi; i++)
                {
                    var tk = tokens[i];
                    if (tk.Raw == "," || tk.Raw == ";") continue;
                    if (tk.IsConnector) continue;
                    idents.Add(string.IsNullOrEmpty(tk.Canonical) ? tk.Raw : tk.Canonical);
                }
                return new List<string> { string.Join(", ", idents) };
            }
            if (slotType == SlotType.EquationList)
            {
                var parts = new List<List<CanonicalToken>>();
                var current = new List<CanonicalToken>();
                foreach (var tok in tokens)
                {
                    if (tok.Raw == "," || tok.Raw == ";")
                    {
                        if (current.Count > 0) { parts.Add(current); current = new List<CanonicalToken>(); }
                    }
                    else current.Add(tok);
                }
                if (current.Count > 0) parts.Add(current);
                return new List<string> { string.Join(" \\\\ ", parts.Select(p => JoinTokens(p))) };
            }

            // EXPR / ExprList / Atom : strip parens + récursion multi-candidats
            int start = 0;
            int end = tokens.Count;
            if (insideBraces
                && tokens.Count >= 2
                && (tokens[0].Name == "LPAREN" || tokens[0].Raw == "(")
                && (tokens[tokens.Count - 1].Name == "RPAREN" || tokens[tokens.Count - 1].Raw == ")"))
            {
                int depth = 1;
                bool samePair = true;
                for (int i = 1; i < tokens.Count - 1; i++)
                {
                    if (tokens[i].Name == "LPAREN" || tokens[i].Raw == "(") depth++;
                    else if (tokens[i].Name == "RPAREN" || tokens[i].Raw == ")") depth--;
                    if (depth == 0) { samePair = false; break; }
                }
                if (samePair) { start = 1; end = tokens.Count - 1; }
            }

            var alts = new List<string>();
            // Récursion sur captures à 2+ tokens par défaut (perf). Exception pour
            // 1 token si le token ressemble à un raccourci math (Df → D_{f},
            // Cf → \mathcal{C}_{f}, xbarre → \overline{x}) : on recurse explicitement
            // pour découvrir le pattern dédié.
            int tokensSpan = end - start;
            bool isShorthandSingle = tokensSpan == 1
                && start < tokens.Count
                && IsLikelyShorthandToken(tokens[start].Raw);
            if (_recursiveEngine != null && _recursionDepth > 0
                && (tokensSpan >= 2 || isShorthandSingle))
            {
                int subStart = tokens[start].Start;
                int subEnd = tokens[end - 1].End;
                if (subStart >= 0 && subEnd > subStart && subEnd <= _sourceSpan.Length)
                {
                    var sub = _sourceSpan.Substring(subStart, subEnd - subStart);
                    var subSuggestions = _recursiveEngine.ConvertAtDepth(sub, _recursionDepth - 1);
                    foreach (var s in subSuggestions.Where(s => s.TotalTokens > 0 && s.ConsumedTokens == s.TotalTokens).Take(3))
                        if (!alts.Contains(s.Latex)) alts.Add(s.Latex);
                }
            }

            var joined = JoinTokens(tokens.GetRange(start, end - start));
            if (!alts.Contains(joined)) alts.Add(joined);
            return alts;
        }

        // Heuristique rapide : le token raw ressemble-t-il à un raccourci math
        // qui mérite une récursion ? Évite de recurser sur toutes les variables
        // simples (x, y, n…) qui n'ont pas de pattern dédié.
        private static bool IsLikelyShorthandToken(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return false;
            if (raw.Length < 2) return false;
            // "Vx", "Cf", "Df" : V/C/D + lowercase
            if ((raw[0] == 'V' || raw[0] == 'C' || raw[0] == 'D')
                && raw.Length >= 2)
            {
                bool allLower = true;
                for (int i = 1; i < raw.Length; i++)
                    if (!char.IsLower(raw[i])) { allLower = false; break; }
                if (allLower) return true;
            }
            // "xbarre", "ybar" : letter + bar/barre
            if (raw.Length >= 4 && char.IsLetter(raw[0]))
            {
                string suffix = raw.Substring(1).ToLowerInvariant();
                if (suffix == "bar" || suffix == "barre") return true;
            }
            // "xA", "yB", "zM" : x/y/z + letter (coordonnée d'un point)
            if (raw.Length == 2)
            {
                char f = raw[0];
                bool isCoordBase = f == 'x' || f == 'y' || f == 'z'
                                || f == 'X' || f == 'Y' || f == 'Z';
                if (isCoordBase && char.IsLetter(raw[1])) return true;
            }
            return false;
        }

        private const string SynthGeneric = "SYNTH";

        private static string JoinTokens(List<CanonicalToken> tokens)
        {
            // Passes par précédence décroissante. Chaque passe remplace les sous-séquences
            // reconnues par un token synthétique (Generic=SYNTH) qui est ensuite un atome
            // pour les passes suivantes. Ça force la précédence mathématique correcte :
            //    fonctions à braces (\sqrt{...}) > puissances (^, _) > fractions (/)
            var grouped = GroupBraceFunctions(tokens);
            grouped = GroupByOperator(grouped, "^", isPower: true);
            grouped = GroupByOperator(grouped, "_", isPower: true);
            grouped = GroupByOperator(grouped, "/", isPower: false);

            return JoinFlat(grouped);
        }

        private static string JoinFlat(List<CanonicalToken> tokens)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < tokens.Count; i++)
            {
                var tok = tokens[i];
                if (tok.IsConnector) continue;

                string rendered = string.IsNullOrEmpty(tok.Canonical) ? tok.Raw : tok.Canonical;
                if (sb.Length > 0)
                {
                    var prev = tokens[i - 1];
                    if (NeedsSpaceBetween(prev, tok, rendered)) sb.Append(' ');
                }
                sb.Append(rendered);
            }
            return sb.ToString();
        }

        // ============================================================
        // Passe 1 : fonctions à braces (\sqrt{arg}, \vec{arg}, ...)
        // ============================================================

        private static readonly HashSet<string> BraceStyleMacros = new HashSet<string>(StringComparer.Ordinal)
        {
            "\\sqrt", "\\vec", "\\tilde", "\\hat", "\\overline", "\\underline", "\\bar", "\\dot",
        };

        private static List<CanonicalToken> GroupBraceFunctions(List<CanonicalToken> tokens)
        {
            var result = new List<CanonicalToken>();
            int i = 0;
            while (i < tokens.Count)
            {
                var t = tokens[i];
                if (BraceStyleMacros.Contains(t.Canonical ?? "")
                    && i + 1 < tokens.Count
                    && (tokens[i + 1].Raw == "(" || tokens[i + 1].Name == "LPAREN"))
                {
                    int closeEnd = FindMatchingParen(tokens, i + 1);
                    if (closeEnd > i + 1)
                    {
                        var inner = tokens.GetRange(i + 2, closeEnd - i - 3);
                        string innerStr = JoinTokens(inner);
                        result.Add(new CanonicalToken
                        {
                            Raw = "",
                            Generic = SynthGeneric,
                            Canonical = t.Canonical + "{" + innerStr + "}",
                            Start = t.Start,
                            End = tokens[closeEnd - 1].End,
                        });
                        i = closeEnd;
                        continue;
                    }
                }
                result.Add(t);
                i++;
            }
            return result;
        }

        // ============================================================
        // Passe 2/3 : groupement par opérateur (atom OP atom → synth)
        // ============================================================

        private static List<CanonicalToken> GroupByOperator(List<CanonicalToken> tokens, string op, bool isPower)
        {
            var result = new List<CanonicalToken>();
            int i = 0;
            while (i < tokens.Count)
            {
                int leftEnd = FindAtomEnd(tokens, i);
                if (leftEnd > i
                    && leftEnd < tokens.Count
                    && tokens[leftEnd].Raw == op)
                {
                    int rightStart = leftEnd + 1;
                    int rightEnd = FindAtomEnd(tokens, rightStart);
                    // Pour le subscript "_", on étend l'opérande droit sur les chaînes
                    // additives (+ / -). "O_n-1" → "O_{n-1}" plutôt que "O_n - 1".
                    // Le power "^" ne l'étend PAS : convention maths "x^n-1" = x^n - 1.
                    if (op == "_")
                    {
                        while (rightEnd < tokens.Count
                            && (tokens[rightEnd].Raw == "+" || tokens[rightEnd].Raw == "-"))
                        {
                            int nextEnd = FindAtomEnd(tokens, rightEnd + 1);
                            if (nextEnd > rightEnd + 1) rightEnd = nextEnd;
                            else break;
                        }
                    }
                    if (rightEnd > rightStart)
                    {
                        var leftTokens = tokens.GetRange(i, leftEnd - i);
                        var rightTokens = tokens.GetRange(rightStart, rightEnd - rightStart);
                        // Puissance : on garde les parens de la base (2)^3 = `(2)^3`.
                        //             exposant : wrapping explicite pour les multi-caractères.
                        // Fraction : on strip les parens externes (num/denom sont déjà délimités par {}).
                        string leftStr = RenderAtom(leftTokens, stripOuterParens: !isPower);
                        string rightStr = RenderAtom(rightTokens, stripOuterParens: true);
                        string canonical;
                        if (isPower)
                        {
                            string wrapExp = NeedsBraceWrap(rightStr) ? "{" + rightStr + "}" : rightStr;
                            canonical = leftStr + op + wrapExp;
                        }
                        else
                        {
                            canonical = $"\\frac{{{leftStr}}}{{{rightStr}}}";
                        }
                        result.Add(new CanonicalToken
                        {
                            Raw = "",
                            Generic = SynthGeneric,
                            Canonical = canonical,
                            Start = tokens[i].Start,
                            End = tokens[rightEnd - 1].End,
                        });
                        i = rightEnd;
                        continue;
                    }
                }
                result.Add(tokens[i]);
                i++;
            }
            return result;
        }

        private static bool NeedsBraceWrap(string s)
        {
            if (s.Length <= 1) return false;
            // Un seul caractère (digit ou lettre) : pas besoin. Sinon on wrap.
            return true;
        }

        private static string RenderAtom(List<CanonicalToken> atomTokens, bool stripOuterParens)
        {
            if (atomTokens.Count == 0) return "";

            // Groupe parenthésé : strip optionnel puis rendu récursif de l'intérieur.
            if (atomTokens.Count >= 2
                && (atomTokens[0].Raw == "(" || atomTokens[0].Name == "LPAREN")
                && (atomTokens[atomTokens.Count - 1].Raw == ")" || atomTokens[atomTokens.Count - 1].Name == "RPAREN")
                && IsSameParenPair(atomTokens))
            {
                var inner = atomTokens.GetRange(1, atomTokens.Count - 2);
                string innerStr = JoinTokens(inner);
                return stripOuterParens ? innerStr : "(" + innerStr + ")";
            }

            return JoinTokens(atomTokens);
        }

        private static bool IsSameParenPair(List<CanonicalToken> toks)
        {
            int depth = 1;
            for (int i = 1; i < toks.Count - 1; i++)
            {
                if (toks[i].Raw == "(" || toks[i].Name == "LPAREN") depth++;
                else if (toks[i].Raw == ")" || toks[i].Name == "RPAREN") depth--;
                if (depth == 0) return false;
            }
            return true;
        }

        // ============================================================
        // Helpers atomes
        // ============================================================

        private static int FindAtomEnd(List<CanonicalToken> tokens, int start)
        {
            if (start >= tokens.Count) return start;
            var t = tokens[start];
            if (t.Raw == "(" || t.Name == "LPAREN")
                return FindMatchingParen(tokens, start);
            // Unaire +/- englobe l'atome suivant (ex. "-2", "+inf").
            if ((t.Raw == "+" || t.Raw == "-") && start + 1 < tokens.Count)
            {
                int innerEnd = FindAtomEnd(tokens, start + 1);
                if (innerEnd > start + 1) return innerEnd;
                return start;
            }
            if (!IsAtomToken(t)) return start;
            int next = start + 1;
            if (next < tokens.Count
                && (tokens[next].Raw == "(" || tokens[next].Name == "LPAREN")
                && t.Raw.Length > 0 && char.IsLetter(t.Raw[0]))
            {
                int parenEnd = FindMatchingParen(tokens, next);
                if (parenEnd > next) return parenEnd;
            }
            // Extension ensembles mathématiques : R*, R+, R-, R*+, R-{0}, R\{0}
            if (t.Name == "REALS" || t.Name == "NATURALS" || t.Name == "INTEGERS"
                || t.Name == "RATIONALS" || t.Name == "COMPLEX")
            {
                int end = next;
                int suffixCount = 0;
                while (end < tokens.Count && suffixCount < 2
                    && (tokens[end].Raw == "*" || tokens[end].Raw == "+" || tokens[end].Raw == "-"))
                {
                    if (tokens[end].Raw == "-" && end + 1 < tokens.Count
                        && (tokens[end + 1].Raw == "{" || tokens[end + 1].Name == "LBRACE"))
                        break;
                    end++;
                    suffixCount++;
                }
                if (end < tokens.Count
                    && (tokens[end].Raw == "-" || tokens[end].Raw == "\\" || tokens[end].Raw == "/")
                    && end + 1 < tokens.Count
                    && (tokens[end + 1].Raw == "{" || tokens[end + 1].Name == "LBRACE"))
                {
                    int depth = 1;
                    int braceClose = end + 1;
                    for (int i = end + 2; i < tokens.Count; i++)
                    {
                        if (tokens[i].Raw == "{" || tokens[i].Name == "LBRACE") depth++;
                        else if (tokens[i].Raw == "}" || tokens[i].Name == "RBRACE")
                        {
                            depth--;
                            if (depth == 0) { braceClose = i + 1; break; }
                        }
                    }
                    if (braceClose > end + 1) end = braceClose;
                }
                return end;
            }
            return next;
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
            if (t.Generic == SynthGeneric) return true;
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

        private static bool NeedsSpaceBetween(CanonicalToken prev, CanonicalToken curr, string currRendered)
        {
            // Espace si les tokens n'étaient pas adjacents dans l'original
            if (prev.End < curr.Start) return true;

            string prevRendered = string.IsNullOrEmpty(prev.Canonical) ? prev.Raw : prev.Canonical;
            bool prevIsMacro = IsLatexMacro(prevRendered);
            bool currIsMacro = IsLatexMacro(currRendered);
            bool currStartsAlnum = currRendered.Length > 0 && char.IsLetterOrDigit(currRendered[0]);
            bool prevEndsAlnum = prevRendered.Length > 0 && char.IsLetterOrDigit(prevRendered[prevRendered.Length - 1]);

            // Macro + alphanum → espace requis côté LaTeX (sinon fusion)
            if (prevIsMacro && currStartsAlnum) return true;
            // Alphanum + macro → espace pour lisibilité (ex : "2 \cdot")
            if (prevEndsAlnum && currIsMacro) return true;
            return false;
        }

        private static bool IsLatexMacro(string s)
        {
            if (s.Length < 2 || s[0] != '\\') return false;
            for (int i = 1; i < s.Length; i++)
                if (!char.IsLetter(s[i])) return false;
            return true;
        }
    }
}
