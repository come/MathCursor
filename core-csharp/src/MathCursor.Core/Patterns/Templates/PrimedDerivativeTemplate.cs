using System.Collections.Generic;
using System.Text;
using MathCursor.Core.Lattice;

namespace MathCursor.Core.Patterns.Templates
{
    /// <summary>
    /// Pattern <c>"primed-derivative"</c> : notation Lagrange pour les
    /// dérivées primées — <c>f'</c>, <c>f''</c>, <c>f"</c> (avec guillemet
    /// ASCII converti en <c>''</c> canonique). Optionnellement suivie
    /// d'arguments tight entre parenthèses : <c>f'(x)</c>, <c>f''(x)</c>.
    ///
    /// <para>Postfix pattern (vs head préfixe pour les autres templates).
    /// L'identifier est une lettre simple (f, g, h, etc.) suivie d'un ou
    /// plusieurs marqueurs <c>'</c> ou <c>"</c>.</para>
    ///
    /// <para>Limite : jusqu'à 4 marqueurs (= <c>f''''</c>). Au-delà,
    /// notation peu lisible — l'user utilise <c>f^(5)</c> typiquement.</para>
    ///
    /// <para>Rendu LaTeX : <c>f'</c>, <c>f''</c>, <c>f'''</c>, <c>f''''</c>.
    /// Les <c>'</c> sont natifs LaTeX math mode. Si l'user tape <c>"</c>
    /// (guillemet ASCII), c'est converti en <c>''</c> canonique.</para>
    ///
    /// <para>Cf. ADR <c>2026-05-21-Feat-primed-derivative-and-double-integral</c>
    /// (P9g + P9h).</para>
    /// </summary>
    public sealed class PrimedDerivativeTemplate : ArgListPatternBase
    {
        public override string TemplateId => "primed-derivative";

        // Pas de heads literals — c'est un pattern postfix sur identifier.
        // Le TryMatchHead override scanne <letter><'+ ou "+>.
        protected override IReadOnlyList<QuantifierVariant> Heads
            => System.Array.Empty<QuantifierVariant>();

        public override PatternMatch? TryMatchHead(PatternScanContext ctx)
        {
            if (ctx == null) return null;
            var src = ctx.Source;
            if (string.IsNullOrEmpty(src)) return null;

            for (int i = ctx.StartPos; i < src.Length; i++)
            {
                if (!char.IsLetter(src[i])) continue;
                // Boundary gauche : pas de lettre/digit avant
                if (i > 0 && char.IsLetterOrDigit(src[i - 1])) continue;

                int j = i + 1;
                int primesCount = 0;
                while (j < src.Length)
                {
                    int countForChar = CountPrimesForChar(src[j]);
                    if (countForChar == 0) break;
                    primesCount += countForChar;
                    j++;
                }
                if (primesCount == 0) continue; // pas primed
                if (primesCount > 4) continue;  // limite lisibilité

                // Optionnel : args si ( tight après les primes
                int argsEnd = j;
                string? funcArgs = null;
                int argsStart = -1;
                if (j < src.Length && src[j] == '(')
                {
                    int closeIdx = FindMatchingClose(src, j, '(', ')');
                    if (closeIdx > j)
                    {
                        argsStart = j + 1;
                        funcArgs = closeIdx > j + 1
                            ? src.Substring(j + 1, closeIdx - j - 1)
                            : string.Empty;
                        argsEnd = closeIdx + 1;
                    }
                }

                var slots = new Dictionary<string, SlotValue>(3)
                {
                    ["function"] = new FilledSlotAtom(src[i].ToString(), i, i + 1),
                    ["primes_count"] = new FilledSlotAtom(primesCount.ToString(),
                        i + 1, j),
                };
                if (funcArgs != null)
                {
                    slots["func_args"] = new FilledSlotAtom(funcArgs,
                        argsStart, argsStart + funcArgs.Length);
                }

                return new PatternMatch(
                    templateId: TemplateId,
                    sourceStart: i,
                    sourceEnd: argsEnd,
                    slots: slots,
                    isComplete: true);
            }
            return null;
        }

        public override IReadOnlyList<PatternCompletion> Expand(
            PatternMatch state, PatternScanContext ctx)
        {
            if (state == null || ctx == null) return System.Array.Empty<PatternCompletion>();
            if (!state.Slots.TryGetValue("function", out var fnSlot)
                || fnSlot is not FilledSlotAtom fn) return System.Array.Empty<PatternCompletion>();
            if (!state.Slots.TryGetValue("primes_count", out var cntSlot)
                || cntSlot is not FilledSlotAtom cnt
                || !int.TryParse(cnt.Text, out int count))
                return System.Array.Empty<PatternCompletion>();

            string? funcArgs = null;
            if (state.Slots.TryGetValue("func_args", out var argsSlot)
                && argsSlot is FilledSlotAtom argsAtom)
                funcArgs = argsAtom.Text;

            string primes = new string('\'', count);
            string fnText = fn.Text;

            string preview = funcArgs != null
                ? $"{fnText}{primes}({funcArgs})"
                : $"{fnText}{primes}";
            string hint = preview; // pas de slot incomplet ici
            string description = BuildDescription(fnText, count, funcArgs);

            // Mutation : si l'user a tapé " (guillemet ASCII), normalise en ''
            // canonique. Sinon, source brute = source mutée (no-op).
            SourceMutation? mutation = BuildMutation(state, fnText, count, funcArgs, ctx);

            return new[] { new PatternCompletion(
                description: description,
                previewLatex: preview,
                hintLatex: hint,
                mutation: mutation,
                completenessScore: 100) };
        }

        private static string BuildDescription(string fn, int count, string? args)
        {
            string suffix = count switch
            {
                1 => "′",
                2 => "″",
                3 => "‴",
                4 => "⁗",
                _ => new string('′', count),
            };
            return args != null ? $"{fn}{suffix}({args})" : $"{fn}{suffix}";
        }

        private static SourceMutation? BuildMutation(
            PatternMatch state, string fn, int count, string? args, PatternScanContext ctx)
        {
            int parentStart = state.SourceStart;
            int parentEnd = state.SourceEnd;
            if (parentStart < 0 || parentEnd > ctx.Source.Length
                || parentEnd <= parentStart) return null;

            // Source canonique : <fn><'count><(args)?>
            var sb = new StringBuilder();
            sb.Append(fn);
            sb.Append(new string('\'', count));
            if (args != null)
            {
                sb.Append("(").Append(args).Append(")");
            }
            return new SourceMutation(parentStart, parentEnd - parentStart, sb.ToString());
        }

        /// <summary>
        /// Compte le nombre de primes équivalent pour un caractère donné.
        /// Word auto-corrige souvent <c>'</c> ASCII en <c>'</c> (U+2019)
        /// typographique, et <c>"</c> en <c>"</c>/<c>"</c>. Cette méthode
        /// reconnaît toutes les variantes pour rester robuste.
        ///
        /// <list type="bullet">
        ///   <item>1 prime : <c>'</c> (U+0027 ASCII), <c>'</c> (U+2019),
        ///     <c>'</c> (U+2018), <c>′</c> (U+2032 math prime)</item>
        ///   <item>2 primes : <c>"</c> (U+0022 ASCII), <c>"</c> (U+201D),
        ///     <c>"</c> (U+201C), <c>″</c> (U+2033 math double prime)</item>
        ///   <item>3 primes : <c>‴</c> (U+2034 triple prime)</item>
        ///   <item>4 primes : <c>⁗</c> (U+2057 quadruple prime)</item>
        ///   <item>0 (= pas un marqueur prime) : tout autre caractère</item>
        /// </list>
        /// </summary>
        private static int CountPrimesForChar(char c)
        {
            return c switch
            {
                '\'' => 1,      // U+0027 apostrophe ASCII
                '’' => 1,  // ’ right single quotation mark (= auto-correct Word)
                '‘' => 1,  // ‘ left single quotation mark
                '′' => 1,  // ′ prime math symbol
                '"' => 2,       // U+0022 quotation mark ASCII
                '”' => 2,  // ” right double quotation mark
                '“' => 2,  // “ left double quotation mark
                '″' => 2,  // ″ double prime
                '‴' => 3,  // ‴ triple prime
                '⁗' => 4,  // ⁗ quadruple prime
                _ => 0,
            };
        }

        /// <summary>
        /// Trouve la position du caractère fermant qui correspond au caractère
        /// ouvrant à <paramref name="openIdx"/>, en respectant les imbrications.
        /// Retourne -1 si pas de close trouvé (= source malformée).
        /// </summary>
        private static int FindMatchingClose(string src, int openIdx, char open, char close)
        {
            int depth = 1;
            for (int i = openIdx + 1; i < src.Length; i++)
            {
                if (src[i] == open) depth++;
                else if (src[i] == close)
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }
    }
}
