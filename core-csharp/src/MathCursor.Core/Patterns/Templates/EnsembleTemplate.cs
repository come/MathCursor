using System.Collections.Generic;
using MathCursor.Core.Lattice;

namespace MathCursor.Core.Patterns.Templates
{
    /// <summary>
    /// Pattern <c>"ensemble"</c> : lettre canonique d'ensemble mathématique
    /// (<c>R</c>, <c>N</c>, <c>Z</c>, <c>Q</c>, <c>C</c>) avec modifiers
    /// optionnels (<c>*</c>, <c>+</c>, <c>-</c>, 1 ou 2 max). Produit la forme
    /// <c>\mathbb{X}</c> avec exposant pour les modifiers.
    ///
    /// <para>P3 minimal : heads alphabétiques uniquement. P4
    /// <c>IntervalUnionTemplate</c> ajoutera la délégation vers <c>[</c>.</para>
    ///
    /// <para>Pattern autonome (peut être déclenché directement) et
    /// compositionnel (consommé comme sub-pattern par <c>ForallBelongsTemplate</c>
    /// via <see cref="PatternRefSlot"/>("ensemble") en P5).</para>
    ///
    /// <para>Convention alignée sur <c>ScanCanonicalSetLetters</c> (legacy
    /// ambig closed) qui sera retiré en P6 : word boundary à gauche, délim
    /// terminal à droite (whitespace, ponctuation, fermeture). La
    /// <see cref="SourceMutation"/> émise transforme <c>R*</c> →
    /// <c>bbR*</c> — le pipeline lattice rendra ensuite <c>\mathbb{R}^*</c>.</para>
    ///
    /// <para>Cf. ADR <c>2026-05-21-Meta-pattern-templates-vs-ambig-closed</c>,
    /// étape P3.</para>
    /// </summary>
    public sealed class EnsembleTemplate : IPatternTemplate
    {
        public string TemplateId => "ensemble";
        public int Order => 0;

        public PatternMatch? TryMatchHead(PatternScanContext ctx)
        {
            if (ctx == null) return null;
            var source = ctx.Source;
            if (string.IsNullOrEmpty(source)) return null;

            for (int i = ctx.StartPos; i < source.Length; i++)
            {
                char head = source[i];

                // P5.2 : délégation à interval-union pour les brackets si le
                // Registry est fourni dans le ctx. EnsembleTemplate devient
                // un dispatcher entre lettres canoniques et intervalles.
                if ((head == '[' || head == '(') && ctx.Registry != null)
                {
                    var intervalTemplate = ctx.Registry.Get("interval-union");
                    if (intervalTemplate != null)
                    {
                        var subCtx = ctx.WithStartPos(i);
                        var subMatch = intervalTemplate.TryMatchHead(subCtx);
                        if (subMatch != null)
                        {
                            // Wrap : ensemble match qui pointe vers le sub-match
                            // interval-union via slot « delegated ». Expand
                            // d'EnsembleTemplate forwarde à interval-union.Expand
                            // (qui parsera la suite à partir de subMatch.SourceEnd).
                            var slots = new Dictionary<string, SlotValue>(1)
                            {
                                ["delegated"] = new FilledSlotSubPattern(subMatch),
                            };
                            return new PatternMatch(
                                templateId: TemplateId,
                                sourceStart: i,
                                sourceEnd: subMatch.SourceEnd,
                                slots: slots,
                                isComplete: subMatch.IsComplete);
                        }
                    }
                    // Bracket sans Registry interval-union → fallback, on essaie
                    // les lettres canoniques (qui rejetteront ce char et continueront).
                    continue;
                }

                if (!IsCanonicalLetter(head)) continue;
                if (i > 0 && char.IsLetter(source[i - 1])) continue; // word boundary

                // Modifiers tight : 1 ou 2 max parmi * + -.
                int j = i + 1;
                while (j < source.Length
                       && (j - (i + 1)) < 2
                       && IsModifier(source[j]))
                    j++;

                // Terminal à droite : EOF ou délimiteur ensemble.
                if (j < source.Length && !IsTerminalDelimiter(source[j])) continue;

                return new PatternMatch(
                    templateId: TemplateId,
                    sourceStart: i,
                    sourceEnd: j,
                    slots: EmptySlots.Instance,
                    isComplete: true);
            }
            return null;
        }

        public IReadOnlyList<PatternCompletion> Expand(PatternMatch state, PatternScanContext ctx)
        {
            if (state == null || ctx == null) return System.Array.Empty<PatternCompletion>();

            // P5.2 : si délégué à interval-union, forwarder.
            if (state.Slots.TryGetValue("delegated", out var delegated)
                && delegated is FilledSlotSubPattern delSub)
            {
                var intervalTemplate = ctx.Registry?.Get("interval-union");
                if (intervalTemplate == null) return System.Array.Empty<PatternCompletion>();
                return intervalTemplate.Expand(delSub.Sub, ctx);
            }

            int len = state.SourceEnd - state.SourceStart;
            if (len < 1 || state.SourceStart < 0
                || state.SourceEnd > ctx.Source.Length) return System.Array.Empty<PatternCompletion>();

            char letter = ctx.Source[state.SourceStart];
            string modifiers = len > 1
                ? ctx.Source.Substring(state.SourceStart + 1, len - 1)
                : string.Empty;

            string previewLatex = BuildLatex(letter, modifiers);
            string description = BuildDescription(letter, modifiers);
            string replacement = "bb" + letter + modifiers;

            var completion = new PatternCompletion(
                description: description,
                previewLatex: previewLatex,
                hintLatex: previewLatex, // pas de slot vide → hint identique au preview
                mutation: new SourceMutation(state.SourceStart, len, replacement),
                completenessScore: 100);

            return new[] { completion };
        }

        // ─── Helpers ──────────────────────────────────────────────────

        private static bool IsCanonicalLetter(char c)
            => c == 'R' || c == 'N' || c == 'Z' || c == 'Q' || c == 'C';

        private static bool IsModifier(char c)
            => c == '*' || c == '+' || c == '-';

        // Caractères qui suivent légitimement une lettre canonique en contexte
        // « ensemble ». Whitespace, ponctuation, fermeture de groupement.
        // Les opérateurs math (+ - * / ^ _) sont EXCLUS pour préserver les
        // formules variables (pi*R², 2N+1) — sauf que +, -, * peuvent être
        // des modifiers tight, capturés en amont.
        private static bool IsTerminalDelimiter(char c)
            => char.IsWhiteSpace(c)
               || c == ',' || c == ';' || c == '.'
               || c == ')' || c == ']' || c == '}';

        private static string BuildLatex(char letter, string modifiers)
        {
            string baseRender = "\\mathbb{" + letter + "}";
            if (modifiers.Length == 0) return baseRender;
            if (modifiers.Length == 1) return baseRender + "^" + modifiers;
            return baseRender + "^{" + modifiers + "}";
        }

        private static string BuildDescription(char letter, string modifiers)
        {
            string baseDesc = letter switch
            {
                'R' => "ℝ",
                'N' => "ℕ",
                'Z' => "ℤ",
                'Q' => "ℚ",
                'C' => "ℂ",
                _ => letter.ToString(),
            };
            return baseDesc + modifiers;
        }
    }
}

