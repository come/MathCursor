using System.Collections.Generic;
using System.Text;

namespace MathCursor.Core.Patterns.Templates
{
    /// <summary>
    /// Pattern <c>"interval-union"</c> : un intervalle <c>[a,b]</c>/<c>(a,b)</c>/
    /// <c>[a,b)</c>/<c>(a,b]</c>, optionnellement enchaîné par
    /// <c>U</c>/<c>∪</c>/<c>union</c>/<c>inter</c>/<c>∩</c> à un autre
    /// <c>interval-union</c> (récursif via slot <c>tail</c>).
    ///
    /// <para>Slots émis dans un <see cref="PatternMatch"/> :</para>
    /// <list type="bullet">
    ///   <item><c>leftBracket</c> — <c>FilledSlotAtom("[")</c> ou <c>("(")</c></item>
    ///   <item><c>lo</c> — borne basse (texte brut : nombre, identifier,
    ///     <c>+oo</c>/<c>-oo</c>/<c>+∞</c>/<c>-∞</c>) ou <c>EmptySlot</c></item>
    ///   <item><c>hi</c> — borne haute, idem</item>
    ///   <item><c>rightBracket</c> — <c>"]"</c>/<c>")"</c> ou <c>EmptySlot</c></item>
    ///   <item><c>operator</c> — <c>FilledSlotAtom</c> portant <c>U</c>/<c>∪</c>/
    ///     <c>union</c>/<c>inter</c>/<c>∩</c> si chaîne continue (sinon absent)</item>
    ///   <item><c>tail</c> — <c>FilledSlotSubPattern</c> pour la suite récursive
    ///     (sinon absent)</item>
    /// </list>
    ///
    /// <para>Pas de <see cref="Lattice.SourceMutation"/> émise : la source
    /// <c>[0,1]U[3,4]</c> est déjà parsable telle quelle par le pipeline
    /// existant. La complétion sert à <b>afficher</b> la forme dans la popup
    /// (avec <c>\square</c> pour les slots vides via <see cref="PatternCompletion.HintLatex"/>),
    /// pas à substituer la source.</para>
    ///
    /// <para>Cf. ADR <c>2026-05-21-Meta-pattern-templates-vs-ambig-closed</c>,
    /// étape P4. Sera consommé en P5 par <c>ForallBelongsTemplate</c> via
    /// <see cref="PatternRefSlot"/> et en P4.5 par <c>EnsembleTemplate</c>
    /// (head <c>[</c> delegate).</para>
    /// </summary>
    public sealed class IntervalUnionTemplate : IPatternTemplate
    {
        public string TemplateId => "interval-union";
        public int Order => 0;

        public PatternMatch? TryMatchHead(PatternScanContext ctx)
        {
            if (ctx == null) return null;
            var src = ctx.Source;
            if (string.IsNullOrEmpty(src)) return null;

            for (int i = ctx.StartPos; i < src.Length; i++)
            {
                char c = src[i];
                if (c != '[' && c != '(') continue;

                // Boundary gauche : seulement pour '(' (ambig function call /
                // indice). '[' est toujours accepté (pas d'ambig courante).
                if (c == '(' && i > 0)
                {
                    char prev = src[i - 1];
                    if (IsInvalidPrevForOpenParen(prev)) continue;
                }

                var slots = new Dictionary<string, SlotValue>(4)
                {
                    ["leftBracket"] = new FilledSlotAtom(c.ToString(), i, i + 1),
                    ["lo"] = EmptySlot.Instance,
                    ["hi"] = EmptySlot.Instance,
                    ["rightBracket"] = EmptySlot.Instance,
                };

                var headOnly = new PatternMatch(
                    templateId: TemplateId,
                    sourceStart: i,
                    sourceEnd: i + 1,
                    slots: slots,
                    isComplete: false);

                // P5 : eager parse — le state retourné a déjà son SourceEnd
                // étendu sur toute la chaîne d'intervals. Permet aux parents
                // (ForallBelongsTemplate) de connaître la fin du sub-pattern
                // après TryMatchHead, sans devoir appeler Expand. Expand
                // devient un pur rendu LaTeX depuis le state final.
                return ParseFromState(headOnly, src);
            }
            return null;
        }

        public IReadOnlyList<PatternCompletion> Expand(PatternMatch state, PatternScanContext ctx)
        {
            if (state == null || ctx == null) return System.Array.Empty<PatternCompletion>();
            // P5 : state est déjà eager-parsed par TryMatchHead. On ne re-parse
            // pas (idempotent : ParseFromState sur un state déjà complet le
            // laisse tel quel).
            var finalState = state;
            var preview = BuildLatex(finalState, hideEmpty: true);
            var hint = BuildLatex(finalState, hideEmpty: false);
            var description = BuildDescription(finalState);
            int score = ComputeCompletenessScore(finalState);

            var completion = new PatternCompletion(
                description: description,
                previewLatex: preview,
                hintLatex: hint,
                mutation: null,
                completenessScore: score,
                sourceStart: finalState.SourceStart,
                sourceEnd: finalState.SourceEnd);

            return new[] { completion };
        }

        // ─── Parsing ─────────────────────────────────────────────────

        /// <summary>
        /// Étend l'état <paramref name="state"/> en consommant les tokens à
        /// partir de <c>state.SourceEnd</c> dans <paramref name="src"/>.
        /// Construction d'un nouvel état immutable étape par étape.
        /// S'arrête dès qu'un slot ne peut être rempli — l'état partiel est
        /// retourné (le caller affichera les <c>EmptySlot</c> restants via
        /// <c>\square</c> dans le hint).
        /// </summary>
        private PatternMatch ParseFromState(PatternMatch state, string src)
        {
            int pos = state.SourceEnd;

            // 1. Parse lo
            pos = SkipWhitespace(src, pos);
            var loAtom = ParseAtom(src, pos);
            if (loAtom == null) return state;
            state = state
                .WithSlot("lo", loAtom)
                .WithSourceEnd(loAtom.End);
            pos = loAtom.End;

            // 2. Comma
            pos = SkipWhitespace(src, pos);
            if (pos >= src.Length || src[pos] != ',') return state;
            pos++;

            // 3. Parse hi
            pos = SkipWhitespace(src, pos);
            var hiAtom = ParseAtom(src, pos);
            if (hiAtom == null) return state;
            state = state
                .WithSlot("hi", hiAtom)
                .WithSourceEnd(hiAtom.End);
            pos = hiAtom.End;

            // 4. Right bracket
            pos = SkipWhitespace(src, pos);
            if (pos >= src.Length) return state;
            char rb = src[pos];
            if (rb != ']' && rb != ')') return state;
            state = state
                .WithSlot("rightBracket", new FilledSlotAtom(rb.ToString(), pos, pos + 1))
                .WithSourceEnd(pos + 1)
                .WithComplete(true);
            pos++;

            // 5. Optional operator
            int posBeforeOp = SkipWhitespace(src, pos);
            var (op, opStart, opEnd) = ParseOperator(src, posBeforeOp);
            if (op == null) return state;

            // 6. Recursive sub-pattern
            int afterOp = SkipWhitespace(src, opEnd);
            var subHead = TryMatchHeadAt(src, afterOp);
            if (subHead == null)
            {
                // Operator parsed but no following interval → on garde l'op
                // dans le slot (forme incomplète "...U" mais tail absent).
                state = state
                    .WithSlot("operator", new FilledSlotAtom(op, opStart, opEnd))
                    .WithSourceEnd(opEnd)
                    .WithComplete(false);
                return state;
            }
            var subFinal = ParseFromState(subHead, src);

            state = state
                .WithSlot("operator", new FilledSlotAtom(op, opStart, opEnd))
                .WithSlot("tail", new FilledSlotSubPattern(subFinal))
                .WithSourceEnd(subFinal.SourceEnd)
                .WithComplete(subFinal.IsComplete);
            return state;
        }

        private PatternMatch? TryMatchHeadAt(string src, int pos)
        {
            if (pos < 0 || pos >= src.Length) return null;
            char c = src[pos];
            if (c != '[' && c != '(') return null;
            // Boundary gauche : seulement pour '(' (cf. TryMatchHead).
            if (c == '(' && pos > 0 && IsInvalidPrevForOpenParen(src[pos - 1])) return null;

            var slots = new Dictionary<string, SlotValue>(4)
            {
                ["leftBracket"] = new FilledSlotAtom(c.ToString(), pos, pos + 1),
                ["lo"] = EmptySlot.Instance,
                ["hi"] = EmptySlot.Instance,
                ["rightBracket"] = EmptySlot.Instance,
            };
            return new PatternMatch(
                templateId: TemplateId,
                sourceStart: pos,
                sourceEnd: pos + 1,
                slots: slots,
                isComplete: false);
        }

        private static int SkipWhitespace(string src, int pos)
        {
            while (pos < src.Length && char.IsWhiteSpace(src[pos])) pos++;
            return pos;
        }

        /// <summary>
        /// Détermine si le caractère précédant <c>(</c> invalide un match
        /// d'intervalle (= ce <c>(</c> ouvre un function call, pas un
        /// intervalle ouvert). Couvre lettres/digits (= <c>f(x)</c>,
        /// <c>2(x)</c>) et apostrophes/primes ASCII + Unicode (= primed
        /// derivative <c>f'(x)</c>, <c>f''(x)</c>, <c>f’(x)</c> avec Word
        /// auto-correct typographique).
        /// </summary>
        private static bool IsInvalidPrevForOpenParen(char prev)
        {
            if (char.IsLetterOrDigit(prev)) return true;
            switch (prev)
            {
                case '\'':       // U+0027 apostrophe ASCII
                case '’':   // U+2019 right single quotation mark
                case '‘':   // U+2018 left single quotation mark
                case '′':   // U+2032 math prime
                case '"':        // U+0022 quotation mark ASCII
                case '”':   // U+201D right double quotation mark
                case '“':   // U+201C left double quotation mark
                case '″':   // U+2033 math double prime
                case '‴':   // U+2034 math triple prime
                case '⁗':   // U+2057 math quadruple prime
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Parse un atome de borne : nombre (digits, point décimal, signe
        /// optionnel), identifier, ou symbole infini (<c>+oo</c>, <c>-oo</c>,
        /// <c>+∞</c>, <c>-∞</c>, <c>∞</c>). Retourne <c>null</c> si rien de
        /// reconnu à <paramref name="pos"/>.
        /// </summary>
        private static FilledSlotAtom? ParseAtom(string src, int pos)
        {
            if (pos >= src.Length) return null;
            int start = pos;
            bool hasSign = false;
            char c = src[pos];

            // Sign optional pour nombre ou infini
            if (c == '+' || c == '-')
            {
                pos++;
                hasSign = true;
                if (pos >= src.Length) return null;
                c = src[pos];
            }

            // Infini Unicode
            if (c == '∞')
            {
                pos++;
                return new FilledSlotAtom(src.Substring(start, pos - start), start, pos);
            }
            // oo (deux 'o' minuscules tight)
            if (c == 'o' && pos + 1 < src.Length && src[pos + 1] == 'o')
            {
                pos += 2;
                return new FilledSlotAtom(src.Substring(start, pos - start), start, pos);
            }

            // Nombre : digits [. digits]
            if (char.IsDigit(c))
            {
                while (pos < src.Length && char.IsDigit(src[pos])) pos++;
                if (pos < src.Length && src[pos] == '.')
                {
                    pos++;
                    while (pos < src.Length && char.IsDigit(src[pos])) pos++;
                }
                if (pos == start || (hasSign && pos == start + 1)) return null;
                return new FilledSlotAtom(src.Substring(start, pos - start), start, pos);
            }

            // Identifier (lettres unicode) — pas de sign devant accepté
            // (+x / -x ne sont pas des bornes valides ; seuls +oo / -oo /
            // +∞ / -∞ le sont, couverts plus haut).
            if (char.IsLetter(c))
            {
                if (hasSign) return null;
                while (pos < src.Length && char.IsLetter(src[pos])) pos++;
                return new FilledSlotAtom(src.Substring(start, pos - start), start, pos);
            }

            return null;
        }

        /// <summary>
        /// Parse un opérateur d'union/intersection à <paramref name="pos"/>.
        /// Reconnaît : <c>U</c> (single), <c>∪</c>, <c>∩</c>, <c>union</c>,
        /// <c>inter</c>. Retourne <c>(canonical, start, end)</c> où
        /// <c>canonical</c> est <c>"U"</c> ou <c>"∩"</c> ou la forme tapée.
        /// </summary>
        private static (string? op, int start, int end) ParseOperator(string src, int pos)
        {
            if (pos >= src.Length) return (null, pos, pos);
            char c = src[pos];
            if (c == '∪') return ("∪", pos, pos + 1);
            if (c == '∩') return ("∩", pos, pos + 1);
            if (c == 'U')
            {
                // U seul (pas suivi de lettre)
                bool nextIsLetter = pos + 1 < src.Length && char.IsLetter(src[pos + 1]);
                if (!nextIsLetter) return ("U", pos, pos + 1);
            }
            // Mots-clés : union, inter
            if (MatchKeyword(src, pos, "union")) return ("union", pos, pos + 5);
            if (MatchKeyword(src, pos, "inter")) return ("inter", pos, pos + 5);
            return (null, pos, pos);
        }

        private static bool MatchKeyword(string src, int pos, string kw)
        {
            if (pos + kw.Length > src.Length) return false;
            for (int k = 0; k < kw.Length; k++)
                if (src[pos + k] != kw[k]) return false;
            // Boundary droite : pas une lettre (pour éviter "unionx" qui matcherait)
            int after = pos + kw.Length;
            if (after < src.Length && char.IsLetter(src[after])) return false;
            return true;
        }

        // ─── Rendu LaTeX + description ────────────────────────────────

        /// <summary>
        /// Construit le rendu LaTeX de <paramref name="state"/>. Si
        /// <paramref name="hideEmpty"/> est true, les slots vides sont
        /// remplacés par chaîne vide (= PreviewLatex). Sinon, par
        /// <c>\square</c> (= HintLatex).
        /// </summary>
        private static string BuildLatex(PatternMatch state, bool hideEmpty)
        {
            var sb = new StringBuilder();
            AppendStateLatex(sb, state, hideEmpty);
            return sb.ToString();
        }

        private static void AppendStateLatex(StringBuilder sb, PatternMatch state, bool hideEmpty)
        {
            string lb = SlotText(state, "leftBracket") ?? string.Empty;
            string lo = SlotText(state, "lo") ?? (hideEmpty ? "" : "\\square");
            string hi = SlotText(state, "hi") ?? (hideEmpty ? "" : "\\square");
            // rb : toujours rendu (valeur ou miroir du lb) pour préserver la
            // structure visuelle de l'interval même partiel.
            string rb = SlotText(state, "rightBracket") ?? MirrorBracket(lb);

            sb.Append("\\left").Append(lb)
              .Append(lo).Append(",").Append(hi)
              .Append("\\right").Append(rb);

            if (state.Slots.TryGetValue("operator", out var opVal)
                && opVal is FilledSlotAtom opAtom)
            {
                bool hasTail = state.Slots.TryGetValue("tail", out var tailVal)
                               && tailVal is FilledSlotSubPattern;
                // En preview, on cache l'opérateur ET la suite si pas de tail :
                // un "[0,1] \cup" tout seul est visuellement incomplet, mieux
                // de ne pas l'afficher en preview.
                if (hideEmpty && !hasTail) return;

                sb.Append(" ").Append(OperatorToLatex(opAtom.Text)).Append(" ");
                if (hasTail
                    && state.Slots.TryGetValue("tail", out var tv)
                    && tv is FilledSlotSubPattern tailSub)
                {
                    AppendStateLatex(sb, tailSub.Sub, hideEmpty);
                }
                else
                {
                    // Hint avec carrés pour la suite manquante.
                    sb.Append("\\left[\\square,\\square\\right]");
                }
            }
        }

        private static string? SlotText(PatternMatch state, string slotName)
        {
            if (!state.Slots.TryGetValue(slotName, out var v)) return null;
            if (v is FilledSlotAtom atom) return atom.Text;
            return null;
        }

        private static string MirrorBracket(string lb)
            => lb == "[" ? "]" : (lb == "(" ? ")" : "]");

        private static string OperatorToLatex(string op) => op switch
        {
            "U" => "\\cup",
            "∪" => "\\cup",
            "union" => "\\cup",
            "∩" => "\\cap",
            "inter" => "\\cap",
            _ => "\\cup",
        };

        // ─── Description (Unicode) ────────────────────────────────────

        private static string BuildDescription(PatternMatch state)
        {
            var sb = new StringBuilder();
            AppendStateDescription(sb, state);
            return sb.ToString();
        }

        private static void AppendStateDescription(StringBuilder sb, PatternMatch state)
        {
            string lb = SlotText(state, "leftBracket") ?? "[";
            string lo = SlotText(state, "lo") ?? "▭";
            string hi = SlotText(state, "hi") ?? "▭";
            string rb = SlotText(state, "rightBracket") ?? MirrorBracket(lb);
            sb.Append(lb).Append(lo).Append(",").Append(hi).Append(rb);

            if (state.Slots.TryGetValue("operator", out var opVal)
                && opVal is FilledSlotAtom opAtom)
            {
                string opSymbol = opAtom.Text switch
                {
                    "U" => "∪",
                    "∪" => "∪",
                    "union" => "∪",
                    "∩" => "∩",
                    "inter" => "∩",
                    _ => "∪",
                };
                sb.Append(opSymbol);
                if (state.Slots.TryGetValue("tail", out var tailVal)
                    && tailVal is FilledSlotSubPattern tailSub)
                {
                    AppendStateDescription(sb, tailSub.Sub);
                }
                else
                {
                    sb.Append("[▭,▭]");
                }
            }
        }

        // ─── Completeness score ───────────────────────────────────────

        private static int ComputeCompletenessScore(PatternMatch state)
        {
            int filled = 0;
            int total = 4; // leftBracket, lo, hi, rightBracket
            if (!state.Slots["leftBracket"].IsEmpty) filled++;
            if (!state.Slots["lo"].IsEmpty) filled++;
            if (!state.Slots["hi"].IsEmpty) filled++;
            if (!state.Slots["rightBracket"].IsEmpty) filled++;

            if (state.Slots.ContainsKey("tail")
                && state.Slots["tail"] is FilledSlotSubPattern sub)
            {
                int subScore = ComputeCompletenessScore(sub.Sub);
                // Moyenne pondérée : 70% sur l'interval courant, 30% sur tail
                return (filled * 100 / total * 70 + subScore * 30) / 100;
            }
            return filled * 100 / total;
        }
    }
}
