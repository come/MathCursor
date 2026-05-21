using System.Collections.Generic;
using System.Text;
using MathCursor.Core.Lattice;

namespace MathCursor.Core.Patterns.Templates
{
    /// <summary>
    /// Pattern <c>"lim"</c> : limite mathématique avec 3 slots positionnels
    /// tous requis. Convention args espace héritée d'<see cref="ArgListPatternBase"/>.
    ///
    /// <para>Syntaxe : <c>Lim &lt;var&gt; &lt;limit&gt; &lt;expression&gt;</c>.
    /// Rendu LaTeX : <c>\lim_{var \to limit} expression</c>.</para>
    ///
    /// <para>Exemples :</para>
    /// <list type="bullet">
    ///   <item><c>Lim</c> → <c>\lim_{▭ \to ▭} ▭</c> (hint template complet)</item>
    ///   <item><c>Lim x</c> → <c>\lim_{x \to ▭} ▭</c></item>
    ///   <item><c>Lim x 0</c> → <c>\lim_{x \to 0} ▭</c></item>
    ///   <item><c>Lim x 0 f(x)</c> → <c>\lim_{x \to 0} f(x)</c> (complet)</item>
    ///   <item><c>Lim x +oo 1/x</c> → <c>\lim_{x \to +\infty} 1/x</c> (conversion infini)</item>
    ///   <item><c>Lim x -oo g(x)</c> → <c>\lim_{x \to -\infty} g(x)</c></item>
    /// </list>
    ///
    /// <para>Heads supportés : <c>Lim</c> (majuscule = convention MathCursor
    /// pour les patterns structurels) et <c>lim</c> (minuscule = alias
    /// rétro-compat avec convention LaTeX standard).</para>
    ///
    /// <para>Discrimination des slots : positions strictes — pas de
    /// classification dernier-arg-est-domain (vs forall). 3 slots tous requis.
    /// Si l'user a tapé moins de 3 args, les manquants sont rendus en carré
    /// `\square` dans HintLatex.</para>
    ///
    /// <para>Cf. ADR <c>2026-05-21-Feat-lim-pattern</c> (P9a).</para>
    /// </summary>
    public sealed class LimTemplate : ArgListPatternBase
    {
        public override string TemplateId => "lim";

        private static readonly QuantifierVariant[] _variants = new[]
        {
            new QuantifierVariant("Lim", "\\lim", "lim", weight: 100),
            new QuantifierVariant("lim", "\\lim", "lim", weight: 95),
        };

        protected override IReadOnlyList<QuantifierVariant> Heads => _variants;

        public override IReadOnlyList<PatternCompletion> Expand(
            PatternMatch state, PatternScanContext ctx)
        {
            if (state == null || ctx == null) return System.Array.Empty<PatternCompletion>();
            var variant = FindVariantForState(state, _variants);
            if (variant == null) return System.Array.Empty<PatternCompletion>();

            // Parse args (3 slots positionnels). Le reste post-arg-3 fusionne
            // dans le slot expression (= permet "Lim x 0 f x" sans paren).
            var args = ParseArgs(ctx.Source, state.SourceEnd);

            string? varText = args.Count >= 1 ? args[0].Text : null;
            string? limitText = args.Count >= 2 ? args[1].Text : null;
            string? exprText = args.Count >= 3 ? ConcatArgsFrom(args, 2, ctx.Source) : null;

            int sourceEnd = args.Count > 0
                ? args[args.Count - 1].End
                : state.SourceEnd;

            // État final pour completeness
            int filledSlots = (varText != null ? 1 : 0)
                + (limitText != null ? 1 : 0)
                + (exprText != null ? 1 : 0);
            bool isComplete = filledSlots == 3;

            string preview = BuildLatex(varText, limitText, exprText, hideEmpty: true);
            string hint = BuildLatex(varText, limitText, exprText, hideEmpty: false);
            string description = BuildDescription(varText, limitText, exprText);
            SourceMutation? mutation = BuildMutation(
                state, variant, varText, limitText, exprText, sourceEnd, ctx);
            int score = 25 + filledSlots * 25; // 25/50/75/100

            return new[] { new PatternCompletion(
                description: description,
                previewLatex: preview,
                hintLatex: hint,
                mutation: mutation,
                completenessScore: score) };
        }

        // ─── Helpers (helpers communs Convert* / ConcatArgsFrom remontés
        //     dans ArgListPatternBase en P9b 2026-05-21). ─────────────

        private static string BuildLatex(
            string? varText, string? limitText, string? exprText, bool hideEmpty)
        {
            var sb = new StringBuilder();
            sb.Append("\\lim_{");
            sb.Append(varText ?? (hideEmpty ? "" : "\\square"));
            sb.Append(" \\to ");
            sb.Append(ConvertInfinityToken(limitText) ?? (hideEmpty ? "" : "\\square"));
            sb.Append("} ");
            sb.Append(exprText ?? (hideEmpty ? "" : "\\square"));
            return sb.ToString();
        }

        private static string BuildDescription(
            string? varText, string? limitText, string? exprText)
        {
            var sb = new StringBuilder();
            sb.Append("lim_");
            sb.Append(varText ?? "▭");
            sb.Append("→");
            // Unicode-friendly for description : oo → ∞
            string lim = ConvertInfinityToUnicode(limitText) ?? "▭";
            sb.Append(lim);
            sb.Append(" ");
            sb.Append(exprText ?? "▭");
            return sb.ToString();
        }

        private static SourceMutation? BuildMutation(
            PatternMatch state, QuantifierVariant variant,
            string? varText, string? limitText, string? exprText,
            int sourceEnd, PatternScanContext ctx)
        {
            // Composite : "Lim x 0 f(x)" → "lim x 0 f(x)" (= juste le head muté
            // en keyword vocab `lim`). Le pipeline lattice connait `lim` et
            // rendra `\lim_{x \to 0} f(x)` correctement quand muté.
            // P9a minimal : on ne touche pas aux tokens internes (infini, etc.)
            // — la conversion +oo → +∞ se fait au niveau du rendu LaTeX
            // (PreviewLatex/HintLatex). La source source-mutée reste lisible.
            int parentStart = state.SourceStart;
            int parentEnd = sourceEnd > state.SourceEnd ? sourceEnd : state.SourceEnd;
            if (parentStart < 0 || parentEnd > ctx.Source.Length || parentEnd <= parentStart)
                return null;

            var sb = new StringBuilder();
            sb.Append(variant.MutationReplacement);
            if (varText != null) sb.Append(" ").Append(varText);
            if (limitText != null) sb.Append(" ").Append(limitText);
            if (exprText != null) sb.Append(" ").Append(exprText);

            return new SourceMutation(parentStart, parentEnd - parentStart, sb.ToString());
        }
    }
}
