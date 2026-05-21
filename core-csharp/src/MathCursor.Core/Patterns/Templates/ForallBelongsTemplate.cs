using System.Collections.Generic;
using System.Text;
using MathCursor.Core.Lattice;

namespace MathCursor.Core.Patterns.Templates
{
    /// <summary>
    /// Pattern <c>"forall-belongs"</c> : quantificateur (universel ou
    /// existentiel) sur une variable ou une liste de variables, avec un
    /// slot optionnel d'appartenance à un ensemble. Forme idiomatique :
    /// <c>∀x [∈ E]</c>, <c>∃x,y [∈ E]</c>.
    ///
    /// <para>Heads supportés (cf. <see cref="QuantifierVariant"/>) :
    /// <c>V</c>/<c>E</c> (raccourcis ASCII) et <c>∀</c>/<c>∃</c> (unicode
    /// direct). Le slot <c>polarity</c> capture le head choisi ; les
    /// LatexSymbol/MutationReplacement viennent du
    /// <see cref="QuantifierVariant"/> matché.</para>
    ///
    /// <para>Openers du slot <c>domain</c> (cf. <see cref="OpenerAlias"/>) :
    /// 6 alternatives <c>app a</c>, <c>appartient</c>, <c>dans</c>, <c>(-</c>,
    /// <c>∈</c>, <c>in</c>. Pondérées par weight pour la désambig — si
    /// plusieurs matchent à la même position (rare), le template émet
    /// plusieurs <see cref="PatternCompletion"/> triées par weight desc.</para>
    ///
    /// <para>Slot <c>domain</c> compositionnel : si un opener est trouvé, le
    /// template délègue à <c>Registry.Get("ensemble")</c> pour parser
    /// l'ensemble qui suit (lettres canoniques R/N/Z/Q/C ou intervals via
    /// délégation interne ensemble→interval-union).</para>
    ///
    /// <para><b>Approche data-ready</b> (option γ du plan P5) : variants et
    /// openers vivent comme <c>static readonly</c> arrays C# pour P5.
    /// Migration YAML (P9+) ne touche que la source de ces arrays ; le code
    /// du template reste identique.</para>
    ///
    /// <para>Cf. ADR <c>2026-05-21-Meta-pattern-templates-vs-ambig-closed</c>
    /// (cadrage) et <c>2026-05-21-Feat-forall-belongs-pattern</c> (livraison
    /// P5).</para>
    /// </summary>
    public sealed class ForallBelongsTemplate : IPatternTemplate
    {
        public string TemplateId => "forall-belongs";
        public int Order => 0;

        // ─── Data-ready variants (préparation YAML P9+) ───────────────

        private static readonly QuantifierVariant[] Variants = new[]
        {
            new QuantifierVariant("V", "\\forall", "forall", weight: 100),
            new QuantifierVariant("E", "\\exists", "exists", weight: 100),
            new QuantifierVariant("∀", "\\forall", "forall", weight: 100),
            new QuantifierVariant("∃", "\\exists", "exists", weight: 100),
        };

        private static readonly OpenerAlias[] Openers = new[]
        {
            new OpenerAlias("∈",          "in", weight: 100, requiresWordBoundary: false),
            new OpenerAlias("appartient", "in", weight: 90,  requiresWordBoundary: true),
            new OpenerAlias("dans",       "in", weight: 85,  requiresWordBoundary: true),
            new OpenerAlias("(-",         "in", weight: 80,  requiresWordBoundary: false),
            new OpenerAlias("app a",      "in", weight: 70,  requiresWordBoundary: true),
            new OpenerAlias("in",         "in", weight: 60,  requiresWordBoundary: true),
        };

        // ─── TryMatchHead ─────────────────────────────────────────────

        public PatternMatch? TryMatchHead(PatternScanContext ctx)
        {
            if (ctx == null) return null;
            var src = ctx.Source;
            if (string.IsNullOrEmpty(src)) return null;

            for (int i = ctx.StartPos; i < src.Length; i++)
            {
                foreach (var variant in Variants)
                {
                    if (!StartsWithAt(src, i, variant.Head)) continue;
                    int end = i + variant.Head.Length;

                    // Boundary gauche : pas une lettre/digit
                    if (i > 0 && char.IsLetterOrDigit(src[i - 1])) continue;
                    // Boundary droite : EOF ou non-lettre/non-digit (sinon
                    // "Vx" ou "Var" matcherait sur V seul)
                    if (end < src.Length && char.IsLetterOrDigit(src[end])) continue;

                    var slots = new Dictionary<string, SlotValue>(4)
                    {
                        ["polarity"] = new FilledSlotAtom(variant.Head, i, end),
                        ["var"] = EmptySlot.Instance,
                        ["opener"] = EmptySlot.Instance,
                        ["domain"] = EmptySlot.Instance,
                    };
                    return new PatternMatch(
                        templateId: TemplateId,
                        sourceStart: i,
                        sourceEnd: end,
                        slots: slots,
                        isComplete: false);
                }
            }
            return null;
        }

        // ─── Expand ────────────────────────────────────────────────────

        public IReadOnlyList<PatternCompletion> Expand(PatternMatch state, PatternScanContext ctx)
        {
            if (state == null || ctx == null) return System.Array.Empty<PatternCompletion>();
            var src = ctx.Source;

            // Récupère la variant utilisée (via le head dans le slot polarity)
            var variant = FindVariantForState(state);
            if (variant == null) return System.Array.Empty<PatternCompletion>();

            int pos = state.SourceEnd;

            // 1. Parse var (identifier list CSV)
            pos = SkipWhitespace(src, pos);
            var varAtom = ParseIdentifierList(src, pos);
            var stateWithVar = state;
            if (varAtom != null)
            {
                stateWithVar = state.WithSlot("var", varAtom).WithSourceEnd(varAtom.End);
                pos = varAtom.End;
            }

            // 2. Parse opener(s) — collecte tous les matchs pour multi-completion
            pos = SkipWhitespace(src, pos);
            var matchedOpeners = FindAllMatchingOpeners(src, pos);

            if (matchedOpeners.Count == 0)
            {
                // Pas d'opener → 1 completion sans domain (V x → ∀x)
                return new[] { BuildCompletion(stateWithVar, variant, openerInfo: null, domainSub: null, ctx) };
            }

            // 3. Pour chaque opener qui matche, essayer de parser le domain
            var completions = new List<PatternCompletion>(matchedOpeners.Count);
            foreach (var openerInfo in matchedOpeners)
            {
                var stateWithOpener = stateWithVar
                    .WithSlot("opener", new FilledSlotAtom(openerInfo.Alias.Token, openerInfo.Start, openerInfo.End))
                    .WithSourceEnd(openerInfo.End);

                // Try parse domain via Registry.Get("ensemble")
                PatternMatch? domainSub = null;
                int posAfterDomain = openerInfo.End;
                var ensembleTemplate = ctx.Registry?.Get("ensemble");
                if (ensembleTemplate != null)
                {
                    int posDomainStart = SkipWhitespace(src, openerInfo.End);
                    var subCtx = ctx.WithStartPos(posDomainStart);
                    domainSub = ensembleTemplate.TryMatchHead(subCtx);
                    if (domainSub != null)
                    {
                        posAfterDomain = domainSub.SourceEnd;
                    }
                }

                var stateWithDomain = domainSub != null
                    ? stateWithOpener
                        .WithSlot("domain", new FilledSlotSubPattern(domainSub))
                        .WithSourceEnd(posAfterDomain)
                        .WithComplete(domainSub.IsComplete)
                    : stateWithOpener;

                completions.Add(BuildCompletion(stateWithDomain, variant, openerInfo, domainSub, ctx));
            }
            return completions;
        }

        // ─── Helpers de parsing ───────────────────────────────────────

        private static int SkipWhitespace(string src, int pos)
        {
            while (pos < src.Length && char.IsWhiteSpace(src[pos])) pos++;
            return pos;
        }

        private static bool StartsWithAt(string src, int pos, string needle)
        {
            if (pos + needle.Length > src.Length) return false;
            for (int k = 0; k < needle.Length; k++)
                if (src[pos + k] != needle[k]) return false;
            return true;
        }

        /// <summary>
        /// Parse une liste d'identifiers séparés par virgules : <c>x</c>,
        /// <c>x,y</c>, <c>x,y,z</c>. Espaces autour des virgules tolérés.
        /// Retourne un <see cref="FilledSlotAtom"/> couvrant toute la liste,
        /// ou null si pas d'identifier au début.
        /// </summary>
        private static FilledSlotAtom? ParseIdentifierList(string src, int pos)
        {
            int start = pos;
            if (pos >= src.Length || !char.IsLetter(src[pos])) return null;
            while (pos < src.Length && char.IsLetter(src[pos])) pos++;
            int end = pos;
            // Optional : ", ident" répété
            while (true)
            {
                int posSkip = SkipWhitespace(src, pos);
                if (posSkip >= src.Length || src[posSkip] != ',') break;
                int posAfterComma = SkipWhitespace(src, posSkip + 1);
                if (posAfterComma >= src.Length || !char.IsLetter(src[posAfterComma])) break;
                // Consomme l'identifier suivant
                pos = posAfterComma;
                while (pos < src.Length && char.IsLetter(src[pos])) pos++;
                end = pos;
            }
            return new FilledSlotAtom(src.Substring(start, end - start), start, end);
        }

        private readonly struct MatchedOpener
        {
            public readonly OpenerAlias Alias;
            public readonly int Start;
            public readonly int End;
            public MatchedOpener(OpenerAlias alias, int start, int end)
            {
                Alias = alias;
                Start = start;
                End = end;
            }
        }

        /// <summary>
        /// Cherche tous les <see cref="OpenerAlias"/> qui matchent à
        /// <paramref name="pos"/> dans <paramref name="src"/>. Retourne la
        /// liste triée par <see cref="OpenerAlias.Weight"/> décroissant. En
        /// pratique 0 ou 1 match (les 6 aliases actuels commencent par des
        /// chars différents), mais le mécanisme supporte multi-match pour
        /// désambig future.
        /// </summary>
        private static List<MatchedOpener> FindAllMatchingOpeners(string src, int pos)
        {
            var matches = new List<MatchedOpener>();
            foreach (var alias in Openers)
            {
                if (!StartsWithAt(src, pos, alias.Token)) continue;
                int end = pos + alias.Token.Length;
                if (alias.RequiresWordBoundary
                    && end < src.Length
                    && char.IsLetter(src[end]))
                    continue;
                matches.Add(new MatchedOpener(alias, pos, end));
            }
            // Tri par poids desc (premier = meilleur candidat)
            matches.Sort((a, b) => b.Alias.Weight.CompareTo(a.Alias.Weight));
            return matches;
        }

        private static QuantifierVariant? FindVariantForState(PatternMatch state)
        {
            if (!state.Slots.TryGetValue("polarity", out var pol)
                || !(pol is FilledSlotAtom polAtom)) return null;
            foreach (var v in Variants)
                if (v.Head == polAtom.Text) return v;
            return null;
        }

        // ─── BuildCompletion (Latex + Mutation composite) ─────────────

        private static PatternCompletion BuildCompletion(
            PatternMatch state,
            QuantifierVariant variant,
            MatchedOpener? openerInfo,
            PatternMatch? domainSub,
            PatternScanContext ctx)
        {
            // Récupère la sub-completion du domain si présente (pour le rendu
            // LaTeX et la composition de mutation).
            PatternCompletion? domainCompletion = null;
            if (domainSub != null)
            {
                var ensembleTemplate = ctx.Registry?.Get("ensemble");
                var subCompletions = ensembleTemplate?.Expand(domainSub, ctx);
                if (subCompletions != null && subCompletions.Count > 0)
                    domainCompletion = subCompletions[0];
            }

            string preview = BuildLatex(state, variant, openerInfo, domainCompletion, hideEmpty: true);
            string hint = BuildLatex(state, variant, openerInfo, domainCompletion, hideEmpty: false);
            string description = BuildDescription(state, variant, openerInfo, domainCompletion);
            SourceMutation? mutation = BuildMutation(state, variant, openerInfo, domainCompletion, ctx);
            int score = ComputeScore(state, openerInfo, domainSub);

            return new PatternCompletion(
                description: description,
                previewLatex: preview,
                hintLatex: hint,
                mutation: mutation,
                completenessScore: score);
        }

        private static string BuildLatex(
            PatternMatch state, QuantifierVariant variant,
            MatchedOpener? openerInfo, PatternCompletion? domainCompletion, bool hideEmpty)
        {
            var sb = new StringBuilder();
            sb.Append(variant.LatexSymbol);

            string varText = SlotText(state, "var");
            if (!string.IsNullOrEmpty(varText))
                sb.Append(" ").Append(varText);
            else if (!hideEmpty)
                sb.Append(" \\square");

            if (openerInfo.HasValue)
            {
                sb.Append(" \\in ");
                if (domainCompletion != null)
                {
                    sb.Append(hideEmpty ? domainCompletion.PreviewLatex : domainCompletion.HintLatex);
                }
                else if (!hideEmpty)
                {
                    sb.Append("\\square");
                }
            }
            return sb.ToString();
        }

        private static string BuildDescription(
            PatternMatch state, QuantifierVariant variant,
            MatchedOpener? openerInfo, PatternCompletion? domainCompletion)
        {
            var sb = new StringBuilder();
            sb.Append(variant.Head == "V" || variant.Head == "∀" ? "∀" : "∃");

            string varText = SlotText(state, "var");
            if (!string.IsNullOrEmpty(varText)) sb.Append(varText);
            else sb.Append("▭");

            if (openerInfo.HasValue)
            {
                sb.Append("∈");
                sb.Append(domainCompletion?.Description ?? "▭");
            }
            return sb.ToString();
        }

        private static SourceMutation? BuildMutation(
            PatternMatch state, QuantifierVariant variant,
            MatchedOpener? openerInfo, PatternCompletion? domainCompletion,
            PatternScanContext ctx)
        {
            // Composite : on construit la string replacement qui remplace
            // toute la zone source du pattern par sa forme canonique
            // ("V x app a R*" → "forall x in bbR*"). Inclut les sub-mutations
            // si présentes (sinon utilise la source telle quelle pour le domain).
            var src = ctx.Source;
            int parentStart = state.SourceStart;
            int parentEnd = state.SourceEnd;
            if (parentStart < 0 || parentEnd > src.Length || parentEnd <= parentStart)
                return null;

            var sb = new StringBuilder();
            sb.Append(variant.MutationReplacement);

            // entre head et var (whitespace + var atom, conservé tel quel
            // depuis la source pour préserver la mise en forme user)
            string varText = SlotText(state, "var");
            if (!string.IsNullOrEmpty(varText))
                sb.Append(" ").Append(varText);

            if (openerInfo.HasValue)
            {
                sb.Append(" ").Append(openerInfo.Value.Alias.Canonical).Append(" ");
                // Domain : sub-mutation si dispo, sinon source brute
                if (domainCompletion?.Mutation != null)
                {
                    sb.Append(domainCompletion.Mutation.Replacement);
                }
                else if (state.Slots.TryGetValue("domain", out var dom)
                         && dom is FilledSlotSubPattern domSub)
                {
                    int subStart = domSub.Sub.SourceStart;
                    int subEnd = domSub.Sub.SourceEnd;
                    if (subStart >= 0 && subEnd <= src.Length && subEnd > subStart)
                        sb.Append(src.Substring(subStart, subEnd - subStart));
                }
            }

            return new SourceMutation(parentStart, parentEnd - parentStart, sb.ToString());
        }

        private static int ComputeScore(PatternMatch state, MatchedOpener? openerInfo, PatternMatch? domainSub)
        {
            int score = 25; // head matché
            if (!state.Slots["var"].IsEmpty) score += 25;
            if (openerInfo.HasValue) score += 25 * openerInfo.Value.Alias.Weight / 100;
            if (domainSub != null && domainSub.IsComplete) score += 25;
            return score;
        }

        private static string SlotText(PatternMatch state, string slotName)
        {
            if (!state.Slots.TryGetValue(slotName, out var v)) return string.Empty;
            return v is FilledSlotAtom atom ? atom.Text : string.Empty;
        }
    }
}
