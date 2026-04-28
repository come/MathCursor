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
    /// Une occurrence d'ambiguïté dans l'AST top-1 avec sa position dans le
    /// LaTeX rendu. Permet à la popup d'appliquer en cascade une préférence
    /// validée : « si l'utilisateur a choisi vec pour BC, on applique vec à
    /// tous les autres matches two-uppercase de la formule (AB, AC…) ».
    /// </summary>
    public sealed class AmbiguityMatch
    {
        public AmbiguitySpot Spot { get; }
        public int Start { get; }
        public int End { get; }
        public AmbiguityMatch(AmbiguitySpot spot, int start, int end)
        {
            Spot = spot;
            Start = start;
            End = end;
        }
    }

    /// <summary>
    /// Résultat de <see cref="AlternativeGenerator.FindRightmost"/> : le top-1
    /// LaTeX complet, le segment ambigu le plus à droite (pour la popup), et
    /// la liste de TOUS les matches dans la formule (pour la résolution en
    /// cascade des autres patterns du même RuleId).
    /// </summary>
    public sealed class AmbiguityResult
    {
        public string TopLatex { get; }
        public AmbiguitySpot? Spot { get; }
        public int? SpotStart { get; }
        public int? SpotEnd { get; }
        public IReadOnlyList<AmbiguityMatch> AllMatches { get; }

        public AmbiguityResult(string topLatex, AmbiguitySpot? spot, int? spotStart, int? spotEnd,
            IReadOnlyList<AmbiguityMatch>? allMatches = null)
        {
            TopLatex = topLatex;
            Spot = spot;
            SpotStart = spotStart;
            SpotEnd = spotEnd;
            AllMatches = allMatches ?? System.Array.Empty<AmbiguityMatch>();
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
            // Collecte aussi TOUS les matches pour permettre la résolution en
            // cascade côté popup (cf. AmbiguityMatch).
            var allMatches = CollectAllMatches(topAst, topLatex);

            if (spot == null) return new AmbiguityResult(topLatex, null, null, null, allMatches);

            int idx = topLatex.LastIndexOf(spot.DefaultLatex, System.StringComparison.Ordinal);
            if (idx < 0)
                return new AmbiguityResult(topLatex, spot, null, null, allMatches);
            return new AmbiguityResult(topLatex, spot, idx, idx + spot.DefaultLatex.Length, allMatches);
        }

        /// <summary>
        /// Parcourt l'AST top-1 et retourne TOUS les sous-AST qui matchent une
        /// règle d'ambiguïté, avec leur position dans <paramref name="topLatex"/>.
        /// Stoppe la descente dès qu'un node match (= cohérent avec
        /// TraverseRightmost qui favorise le pattern le plus large).
        /// </summary>
        private static IReadOnlyList<AmbiguityMatch> CollectAllMatches(AstNode topAst, string topLatex)
        {
            var matches = new List<AmbiguityMatch>();
            // On parcourt en s'assurant de ne pas substituer 2x le même range :
            // chaque match consomme sa portion via TrackingFromRight.
            var consumed = new bool[topLatex.Length];
            CollectAllMatchesRec(topAst, topLatex, matches, consumed);
            return matches;
        }

        private static void CollectAllMatchesRec(AstNode node, string topLatex,
            List<AmbiguityMatch> output, bool[] consumed)
        {
            var spot = MatchAmbiguity(node);
            if (spot != null)
            {
                // Cherche la POSITION DROITE non-consommée pour éviter qu'un
                // match avale la position d'un autre. Ex: pour AB+BC on veut
                // BC trouvé à pos[3..5], pas à pos[0..2] = AB.
                int idx = LastIndexOfFree(topLatex, spot.DefaultLatex, consumed);
                if (idx >= 0)
                {
                    int end = idx + spot.DefaultLatex.Length;
                    for (int i = idx; i < end; i++) consumed[i] = true;
                    output.Add(new AmbiguityMatch(spot, idx, end));
                }
                return; // pattern parent prioritaire, ne descend pas dans enfants
            }
            foreach (var child in GetChildrenRightFirst(node))
                CollectAllMatchesRec(child, topLatex, output, consumed);
        }

        private static int LastIndexOfFree(string text, string needle, bool[] consumed)
        {
            int idx = text.LastIndexOf(needle, System.StringComparison.Ordinal);
            while (idx >= 0)
            {
                bool free = true;
                for (int i = idx; i < idx + needle.Length; i++)
                    if (consumed[i]) { free = false; break; }
                if (free) return idx;
                if (idx == 0) return -1;
                idx = text.LastIndexOf(needle, idx - 1, System.StringComparison.Ordinal);
            }
            return -1;
        }

        private static (AstNode? sub, AmbiguitySpot? spot) TraverseRightmost(AstNode node)
        {
            // PRÉ-ORDRE right-first : on teste le node COURANT avant de descendre
            // dans les enfants. Ça favorise les patterns les plus LARGES (= les
            // plus contextuels). Ex: pour ABC (Bin(*, Bin(*, A, B), C)) on veut
            // exposer l'ambig sur ABC entier (angle/triangle), pas l'ambig
            // partielle sur AB qui n'a aucun sens dans ce contexte.
            var spot = MatchAmbiguity(node);
            if (spot != null) return (node, spot);

            foreach (var child in GetChildrenRightFirst(node))
            {
                var result = TraverseRightmost(child);
                if (result.spot != null) return result;
            }
            return (null, null);
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
            // Règle 1a : trois majuscules 1-char en mult implicite tight (ABC)
            // → objet géométrique à 3 sommets. AST = Bin(*, Bin(*, A, B), C)
            // (gauche-associatif). Testée AVANT la règle 2-uppercase pour
            // éviter qu'on propose l'ambig partielle sur AB seul.
            if (node is Bin outer && outer.Op == "*" && outer.Implicit && outer.Tight
                && outer.Lhs is Bin inner && inner.Op == "*" && inner.Implicit && inner.Tight
                && inner.Lhs is Atom a1 && a1.Kind == "ident" && a1.Value.Length == 1 && char.IsUpper(a1.Value[0])
                && inner.Rhs is Atom a2 && a2.Kind == "ident" && a2.Value.Length == 1 && char.IsUpper(a2.Value[0])
                && outer.Rhs is Atom a3 && a3.Kind == "ident" && a3.Value.Length == 1 && char.IsUpper(a3.Value[0]))
            {
                var triplet = a1.Value + a2.Value + a3.Value;
                return new AmbiguitySpot(
                    ruleId: RuleThreeUppercase,
                    defaultLatex: triplet,
                    alternatives: new[]
                    {
                        $"\\widehat{{{triplet}}}",   // angle ABC (sommet B)
                        $"\\triangle {triplet}",     // triangle ABC
                    });
            }

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
        public const string RuleThreeUppercase = "three-uppercase";
        public const string RuleLetterSupNumber = "letter-sup-number";
    }
}
