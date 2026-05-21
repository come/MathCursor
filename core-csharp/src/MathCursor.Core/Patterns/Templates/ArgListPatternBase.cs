using System.Collections.Generic;

namespace MathCursor.Core.Patterns.Templates
{
    /// <summary>
    /// Span source d'un argument parsé par <see cref="ArgListPatternBase"/>.
    /// <see cref="Text"/> est la sous-chaîne source brute pour cet arg.
    /// </summary>
    public sealed class ArgSpan
    {
        public string Text { get; }
        public int Start { get; }
        public int End { get; }

        public ArgSpan(string text, int start, int end)
        {
            Text = text ?? throw new System.ArgumentNullException(nameof(text));
            Start = start;
            End = end;
        }
    }

    /// <summary>
    /// Résultat de la classification des args par
    /// <see cref="ArgListPatternBase.ClassifyArgs"/> : les premiers args
    /// sont les vars (= liste de variables), le dernier est éventuellement
    /// le domain (= ensemble identifié).
    /// </summary>
    public sealed class ArgClassification
    {
        public IReadOnlyList<ArgSpan> VarArgs { get; }
        public ArgSpan? DomainArg { get; }
        /// <summary>Sub-pattern résolu pour <see cref="DomainArg"/> via
        /// <c>Registry.Get("ensemble").TryMatchHead</c>. Null si pas de domain
        /// ou Registry absent.</summary>
        public PatternMatch? DomainSubMatch { get; }

        public ArgClassification(
            IReadOnlyList<ArgSpan> varArgs,
            ArgSpan? domainArg,
            PatternMatch? domainSubMatch)
        {
            VarArgs = varArgs ?? System.Array.Empty<ArgSpan>();
            DomainArg = domainArg;
            DomainSubMatch = domainSubMatch;
        }
    }

    /// <summary>
    /// Base abstraite pour les templates qui suivent la convention
    /// <b>"head + args séparés par espaces"</b> : <c>V x R</c>, <c>Lim x 0 f(x)</c>,
    /// <c>sum k 0 n k²</c>, <c>int 0 1 f(x)</c>, etc.
    ///
    /// <para>Cohérent avec la doctrine "rapidité de saisie" — pas d'openers
    /// textuels intermédiaires (= <c>app a</c>, <c>in</c>, etc. retirés en
    /// P5-refactor 2026-05-21). Convention de discrimination : si le
    /// <b>dernier</b> arg matche le pattern <c>ensemble</c> (R/N/Z/Q/C avec
    /// ou sans modifier, ou intervalle <c>[...]</c>), c'est le domain ;
    /// sinon tous les args = vars.</para>
    ///
    /// <para>Sous-classes : <c>ForallBelongsTemplate</c> (V/E/∀/∃),
    /// futurs <c>LimTemplate</c>, <c>SumTemplate</c>, <c>IntegralTemplate</c>
    /// (en P9+).</para>
    ///
    /// <para>Cf. ADR <c>2026-05-21-Refactor-forall-belongs-arglist-convention</c>.</para>
    /// </summary>
    public abstract class ArgListPatternBase : IPatternTemplate
    {
        public abstract string TemplateId { get; }
        public virtual int Order => 0;

        /// <summary>Variantes du head (raccourcis ASCII + unicode direct).
        /// Ex. pour forall-belongs : V, E, ∀, ∃.</summary>
        protected abstract IReadOnlyList<QuantifierVariant> Heads { get; }

        public PatternMatch? TryMatchHead(PatternScanContext ctx)
        {
            if (ctx == null) return null;
            var src = ctx.Source;
            if (string.IsNullOrEmpty(src)) return null;

            for (int i = ctx.StartPos; i < src.Length; i++)
            {
                foreach (var variant in Heads)
                {
                    if (!StartsWithAt(src, i, variant.Head)) continue;
                    int end = i + variant.Head.Length;

                    // Boundary gauche : pas une lettre/digit
                    if (i > 0 && char.IsLetterOrDigit(src[i - 1])) continue;
                    // Boundary droite : EOF ou non-lettre/non-digit (sinon
                    // "Vx" ou "Var" matcherait sur V seul)
                    if (end < src.Length && char.IsLetterOrDigit(src[end])) continue;

                    var slots = new Dictionary<string, SlotValue>(1)
                    {
                        ["polarity"] = new FilledSlotAtom(variant.Head, i, end),
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

        public abstract IReadOnlyList<PatternCompletion> Expand(
            PatternMatch state, PatternScanContext ctx);

        // ─── Helpers communs aux sous-classes ──────────────────────────

        /// <summary>
        /// Parse les args bruts à partir de <paramref name="pos"/> dans
        /// <paramref name="src"/>. Chaque arg est une séquence non-whitespace
        /// (= identifier, nombre, ou un block délimité <c>[...]</c>/<c>(...)</c>).
        ///
        /// <para>Les whitespaces séparent les args. Un block crocheté/parenthésé
        /// est considéré comme UN arg (= permet <c>V x [0,1]</c> = 2 args).</para>
        /// </summary>
        protected static IReadOnlyList<ArgSpan> ParseArgs(string src, int pos)
        {
            var args = new List<ArgSpan>();
            while (pos < src.Length)
            {
                pos = SkipWhitespace(src, pos);
                if (pos >= src.Length) break;

                int start = pos;
                char c = src[pos];

                // Block crocheté/parenthésé = 1 arg atomique (pour intervals)
                if (c == '[' || c == '(')
                {
                    char closeChar = c == '[' ? ']' : ')';
                    pos++;
                    int depth = 1;
                    while (pos < src.Length && depth > 0)
                    {
                        char ch = src[pos];
                        if (ch == c) depth++;
                        else if (ch == closeChar) depth--;
                        pos++;
                    }
                    // Extension : chaîne union/inter post-bracket (= [0,1]U[3,4])
                    // ne casse pas par le space car U est tight. Mais une espace
                    // après bracket termine quand même.
                    pos = ConsumeUnionExtension(src, pos);
                }
                else
                {
                    // Token : séquence non-whitespace, terminée à whitespace ou EOF
                    while (pos < src.Length && !char.IsWhiteSpace(src[pos])) pos++;
                }

                if (pos > start)
                    args.Add(new ArgSpan(src.Substring(start, pos - start), start, pos));
            }
            return args;
        }

        /// <summary>
        /// Si après une fermeture de bracket on a un opérateur d'union
        /// (<c>U</c>, <c>∪</c>, <c>union</c>, <c>inter</c>, <c>∩</c>) puis un
        /// autre bracket, on étend l'arg pour englober la chaîne complète.
        /// Permet <c>V x [0,1]U[3,4]</c> = 2 args (var + interval-union chaîne).
        /// </summary>
        private static int ConsumeUnionExtension(string src, int pos)
        {
            while (pos < src.Length)
            {
                int afterOp = TryConsumeUnionOperator(src, pos);
                if (afterOp == pos) break;
                if (afterOp >= src.Length) return pos; // op pas suivi de bracket → stop avant op
                char next = src[afterOp];
                if (next != '[' && next != '(') return pos; // op pas suivi de bracket
                // Consomme l'op + le sous-bracket
                pos = afterOp;
                char closeChar = src[pos] == '[' ? ']' : ')';
                int depth = 1;
                pos++;
                while (pos < src.Length && depth > 0)
                {
                    char ch = src[pos];
                    if (ch == src[afterOp]) depth++;
                    else if (ch == closeChar) depth--;
                    pos++;
                }
            }
            return pos;
        }

        private static int TryConsumeUnionOperator(string src, int pos)
        {
            if (pos >= src.Length) return pos;
            char c = src[pos];
            if (c == '∪' || c == '∩' || c == 'U') return pos + 1;
            if (StartsWithAt(src, pos, "union")) return pos + 5;
            if (StartsWithAt(src, pos, "inter")) return pos + 5;
            return pos;
        }

        /// <summary>
        /// Classifie les <paramref name="args"/> en (vars + domain optionnel)
        /// selon la convention : si le dernier arg matche un pattern
        /// <c>ensemble</c> (= R/N/Z/Q/C avec/sans modifier, ou intervalle),
        /// c'est le domain. Sinon tous = vars.
        ///
        /// <para>La détection d'ensemble passe par
        /// <c>ctx.Registry.Get("ensemble").TryMatchHead</c> sur un sub-ctx
        /// positionné au dernier arg. Si Registry absent, pas de domain
        /// classifié (tous = vars).</para>
        /// </summary>
        protected static ArgClassification ClassifyArgs(
            IReadOnlyList<ArgSpan> args, PatternScanContext ctx)
        {
            if (args == null || args.Count == 0)
                return new ArgClassification(
                    System.Array.Empty<ArgSpan>(), null, null);

            var registry = ctx.Registry;
            if (registry == null || args.Count < 2)
            {
                // Sans Registry, ou un seul arg : aucune classification possible,
                // tout = vars. (Un seul arg ne peut être domain — par convention,
                // au minimum 1 var + 1 domain pour qu'il y ait classification.)
                return new ArgClassification(args, null, null);
            }

            var ensembleTemplate = registry.Get("ensemble");
            if (ensembleTemplate == null)
                return new ArgClassification(args, null, null);

            // Tester le dernier arg : matche-t-il un pattern ensemble dont
            // le span couvre EXACTEMENT le span de l'arg ?
            var lastArg = args[args.Count - 1];
            var subCtx = ctx.WithStartPos(lastArg.Start);
            var subMatch = ensembleTemplate.TryMatchHead(subCtx);
            if (subMatch == null
                || subMatch.SourceStart != lastArg.Start
                || subMatch.SourceEnd != lastArg.End)
            {
                // Pas un ensemble identifié (= pas de match, ou match partiel)
                return new ArgClassification(args, null, null);
            }

            // Dernier arg = domain identifié
            var varArgs = new List<ArgSpan>(args.Count - 1);
            for (int i = 0; i < args.Count - 1; i++) varArgs.Add(args[i]);
            return new ArgClassification(varArgs, lastArg, subMatch);
        }

        protected static QuantifierVariant? FindVariantForState(
            PatternMatch state, IReadOnlyList<QuantifierVariant> variants)
        {
            if (!state.Slots.TryGetValue("polarity", out var pol)
                || !(pol is FilledSlotAtom polAtom)) return null;
            foreach (var v in variants)
                if (v.Head == polAtom.Text) return v;
            return null;
        }

        protected static int SkipWhitespace(string src, int pos)
        {
            while (pos < src.Length && char.IsWhiteSpace(src[pos])) pos++;
            return pos;
        }

        protected static bool StartsWithAt(string src, int pos, string needle)
        {
            if (pos + needle.Length > src.Length) return false;
            for (int k = 0; k < needle.Length; k++)
                if (src[pos + k] != needle[k]) return false;
            return true;
        }

        // ─── Helpers partagés Lim/Sum/Int/Dérivée (P9 series) ──────────

        /// <summary>
        /// Concatène les args à partir de <paramref name="startIdx"/> en
        /// préservant les whitespaces originaux entre eux (= reproduit la
        /// sous-chaîne source brute du premier au dernier arg).
        /// Utilisé par les templates dont le dernier slot accepte une
        /// expression multi-tokens (ex. <c>Lim x 0 f x</c> où expression = "f x").
        /// </summary>
        protected static string ConcatArgsFrom(IReadOnlyList<ArgSpan> args, int startIdx, string source)
        {
            if (startIdx < 0 || startIdx >= args.Count) return string.Empty;
            int from = args[startIdx].Start;
            int to = args[args.Count - 1].End;
            return source.Substring(from, to - from);
        }

        /// <summary>
        /// Convertit les tokens raccourcis pour l'infini en LaTeX :
        /// <c>+oo</c> → <c>+\infty</c>, <c>-oo</c> → <c>-\infty</c>, etc.
        /// Retourne null si <paramref name="text"/> est null ; retourne le
        /// texte tel quel si pas de match (= permet d'avoir d'autres bornes
        /// littérales comme `a`, `n+1`, etc.).
        /// </summary>
        protected static string? ConvertInfinityToken(string? text)
        {
            if (text == null) return null;
            return text switch
            {
                "oo" => "\\infty",
                "+oo" => "+\\infty",
                "-oo" => "-\\infty",
                "infini" => "\\infty",
                "+infini" => "+\\infty",
                "-infini" => "-\\infty",
                "∞" => "\\infty",
                "+∞" => "+\\infty",
                "-∞" => "-\\infty",
                _ => text,
            };
        }

        /// <summary>
        /// Variante Unicode-friendly de <see cref="ConvertInfinityToken"/>,
        /// pour les descriptions popup (ex. <c>+oo</c> → <c>+∞</c>).
        /// </summary>
        protected static string? ConvertInfinityToUnicode(string? text)
        {
            if (text == null) return null;
            return text switch
            {
                "oo" => "∞",
                "+oo" => "+∞",
                "-oo" => "-∞",
                "infini" => "∞",
                "+infini" => "+∞",
                "-infini" => "-∞",
                _ => text,
            };
        }
    }
}
