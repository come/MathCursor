using System.Collections.Generic;
using System.Text;
using MathCursor.Core.Lattice;

namespace MathCursor.Core.Patterns.Templates
{
    /// <summary>
    /// Pattern <c>"forall-belongs"</c> : quantificateur (universel ou
    /// existentiel) sur une ou plusieurs variables, avec un slot optionnel
    /// d'appartenance à un ensemble. Forme idiomatique :
    /// <c>∀x [∈ E]</c>, <c>∃x,y [∈ E]</c>.
    ///
    /// <para><b>Convention args espacés (refacto 2026-05-21 P5R)</b> :
    /// l'utilisateur sépare les arguments par des espaces (et/ou virgules
    /// pour les vars), comme pour <c>Lim x 0 f(x)</c> ou <c>sum k 0 n k²</c>.
    /// Les openers textuels (<c>app a</c>, <c>appartient</c>, <c>dans</c>,
    /// <c>in</c>, <c>(-</c>, <c>∈</c>) ont été retirés — cohérent avec la
    /// doctrine "rapidité de saisie".</para>
    ///
    /// <para>Discrimination var vs domain : si le DERNIER arg matche un
    /// pattern <c>ensemble</c> (R/N/Z/Q/C avec/sans modifier, ou intervalle
    /// <c>[...]</c>/<c>[..]U[..]</c>), c'est le domain. Sinon tous les args
    /// = vars. Exemples :</para>
    /// <list type="bullet">
    ///   <item><c>V x</c> → <c>∀x</c> (1 var, pas de domain)</item>
    ///   <item><c>V x R</c> → <c>∀x ∈ ℝ</c> (var + domain ℝ)</item>
    ///   <item><c>V x y</c> → <c>∀x,y</c> (2 vars, y pas ensemble)</item>
    ///   <item><c>V x y R</c> → <c>∀x,y ∈ ℝ</c></item>
    ///   <item><c>V x [0,1]</c> → <c>∀x ∈ [0,1]</c></item>
    ///   <item><c>V x [0,1]U[3,4]</c> → <c>∀x ∈ [0,1]∪[3,4]</c></item>
    ///   <item><c>V x,y [0,1]</c> → <c>∀x,y ∈ [0,1]</c> (virgule = équivalente à espace pour vars)</item>
    /// </list>
    ///
    /// <para>Heads supportés (cf. <see cref="QuantifierVariant"/>) :
    /// <c>V</c>/<c>E</c> (raccourcis ASCII) et <c>∀</c>/<c>∃</c> (unicode
    /// direct). Le slot <c>polarity</c> capture le head choisi ; les
    /// LatexSymbol/MutationReplacement viennent du
    /// <see cref="QuantifierVariant"/> matché.</para>
    ///
    /// <para>Cf. ADRs : cadrage <c>2026-05-21-Meta-pattern-templates-vs-ambig-closed</c>
    /// + P5 <c>Feat-forall-belongs-pattern</c> + P5R refacto
    /// <c>Refactor-forall-belongs-arglist-convention</c>.</para>
    /// </summary>
    public sealed class ForallBelongsTemplate : ArgListPatternBase
    {
        public override string TemplateId => "forall-belongs";

        // ─── Data-ready variants (préparation YAML P9+) ───────────────

        private static readonly QuantifierVariant[] _variants = new[]
        {
            new QuantifierVariant("V", "\\forall", "forall", weight: 100),
            new QuantifierVariant("E", "\\exists", "exists", weight: 100),
            new QuantifierVariant("∀", "\\forall", "forall", weight: 100),
            new QuantifierVariant("∃", "\\exists", "exists", weight: 100),
        };

        protected override IReadOnlyList<QuantifierVariant> Heads => _variants;

        // ─── Expand ────────────────────────────────────────────────────

        public override IReadOnlyList<PatternCompletion> Expand(
            PatternMatch state, PatternScanContext ctx)
        {
            if (state == null || ctx == null) return System.Array.Empty<PatternCompletion>();
            var variant = FindVariantForState(state, _variants);
            if (variant == null) return System.Array.Empty<PatternCompletion>();

            // Parse + classifier les args depuis state.SourceEnd
            var rawArgs = ParseArgs(ctx.Source, state.SourceEnd);
            var classification = ClassifyArgs(rawArgs, ctx);

            // État final : remplit les slots var/domain selon la classification
            var slots = new Dictionary<string, SlotValue>(state.Slots.Count + 2);
            foreach (var kv in state.Slots) slots[kv.Key] = kv.Value;
            int finalSourceEnd = state.SourceEnd;

            // Slot "var" : concat des VarArgs séparés par virgules pour le rendu
            // (= convention LaTeX : `\forall x,y` ; espaces tolérés à l'entrée
            // mais on normalise en virgule)
            if (classification.VarArgs.Count > 0)
            {
                var varText = JoinVars(classification.VarArgs);
                var firstVar = classification.VarArgs[0];
                var lastVar = classification.VarArgs[classification.VarArgs.Count - 1];
                slots["var"] = new FilledSlotAtom(varText, firstVar.Start, lastVar.End);
                finalSourceEnd = lastVar.End;
            }
            else
            {
                slots["var"] = EmptySlot.Instance;
            }

            // Slot "domain" : sub-pattern ensemble si classification l'a identifié
            if (classification.DomainSubMatch != null && classification.DomainArg != null)
            {
                slots["domain"] = new FilledSlotSubPattern(classification.DomainSubMatch);
                finalSourceEnd = classification.DomainArg.End;
            }
            else
            {
                slots["domain"] = EmptySlot.Instance;
            }

            bool isComplete = classification.VarArgs.Count > 0
                && (classification.DomainSubMatch == null || classification.DomainSubMatch.IsComplete);

            var finalState = new PatternMatch(
                templateId: TemplateId,
                sourceStart: state.SourceStart,
                sourceEnd: finalSourceEnd,
                slots: slots,
                isComplete: isComplete);

            return new[] { BuildCompletion(finalState, variant, classification, ctx) };
        }

        private static string JoinVars(IReadOnlyList<ArgSpan> varArgs)
        {
            if (varArgs.Count == 1) return varArgs[0].Text;
            var sb = new StringBuilder();
            for (int i = 0; i < varArgs.Count; i++)
            {
                if (i > 0) sb.Append(',');
                // Normalise : si l'arg lui-même contient déjà des virgules
                // (ex. "x,y" tapé en 1 token), on l'ajoute tel quel.
                sb.Append(varArgs[i].Text);
            }
            return sb.ToString();
        }

        // ─── BuildCompletion (Latex + Mutation composite) ─────────────

        private static PatternCompletion BuildCompletion(
            PatternMatch state,
            QuantifierVariant variant,
            ArgClassification classification,
            PatternScanContext ctx)
        {
            // Sub-completion du domain (si présent) pour rendu LaTeX
            PatternCompletion? domainCompletion = null;
            if (classification.DomainSubMatch != null)
            {
                var ensembleTemplate = ctx.Registry?.Get("ensemble");
                var subCompletions = ensembleTemplate?.Expand(classification.DomainSubMatch, ctx);
                if (subCompletions != null && subCompletions.Count > 0)
                    domainCompletion = subCompletions[0];
            }

            string preview = BuildLatex(state, variant, classification, domainCompletion, hideEmpty: true);
            string hint = BuildLatex(state, variant, classification, domainCompletion, hideEmpty: false);
            string description = BuildDescription(variant, classification, domainCompletion);
            SourceMutation? mutation = BuildMutation(state, variant, classification, domainCompletion, ctx);
            int score = ComputeScore(classification);

            return new PatternCompletion(
                description: description,
                previewLatex: preview,
                hintLatex: hint,
                mutation: mutation,
                completenessScore: score);
        }

        private static string BuildLatex(
            PatternMatch state, QuantifierVariant variant,
            ArgClassification classification, PatternCompletion? domainCompletion, bool hideEmpty)
        {
            var sb = new StringBuilder();
            sb.Append(variant.LatexSymbol);

            string varText = JoinVarsForLatex(classification.VarArgs);
            if (!string.IsNullOrEmpty(varText))
                sb.Append(" ").Append(varText);
            else if (!hideEmpty)
                sb.Append(" \\square");

            if (classification.DomainSubMatch != null)
            {
                sb.Append(" \\in ");
                sb.Append(domainCompletion != null
                    ? (hideEmpty ? domainCompletion.PreviewLatex : domainCompletion.HintLatex)
                    : (hideEmpty ? "" : "\\square"));
            }
            return sb.ToString();
        }

        private static string JoinVarsForLatex(IReadOnlyList<ArgSpan> varArgs)
        {
            if (varArgs.Count == 0) return string.Empty;
            if (varArgs.Count == 1) return varArgs[0].Text;
            var sb = new StringBuilder();
            for (int i = 0; i < varArgs.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(varArgs[i].Text);
            }
            return sb.ToString();
        }

        private static string BuildDescription(
            QuantifierVariant variant, ArgClassification classification, PatternCompletion? domainCompletion)
        {
            var sb = new StringBuilder();
            sb.Append(variant.Head == "V" || variant.Head == "∀" ? "∀" : "∃");

            string varText = JoinVarsForLatex(classification.VarArgs);
            if (!string.IsNullOrEmpty(varText)) sb.Append(varText);
            else sb.Append("▭");

            if (classification.DomainSubMatch != null)
            {
                sb.Append("∈");
                sb.Append(domainCompletion?.Description ?? "▭");
            }
            return sb.ToString();
        }

        private static SourceMutation? BuildMutation(
            PatternMatch state, QuantifierVariant variant,
            ArgClassification classification, PatternCompletion? domainCompletion,
            PatternScanContext ctx)
        {
            // Composite : "V x R" → "forall x in bbR" (mutation couvrant
            // la zone source complète du pattern). Inclut la mutation
            // canonique du domain si disponible (= bbR), sinon source brute.
            var src = ctx.Source;
            int parentStart = state.SourceStart;
            int parentEnd = state.SourceEnd;
            if (parentStart < 0 || parentEnd > src.Length || parentEnd <= parentStart)
                return null;

            var sb = new StringBuilder();
            sb.Append(variant.MutationReplacement);

            string varText = JoinVarsForLatex(classification.VarArgs);
            if (!string.IsNullOrEmpty(varText))
                sb.Append(" ").Append(varText);

            if (classification.DomainSubMatch != null && classification.DomainArg != null)
            {
                sb.Append(" in ");
                if (domainCompletion?.Mutation != null)
                {
                    sb.Append(domainCompletion.Mutation.Replacement);
                }
                else
                {
                    var d = classification.DomainArg;
                    sb.Append(src.Substring(d.Start, d.End - d.Start));
                }
            }

            return new SourceMutation(parentStart, parentEnd - parentStart, sb.ToString());
        }

        private static int ComputeScore(ArgClassification classification)
        {
            int score = 25; // head matché
            if (classification.VarArgs.Count > 0) score += 25;
            if (classification.DomainSubMatch != null) score += 50;
            return score;
        }
    }
}
