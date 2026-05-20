using System.Collections.Generic;
using System.Linq;
using MathCursor.Core.Lattice.Ast;

namespace MathCursor.Core.Lattice.Ambiguity
{
    /// <summary>
    /// Helpers statiques partagés par les scanners d'ambiguïté. Frontière
    /// utility de l'archi Strategy : les scanners y réfèrent sans en
    /// hériter — contrats individuels, mais DRY respecté sur les utilitaires
    /// communs.
    ///
    /// <para>Cf. ADR <c>2026-05-13-Refactor-ambiguity-scanners-strategy</c>.</para>
    /// </summary>
    public static class AmbiguityScannerHelpers
    {
        /// <summary>
        /// Cherche la dernière occurrence de <paramref name="needle"/> dans
        /// <paramref name="text"/> qui :
        /// <list type="number">
        /// <item>n'a aucune position dans <paramref name="consumed"/> à <c>true</c>,</item>
        /// <item>respecte le word-boundary (caractère avant ET après ne sont pas des lettres).</item>
        /// </list>
        /// Retourne <c>-1</c> si aucune occurrence valide.
        /// </summary>
        public static int LastIndexOfWordBoundary(string text, string needle, bool[] consumed)
        {
            int idx = text.LastIndexOf(needle, System.StringComparison.Ordinal);
            while (idx >= 0)
            {
                bool free = true;
                for (int i = idx; i < idx + needle.Length; i++)
                    if (consumed[i]) { free = false; break; }
                if (free)
                {
                    bool boundLeft = idx == 0 || !char.IsLetter(text[idx - 1]);
                    bool boundRight = idx + needle.Length == text.Length
                                   || !char.IsLetter(text[idx + needle.Length]);
                    if (boundLeft && boundRight) return idx;
                }
                if (idx == 0) return -1;
                idx = text.LastIndexOf(needle, idx - 1, System.StringComparison.Ordinal);
            }
            return -1;
        }

        /// <summary>
        /// Vrai si <paramref name="s"/> est une chaîne de 2 ou 3 majuscules.
        /// </summary>
        public static bool IsAllUpperPair(string? s)
        {
            if (s == null) return false;
            if (s.Length != 2 && s.Length != 3) return false;
            foreach (var c in s) if (!char.IsUpper(c)) return false;
            return true;
        }

        /// <summary>
        /// Fabrique un <see cref="AmbiguitySpot"/> pour une pair de 2/3
        /// majuscules SANS <c>SourceMutation</c> sur les alternatives.
        /// Utilisé pour les patterns détectés via AST ou via splice latex
        /// pur. Pour les patterns détectés via scan source (où l'on dispose
        /// de l'offset source), préférer une fabrique <c>WithMutations</c>
        /// (à venir en S1 du refacto).
        /// </summary>
        public static AmbiguitySpot MakeUpperSpotLatexOnly(string pair)
        {
            if (pair.Length == 2)
            {
                return new AmbiguitySpot(
                    ruleId: AlternativeGenerator.RuleTwoUppercase,
                    defaultLatex: pair,
                    alternatives: new[]
                    {
                        new AmbiguityAlternative($"\\vec{{{pair}}}"),
                        new AmbiguityAlternative($"\\left({pair}\\right)"),
                        new AmbiguityAlternative($"\\left[{pair}\\right]"),
                    });
            }
            // length == 3
            return new AmbiguitySpot(
                ruleId: AlternativeGenerator.RuleThreeUppercase,
                defaultLatex: pair,
                alternatives: new[]
                {
                    new AmbiguityAlternative($"\\widehat{{{pair}}}"),
                    new AmbiguityAlternative($"\\triangle {pair}"),
                });
        }

        /// <summary>
        /// Itère les enfants directs d'un nœud AST <b>right-first</b>
        /// (favorise le pattern le plus à droite = le plus proche du
        /// caret en saisie).
        /// </summary>
        public static IEnumerable<AstNode> GetChildrenRightFirst(AstNode node) => node switch
        {
            Bin b => new[] { b.Rhs, b.Lhs },
            Sup s => new[] { s.Exp, s.Base },
            Sub s => new[] { s.Idx, s.Base },
            Group g => new[] { g.Expr },
            Frac f => new[] { f.Den, f.Num },
            Sqrt sq => new[] { sq.Arg },
            Func fn => new[] { fn.Arg },
            Sum sum => new[] { sum.Body, sum.End, sum.Start, sum.Var },
            Lim l => new[] { l.Body, l.Target, l.Var },
            Int it => new[] { it.Body, it.High, it.Low },
            Unary u => new[] { u.Arg },
            _ => Enumerable.Empty<AstNode>(),
        };
    }
}
