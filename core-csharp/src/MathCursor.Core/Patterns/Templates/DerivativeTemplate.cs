using System.Collections.Generic;
using System.Text;
using MathCursor.Core.Lattice;

namespace MathCursor.Core.Patterns.Templates
{
    /// <summary>
    /// Pattern <c>"derivative"</c> : dérivée d'une expression par rapport à
    /// une variable. 2 slots positionnels tous requis. Hérite d'
    /// <see cref="ArgListPatternBase"/>.
    ///
    /// <para>Syntaxe : <c>Derive &lt;var&gt; &lt;expression&gt;</c>.
    /// Rendu LaTeX : <c>\frac{d}{d&lt;var&gt;} &lt;expression&gt;</c>.</para>
    ///
    /// <para>Exemples :</para>
    /// <list type="bullet">
    ///   <item><c>Derive</c> → <c>\frac{d}{d▭} ▭</c> (template complet)</item>
    ///   <item><c>Derive x</c> → <c>\frac{d}{dx} ▭</c></item>
    ///   <item><c>Derive x f(x)</c> → <c>\frac{d}{dx} f(x)</c> (complet)</item>
    ///   <item><c>Derive t e^t</c> → <c>\frac{d}{dt} e^t</c></item>
    ///   <item><c>Derive x x²+1</c> → <c>\frac{d}{dx} x²+1</c> (expr multi-tokens)</item>
    /// </list>
    ///
    /// <para>Heads supportés : <c>Derive</c> (canonique), <c>derive</c> (alias),
    /// <c>dérivée</c> (FR), <c>dérive</c> (FR).</para>
    ///
    /// <para>Cf. ADR <c>2026-05-21-Feat-derivative-pattern</c> (P9d).</para>
    /// </summary>
    public sealed class DerivativeTemplate : ArgListPatternBase
    {
        public override string TemplateId => "derivative";

        private static readonly QuantifierVariant[] _variants = new[]
        {
            new QuantifierVariant("Derive", "\\frac{d}{d}", "derive", weight: 100),
            new QuantifierVariant("derive", "\\frac{d}{d}", "derive", weight: 95),
            new QuantifierVariant("dérivée", "\\frac{d}{d}", "derive", weight: 90),
            new QuantifierVariant("dérive", "\\frac{d}{d}", "derive", weight: 85),
        };

        protected override IReadOnlyList<QuantifierVariant> Heads => _variants;

        public override IReadOnlyList<PatternCompletion> Expand(
            PatternMatch state, PatternScanContext ctx)
        {
            if (state == null || ctx == null) return System.Array.Empty<PatternCompletion>();
            var variant = FindVariantForState(state, _variants);
            if (variant == null) return System.Array.Empty<PatternCompletion>();

            var args = ParseArgs(ctx.Source, state.SourceEnd);

            string? varText = args.Count >= 1 ? args[0].Text : null;
            string? exprText = args.Count >= 2 ? ConcatArgsFrom(args, 1, ctx.Source) : null;

            int sourceEnd = args.Count > 0
                ? args[args.Count - 1].End
                : state.SourceEnd;

            int filledSlots = (varText != null ? 1 : 0)
                + (exprText != null ? 1 : 0);
            int score = 33 + filledSlots * 33; // 33/66/99

            string preview = BuildLatex(varText, exprText, hideEmpty: true);
            string hint = BuildLatex(varText, exprText, hideEmpty: false);
            string description = BuildDescription(varText, exprText);
            SourceMutation? mutation = BuildMutation(
                state, variant, varText, exprText, sourceEnd, ctx);

            return new[] { new PatternCompletion(
                description: description,
                previewLatex: preview,
                hintLatex: hint,
                mutation: mutation,
                completenessScore: filledSlots == 2 ? 100 : score) };
        }

        private static string BuildLatex(
            string? varText, string? exprText, bool hideEmpty)
        {
            var sb = new StringBuilder();
            sb.Append("\\frac{d}{d");
            sb.Append(varText ?? (hideEmpty ? "" : "\\square"));
            sb.Append("} ");
            sb.Append(exprText ?? (hideEmpty ? "" : "\\square"));
            return sb.ToString();
        }

        private static string BuildDescription(string? varText, string? exprText)
        {
            var sb = new StringBuilder();
            sb.Append("d/d");
            sb.Append(varText ?? "▭");
            sb.Append(" ");
            sb.Append(exprText ?? "▭");
            return sb.ToString();
        }

        private static SourceMutation? BuildMutation(
            PatternMatch state, QuantifierVariant variant,
            string? varText, string? exprText,
            int sourceEnd, PatternScanContext ctx)
        {
            int parentStart = state.SourceStart;
            int parentEnd = sourceEnd > state.SourceEnd ? sourceEnd : state.SourceEnd;
            if (parentStart < 0 || parentEnd > ctx.Source.Length || parentEnd <= parentStart)
                return null;

            var sb = new StringBuilder();
            sb.Append(variant.MutationReplacement);
            if (varText != null) sb.Append(" ").Append(varText);
            if (exprText != null) sb.Append(" ").Append(exprText);

            return new SourceMutation(parentStart, parentEnd - parentStart, sb.ToString());
        }
    }
}
