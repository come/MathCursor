using System.Collections.Generic;
using System.Linq;
using MathCursor.Core.Lattice.Ast;

namespace MathCursor.Core.Lattice
{
    /// <summary>
    /// Une ambiguïté détectée localement dans l'AST top-1 : un sous-AST qui a
    /// une lecture par défaut (le LaTeX intégré dans top-1) et N alternatives.
    /// <see cref="RuleId"/> identifie la RÈGLE qui a généré cette ambiguïté
    /// (ex: "two-uppercase") — utilisé par la popup pour appliquer une
    /// préférence apprise (l'utilisateur a déjà choisi vec pour AB → on
    /// applique vec automatiquement aux CD, EF, … de la session).
    /// </summary>
    public sealed class AmbiguitySpot
    {
        public string RuleId { get; }
        public string DefaultLatex { get; }
        public IReadOnlyList<string> Alternatives { get; }
        public AmbiguitySpot(string ruleId, string defaultLatex, IReadOnlyList<string> alternatives)
        {
            RuleId = ruleId;
            DefaultLatex = defaultLatex;
            Alternatives = alternatives;
        }
    }

    /// <summary>
    /// Résultat de <see cref="AlternativeGenerator.FindRightmost"/> : le top-1
    /// LaTeX complet, et éventuellement le segment ambigu le plus à droite avec
    /// sa position dans le top-1 (pour permettre la recompose dans la popup).
    /// Position null si aucune ambiguïté détectée.
    /// </summary>
    public sealed class AmbiguityResult
    {
        public string TopLatex { get; }
        public AmbiguitySpot? Spot { get; }
        public int? SpotStart { get; }
        public int? SpotEnd { get; }
        public AmbiguityResult(string topLatex, AmbiguitySpot? spot, int? spotStart, int? spotEnd)
        {
            TopLatex = topLatex;
            Spot = spot;
            SpotStart = spotStart;
            SpotEnd = spotEnd;
        }
    }

    /// <summary>
    /// Génère des alternatives sémantiques sur l'AST top-1 (LaTeX top-1 du
    /// pipeline lattice). Ces alternatives ne viennent PAS du lattice naturel
    /// (qui ne génère qu'une lecture par chaîne d'opérateurs) — elles encodent
    /// des conventions typographiques où une même saisie peut vouloir dire
    /// plusieurs choses dans les maths du lycée français.
    ///
    /// Exemples :
    /// <list type="bullet">
    /// <item><c>AB</c> (deux majuscules adjacentes) → vecteur, droite, segment</item>
    /// <item><c>x2</c> (lettre + chiffre) → exposant (top-1) ou indice (alt)</item>
    /// </list>
    ///
    /// Cf. <see cref="FindRightmost"/> pour la stratégie « ambiguïté la plus à
    /// droite = la plus proche du caret en cours de saisie » : tout ce qui est
    /// avant a été validé mou par le fait que l'élève continue à taper.
    /// </summary>
    public static class AlternativeGenerator
    {
        /// <summary>
        /// Liste plate des alternatives à TOUS les niveaux (legacy, utilisé par
        /// <see cref="MathCursor.Core.LatticeEngine.Convert"/> pour exposer
        /// chaque variante comme une suggestion complète). Usage UI 5b2 :
        /// préférer <see cref="FindRightmost"/> qui rend une seule ambiguïté.
        /// </summary>
        public static IReadOnlyList<string> Generate(AstNode? topAst)
        {
            if (topAst == null) return System.Array.Empty<string>();
            var spot = MatchAmbiguity(topAst);
            return spot?.Alternatives ?? System.Array.Empty<string>();
        }

        /// <summary>
        /// Trouve l'ambiguïté la plus à droite dans l'AST (post-ordre right-first).
        /// Renvoie le LaTeX du segment ambigu (default + alternatives) et sa
        /// position dans <paramref name="topLatex"/> via LastIndexOf.
        ///
        /// Si plusieurs ambiguïtés existent dans la formule, seule la plus à
        /// droite est exposée — convention « validation molle » : l'élève a
        /// implicitement validé les ambiguïtés gauches en tapant le reste.
        /// </summary>
        public static AmbiguityResult FindRightmost(AstNode? topAst, string topLatex)
        {
            if (topAst == null) return new AmbiguityResult(topLatex, null, null, null);

            var (subAst, spot) = TraverseRightmost(topAst);
            if (spot == null) return new AmbiguityResult(topLatex, null, null, null);

            // Position dans topLatex : LastIndexOf du LaTeX par défaut. Si
            // plusieurs occurrences existent, on prend la dernière (cohérent
            // avec « le plus à droite »). Si introuvable (le rendu intégré
            // diverge du rendu isolé), on n'expose pas la position — la popup
            // affichera quand même les alternatives en mode legacy.
            int idx = topLatex.LastIndexOf(spot.DefaultLatex, System.StringComparison.Ordinal);
            if (idx < 0)
                return new AmbiguityResult(topLatex, spot, null, null);
            return new AmbiguityResult(topLatex, spot, idx, idx + spot.DefaultLatex.Length);
        }

        private static (AstNode? sub, AmbiguitySpot? spot) TraverseRightmost(AstNode node)
        {
            // Récursion en post-ordre right-first : chaque enfant est inspecté
            // de droite à gauche, et on retourne dès qu'une ambiguïté est trouvée.
            foreach (var child in GetChildrenRightFirst(node))
            {
                var result = TraverseRightmost(child);
                if (result.spot != null) return result;
            }
            // Aucun enfant ambigu : on teste ce nœud lui-même.
            var spot = MatchAmbiguity(node);
            return spot != null ? (node, spot) : (null, null);
        }

        private static IEnumerable<AstNode> GetChildrenRightFirst(AstNode node) => node switch
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

        // ----------------- Règles de matching -----------------

        /// <summary>
        /// Teste si un nœud est ambigu et renvoie ses alternatives + le LaTeX
        /// par défaut tel qu'il apparaîtra dans le top-1 (= le rendu standard).
        /// </summary>
        private static AmbiguitySpot? MatchAmbiguity(AstNode node)
        {
            // Règle 1 : deux majuscules 1-char en mult implicite tight → objet
            // géométrique nommé.
            if (node is Bin b && b.Op == "*" && b.Implicit && b.Tight
                && b.Lhs is Atom lhs && lhs.Kind == "ident" && lhs.Value.Length == 1
                && b.Rhs is Atom rhs && rhs.Kind == "ident" && rhs.Value.Length == 1
                && char.IsUpper(lhs.Value[0]) && char.IsUpper(rhs.Value[0]))
            {
                var pair = lhs.Value + rhs.Value;
                return new AmbiguitySpot(
                    ruleId: RuleTwoUppercase,
                    defaultLatex: pair,
                    alternatives: new[]
                    {
                        $"\\vec{{{pair}}}",
                        $"\\left({pair}\\right)",
                        $"\\left[{pair}\\right]",
                    });
            }

            // Règle 2 : Sup d'une lettre 1-char par un Number → alternative subscript.
            if (node is Sup s
                && s.Base is Atom sb && sb.Kind == "ident" && sb.Value.Length == 1
                && s.Exp is Atom se && se.Kind == "number")
            {
                return new AmbiguitySpot(
                    ruleId: RuleLetterSupNumber,
                    defaultLatex: $"{sb.Value}^{{{se.Value}}}",
                    alternatives: new[] { $"{sb.Value}_{{{se.Value}}}" });
            }

            return null;
        }

        // Identifiants des règles d'ambiguïté — utilisés par la popup pour
        // mémoriser les préférences utilisateur par TYPE de pattern (et non
        // par instance string).
        public const string RuleTwoUppercase = "two-uppercase";
        public const string RuleLetterSupNumber = "letter-sup-number";
    }
}
