using System.Collections.Generic;
using System.Text;
using MathCursor.Core.Lattice;

namespace MathCursor.Core.Patterns.Templates
{
    /// <summary>
    /// Pattern <c>"integral"</c> : intégrale définie avec 4 slots positionnels
    /// tous requis. Hérite d'<see cref="ArgListPatternBase"/>.
    ///
    /// <para>Syntaxe : <c>Int &lt;var&gt; &lt;from&gt; &lt;to&gt; &lt;expression&gt;</c>.
    /// Rendu LaTeX : <c>\int_{from}^{to} expression \, d&lt;var&gt;</c>.
    /// La variable est en premier (= cohérence MathCursor avec Lim/Sum)
    /// même si la convention LaTeX la place en fin via <c>dx</c>.</para>
    ///
    /// <para>Exemples :</para>
    /// <list type="bullet">
    ///   <item><c>Int</c> → <c>\int_{▭}^{▭} ▭ \, d▭</c> (template complet)</item>
    ///   <item><c>Int x 0 1 f(x)</c> → <c>\int_{0}^{1} f(x) \, dx</c></item>
    ///   <item><c>Int t -oo +oo e^(-t²)</c> → <c>\int_{-\infty}^{+\infty} e^(-t²) \, dt</c></item>
    /// </list>
    ///
    /// <para>Heads supportés : <c>Int</c> (canonique), <c>int</c> (alias),
    /// <c>intégrale</c> (FR), <c>∫</c> (unicode).</para>
    ///
    /// <para>Cf. ADR <c>2026-05-21-Feat-integral-pattern</c> (P9c).</para>
    /// </summary>
    public sealed class IntegralTemplate : ArgListPatternBase
    {
        public override string TemplateId => "integral";

        private static readonly QuantifierVariant[] _variants = new[]
        {
            new QuantifierVariant("Int", "\\int", "int", weight: 100),
            new QuantifierVariant("int", "\\int", "int", weight: 95),
            new QuantifierVariant("intégrale", "\\int", "int", weight: 90),
            new QuantifierVariant("∫", "\\int", "int", weight: 100),
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
            string? fromText = args.Count >= 2 ? args[1].Text : null;
            string? toText = args.Count >= 3 ? args[2].Text : null;
            string? exprText = args.Count >= 4 ? ConcatArgsFrom(args, 3, ctx.Source) : null;

            int sourceEnd = args.Count > 0
                ? args[args.Count - 1].End
                : state.SourceEnd;

            int filledSlots = (varText != null ? 1 : 0)
                + (fromText != null ? 1 : 0)
                + (toText != null ? 1 : 0)
                + (exprText != null ? 1 : 0);
            int score = 20 + filledSlots * 20;

            string preview = BuildLatex(varText, fromText, toText, exprText, hideEmpty: true);
            string hint = BuildLatex(varText, fromText, toText, exprText, hideEmpty: false);
            string description = BuildDescription(varText, fromText, toText, exprText);
            SourceMutation? mutation = BuildMutation(
                state, variant, varText, fromText, toText, exprText, sourceEnd, ctx);

            return new[] { new PatternCompletion(
                description: description,
                previewLatex: preview,
                hintLatex: hint,
                mutation: mutation,
                completenessScore: score) };
        }

        private static string BuildLatex(
            string? varText, string? fromText, string? toText, string? exprText, bool hideEmpty)
        {
            var sb = new StringBuilder();
            sb.Append("\\int_{");
            sb.Append(ConvertInfinityToken(fromText) ?? (hideEmpty ? "" : "\\square"));
            sb.Append("}^{");
            sb.Append(ConvertInfinityToken(toText) ?? (hideEmpty ? "" : "\\square"));
            sb.Append("} ");
            sb.Append(exprText ?? (hideEmpty ? "" : "\\square"));
            sb.Append(" \\, d");
            sb.Append(varText ?? (hideEmpty ? "" : "\\square"));
            return sb.ToString();
        }

        private static string BuildDescription(
            string? varText, string? fromText, string? toText, string? exprText)
        {
            var sb = new StringBuilder();
            sb.Append("∫_{");
            sb.Append(ConvertInfinityToUnicode(fromText) ?? "▭");
            sb.Append("}^{");
            sb.Append(ConvertInfinityToUnicode(toText) ?? "▭");
            sb.Append("} ");
            sb.Append(exprText ?? "▭");
            sb.Append(" d");
            sb.Append(varText ?? "▭");
            return sb.ToString();
        }

        private static SourceMutation? BuildMutation(
            PatternMatch state, QuantifierVariant variant,
            string? varText, string? fromText, string? toText, string? exprText,
            int sourceEnd, PatternScanContext ctx)
        {
            int parentStart = state.SourceStart;
            int parentEnd = sourceEnd > state.SourceEnd ? sourceEnd : state.SourceEnd;
            if (parentStart < 0 || parentEnd > ctx.Source.Length || parentEnd <= parentStart)
                return null;

            var sb = new StringBuilder();
            sb.Append(variant.MutationReplacement);
            if (varText != null) sb.Append(" ").Append(varText);
            if (fromText != null) sb.Append(" ").Append(fromText);
            if (toText != null) sb.Append(" ").Append(toText);
            if (exprText != null) sb.Append(" ").Append(exprText);

            return new SourceMutation(parentStart, parentEnd - parentStart, sb.ToString());
        }
    }
}
