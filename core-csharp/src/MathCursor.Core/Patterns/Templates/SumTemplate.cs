using System.Collections.Generic;
using System.Text;
using MathCursor.Core.Lattice;

namespace MathCursor.Core.Patterns.Templates
{
    /// <summary>
    /// Pattern <c>"sum"</c> : sommation mathématique avec 4 slots positionnels
    /// tous requis. Hérite d'<see cref="ArgListPatternBase"/> (convention args
    /// espace).
    ///
    /// <para>Syntaxe : <c>Sum &lt;var&gt; &lt;from&gt; &lt;to&gt; &lt;expression&gt;</c>.
    /// Rendu LaTeX : <c>\sum_{var=from}^{to} expression</c>. Le <c>=</c>
    /// entre var et from est implicite (= convention LaTeX, pas tapé par
    /// l'user).</para>
    ///
    /// <para>Exemples :</para>
    /// <list type="bullet">
    ///   <item><c>Sum</c> → <c>\sum_{▭=▭}^{▭} ▭</c> (template complet)</item>
    ///   <item><c>Sum k</c> → <c>\sum_{k=▭}^{▭} ▭</c></item>
    ///   <item><c>Sum k 0</c> → <c>\sum_{k=0}^{▭} ▭</c></item>
    ///   <item><c>Sum k 0 n</c> → <c>\sum_{k=0}^{n} ▭</c></item>
    ///   <item><c>Sum k 0 n k²</c> → <c>\sum_{k=0}^{n} k²</c> (complet)</item>
    ///   <item><c>Sum n 1 +oo 1/n²</c> → <c>\sum_{n=1}^{+\infty} 1/n²</c></item>
    /// </list>
    ///
    /// <para>Heads supportés : <c>Sum</c> (canonique), <c>sum</c> (alias EN
    /// LaTeX standard), <c>somme</c> (FR), <c>Σ</c>/<c>∑</c> (unicode direct).</para>
    ///
    /// <para>Cf. ADR <c>2026-05-21-Feat-sum-pattern</c> (P9b).</para>
    /// </summary>
    public sealed class SumTemplate : ArgListPatternBase
    {
        public override string TemplateId => "sum";

        private static readonly QuantifierVariant[] _variants = new[]
        {
            new QuantifierVariant("Sum", "\\sum", "sum", weight: 100),
            new QuantifierVariant("sum", "\\sum", "sum", weight: 95),
            new QuantifierVariant("somme", "\\sum", "sum", weight: 90),
            new QuantifierVariant("∑", "\\sum", "sum", weight: 100),
            new QuantifierVariant("Σ", "\\sum", "sum", weight: 100),
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
            int score = 20 + filledSlots * 20; // 20/40/60/80/100

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
            sb.Append("\\sum_{");
            sb.Append(varText ?? (hideEmpty ? "" : "\\square"));
            sb.Append("=");
            sb.Append(ConvertInfinityToken(fromText) ?? (hideEmpty ? "" : "\\square"));
            sb.Append("}^{");
            sb.Append(ConvertInfinityToken(toText) ?? (hideEmpty ? "" : "\\square"));
            sb.Append("} ");
            sb.Append(exprText ?? (hideEmpty ? "" : "\\square"));
            return sb.ToString();
        }

        private static string BuildDescription(
            string? varText, string? fromText, string? toText, string? exprText)
        {
            var sb = new StringBuilder();
            sb.Append("Σ_{");
            sb.Append(varText ?? "▭");
            sb.Append("=");
            sb.Append(ConvertInfinityToUnicode(fromText) ?? "▭");
            sb.Append("}^{");
            sb.Append(ConvertInfinityToUnicode(toText) ?? "▭");
            sb.Append("} ");
            sb.Append(exprText ?? "▭");
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
