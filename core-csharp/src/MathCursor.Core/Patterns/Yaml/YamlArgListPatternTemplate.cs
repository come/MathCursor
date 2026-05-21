using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using MathCursor.Core.Lattice;
using MathCursor.Core.Patterns.Templates;

namespace MathCursor.Core.Patterns.Yaml
{
    /// <summary>
    /// Template générique qui charge sa spec depuis un <see cref="PatternSpec"/>
    /// (YAML) et implémente <see cref="IPatternTemplate"/> via la spec. Hérite
    /// d'<see cref="ArgListPatternBase"/> pour réutiliser ParseArgs / ClassifyArgs
    /// / Convert*.
    ///
    /// <para>Slots positionnels remplis par <see cref="ArgListPatternBase.ParseArgs"/>.
    /// Le dernier slot avec <see cref="PatternSlotSpec.MultiToken"/> = true
    /// consomme tous les args restants (= expression multi-tokens).</para>
    ///
    /// <para>Render templates substituent les placeholders <c>&lt;name&gt;</c>
    /// (= valeur du slot, vide si non rempli) et <c>&lt;name|fallback&gt;</c>
    /// (= valeur ou fallback si vide). Pour les slots avec <c>convert: infinity</c>,
    /// la conversion <c>+oo</c> → <c>+\infty</c> est appliquée avant
    /// substitution.</para>
    ///
    /// <para>Cf. ADR <c>2026-05-21-Feat-yaml-pattern-specs</c> (P9e).</para>
    /// </summary>
    public sealed class YamlArgListPatternTemplate : ArgListPatternBase
    {
        private readonly PatternSpec _spec;
        private readonly QuantifierVariant[] _variants;
        // Regex compilée pour matcher les placeholders <name> ou <name|fallback>.
        // Capture group 1 = name, group 2 = fallback (optionnel).
        private static readonly Regex _placeholderRegex = new Regex(
            @"<([a-zA-Z_][a-zA-Z0-9_]*)(?:\|([^>]*))?>",
            RegexOptions.Compiled);

        public override string TemplateId => _spec.TemplateId;
        public override int Order => _spec.Order;
        protected override IReadOnlyList<QuantifierVariant> Heads => _variants;

        public YamlArgListPatternTemplate(PatternSpec spec)
        {
            _spec = spec ?? throw new System.ArgumentNullException(nameof(spec));
            _variants = new QuantifierVariant[spec.Heads.Count];
            for (int i = 0; i < spec.Heads.Count; i++)
            {
                var h = spec.Heads[i];
                _variants[i] = new QuantifierVariant(
                    head: h.Source,
                    latexSymbol: h.Latex,
                    mutationReplacement: h.Mutation,
                    weight: h.Weight);
            }
        }

        public override IReadOnlyList<PatternCompletion> Expand(
            PatternMatch state, PatternScanContext ctx)
        {
            if (state == null || ctx == null) return System.Array.Empty<PatternCompletion>();
            var variant = FindVariantForState(state, _variants);
            if (variant == null) return System.Array.Empty<PatternCompletion>();

            var args = ParseArgs(ctx.Source, state.SourceEnd);

            // Récupère la valeur de chaque slot positionnel selon la spec
            var slotValues = new Dictionary<string, string?>(_spec.Slots.Count);
            for (int i = 0; i < _spec.Slots.Count; i++)
            {
                var slot = _spec.Slots[i];
                string? value;
                if (slot.MultiToken)
                {
                    value = args.Count > slot.Position
                        ? ConcatArgsFrom(args, slot.Position, ctx.Source)
                        : null;
                }
                else
                {
                    value = args.Count > slot.Position ? args[slot.Position].Text : null;
                }
                slotValues[slot.Name] = value;
            }

            int sourceEnd = args.Count > 0
                ? args[args.Count - 1].End
                : state.SourceEnd;

            int filledSlots = 0;
            foreach (var kv in slotValues) if (kv.Value != null) filledSlots++;
            int score = _spec.Scoring.Base + filledSlots * _spec.Scoring.PerSlot;
            if (filledSlots == _spec.Slots.Count) score = 100;

            string preview = Render(_spec.Render.Preview, slotValues, applyFallbacks: false, asciiInfinity: false);
            string hint = Render(_spec.Render.Hint, slotValues, applyFallbacks: true, asciiInfinity: false);
            string description = Render(_spec.Render.Description, slotValues, applyFallbacks: true, asciiInfinity: true);

            SourceMutation? mutation = BuildMutation(state, variant, slotValues, sourceEnd, ctx);

            return new[] { new PatternCompletion(
                description: description,
                previewLatex: preview,
                hintLatex: hint,
                mutation: mutation,
                completenessScore: score) };
        }

        /// <summary>
        /// Substitue les placeholders dans <paramref name="template"/> avec les
        /// valeurs de <paramref name="slotValues"/>. Applique conversion infini
        /// si <c>convert: infinity</c> dans le slot spec.
        /// </summary>
        private string Render(
            string template,
            Dictionary<string, string?> slotValues,
            bool applyFallbacks,
            bool asciiInfinity)
        {
            return _placeholderRegex.Replace(template, match =>
            {
                string name = match.Groups[1].Value;
                string? fallback = match.Groups[2].Success ? match.Groups[2].Value : null;
                slotValues.TryGetValue(name, out var raw);

                // Conversion infini si slot a convert: infinity
                string? processed = raw;
                if (raw != null)
                {
                    var slot = FindSlotByName(name);
                    if (slot?.Convert == "infinity")
                    {
                        processed = asciiInfinity
                            ? ConvertInfinityToUnicode(raw)
                            : ConvertInfinityToken(raw);
                    }
                }

                if (processed != null) return processed;
                // Slot vide : applique fallback si demandé, sinon chaîne vide
                if (applyFallbacks && fallback != null) return fallback;
                return string.Empty;
            });
        }

        private PatternSlotSpec? FindSlotByName(string name)
        {
            foreach (var s in _spec.Slots)
                if (s.Name == name) return s;
            return null;
        }

        private SourceMutation? BuildMutation(
            PatternMatch state, QuantifierVariant variant,
            Dictionary<string, string?> slotValues, int sourceEnd, PatternScanContext ctx)
        {
            int parentStart = state.SourceStart;
            int parentEnd = sourceEnd > state.SourceEnd ? sourceEnd : state.SourceEnd;
            if (parentStart < 0 || parentEnd > ctx.Source.Length || parentEnd <= parentStart)
                return null;

            var sb = new StringBuilder();
            sb.Append(variant.MutationReplacement);
            // Ordre des slots positionnels = ordre Source d'origine
            foreach (var slot in _spec.Slots)
            {
                if (slotValues.TryGetValue(slot.Name, out var val) && val != null)
                    sb.Append(" ").Append(val);
            }
            return new SourceMutation(parentStart, parentEnd - parentStart, sb.ToString());
        }
    }
}
