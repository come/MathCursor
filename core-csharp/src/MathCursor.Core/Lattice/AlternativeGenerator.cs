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

            // Walk pré-ordre right-first qui collecte TOUS les matches valides
            // (= entourés de word boundaries dans topLatex). Le rightmost est
            // le PREMIER ajouté (parcours right-first), retourné comme Spot
            // principal. Les autres servent à la cascade côté popup.
            var allMatches = CollectAllMatches(topAst, topLatex);
            if (allMatches.Count == 0)
                return new AmbiguityResult(topLatex, null, null, null, allMatches);
            var rightmost = allMatches[0];
            return new AmbiguityResult(topLatex, rightmost.Spot, rightmost.Start, rightmost.End, allMatches);
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
            var consumed = new bool[topLatex.Length];
            // 1) Patterns AST-based (Sup d'une lettre par un nombre, etc.)
            CollectAllMatchesRec(topAst, topLatex, matches, consumed);
            // 2) Patterns STRING-based : séquences de majuscules adjacentes dans
            //    topLatex. On scanne en passant par-dessus l'AST parce que
            //    l'arbre gauche-associatif ne regroupe pas toujours C et D
            //    ensemble (ex: AB*CD donne ((A*B)*C)*D, donc CD n'est jamais
            //    un sous-Bin direct). Le scan string capture toutes les
            //    séquences majuscules quelle que soit leur structure AST.
            ScanUppercaseSequences(topLatex, matches, consumed);
            // Tri par position décroissante : le rightmost reste matches[0].
            matches.Sort((a, b) => b.Start.CompareTo(a.Start));
            return matches;
        }

        /// <summary>
        /// Parcourt <paramref name="topLatex"/> et émet un match pour chaque
        /// séquence de 2 ou 3 majuscules consécutives entourées de non-lettres
        /// (word boundary). Évite les positions déjà consommées par d'autres
        /// matches AST-based.
        /// </summary>
        private static void ScanUppercaseSequences(string topLatex,
            List<AmbiguityMatch> output, bool[] consumed)
        {
            int i = 0;
            while (i < topLatex.Length)
            {
                if (!char.IsUpper(topLatex[i]) || consumed[i]) { i++; continue; }
                // Word boundary left
                if (i > 0 && char.IsLetter(topLatex[i - 1])) { i++; continue; }
                int j = i;
                while (j < topLatex.Length && char.IsUpper(topLatex[j]) && !consumed[j]) j++;
                int len = j - i;
                // Word boundary right (if more letters follow, c'est un mot plus
                // long, on n'émet aucun match — ex: ABCD ne propose ni AB ni ABC)
                if (j < topLatex.Length && char.IsLetter(topLatex[j])) { i = j; continue; }

                AmbiguitySpot? spot = null;
                if (len == 2)
                {
                    var pair = topLatex.Substring(i, 2);
                    spot = new AmbiguitySpot(
                        ruleId: RuleTwoUppercase,
                        defaultLatex: pair,
                        alternatives: new[]
                        {
                            $"\\vec{{{pair}}}",
                            $"\\left({pair}\\right)",
                            $"\\left[{pair}\\right]",
                        });
                }
                else if (len == 3)
                {
                    var triplet = topLatex.Substring(i, 3);
                    spot = new AmbiguitySpot(
                        ruleId: RuleThreeUppercase,
                        defaultLatex: triplet,
                        alternatives: new[]
                        {
                            $"\\widehat{{{triplet}}}",
                            $"\\triangle {triplet}",
                        });
                }
                // 4+ majuscules : on n'émet rien (pas de pattern géométrique
                // standard pour 4 lettres, et l'utilisateur n'a probablement
                // pas tapé ça sciemment).
                if (spot != null)
                {
                    for (int k = i; k < j; k++) consumed[k] = true;
                    output.Add(new AmbiguityMatch(spot, i, j));
                }
                i = j;
            }
        }

        private static void CollectAllMatchesRec(AstNode node, string topLatex,
            List<AmbiguityMatch> output, bool[] consumed)
        {
            var spot = MatchAmbiguity(node);
            if (spot != null)
            {
                // Cherche la position du defaultLatex dans topLatex avec :
                //  - Position non-consommée par un match précédent (sinon AB
                //    et BC dans "AB+BC" pointeraient au même endroit).
                //  - Word boundary : entouré de non-lettres (ou bord de
                //    string). Sans ça, "ABC" matcherait dans "ABCD" et
                //    "AB" matcherait dans "ABC" — le pattern serait juste
                //    un sous-string d'un mot plus long, pas un objet
                //    géométrique distinct.
                int idx = LastIndexOfWordBoundary(topLatex, spot.DefaultLatex, consumed);
                if (idx >= 0)
                {
                    int end = idx + spot.DefaultLatex.Length;
                    for (int i = idx; i < end; i++) consumed[i] = true;
                    output.Add(new AmbiguityMatch(spot, idx, end));
                    return; // match validé, on ne descend pas plus profond
                }
                // Pas de boundary trouvée : ce match parent est rejeté
                // (probablement sub-string d'un mot plus long). On descend
                // QUAND MÊME pour voir si un sous-AST plus petit matche
                // ailleurs dans la formule.
            }
            foreach (var child in GetChildrenRightFirst(node))
                CollectAllMatchesRec(child, topLatex, output, consumed);
        }

        /// <summary>
        /// Cherche la dernière occurrence de <paramref name="needle"/> dans
        /// <paramref name="text"/> qui satisfait :
        ///  1) toutes ses positions [idx..idx+len) sont libres dans consumed
        ///  2) word boundary : le caractère AVANT et APRÈS n'est pas une lettre
        ///     (ou bord de chaîne). Empêche le match d'un sub-string de mot
        ///     plus long (ex: AB dans ABCD).
        /// Retourne -1 si aucune occurrence valide.
        /// </summary>
        private static int LastIndexOfWordBoundary(string text, string needle, bool[] consumed)
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
        /// Teste si un nœud AST est ambigu (patterns structurels uniquement).
        /// Les patterns string-based (séquences de majuscules AB/ABC pour les
        /// objets géométriques) sont gérés par <see cref="ScanUppercaseSequences"/>
        /// qui scanne directement le topLatex rendu — l'AST gauche-associatif
        /// ne regroupe pas toujours les lettres ensemble (ex: AB*CD donne
        /// ((A*B)*C)*D, donc CD n'est jamais un sous-Bin direct).
        /// </summary>
        private static AmbiguitySpot? MatchAmbiguity(AstNode node)
        {
            // Règle 2 : Sup d'une lettre 1-char par un Number, MAIS seulement
            // si le Sup est IMPLICITE (issu de la règle Number-tight, ex: x2).
            // Si l'utilisateur a tapé `x^2` explicitement, IsImplicit=false →
            // pas d'ambig (l'utilisateur a déjà tranché en mettant le ^).
            if (node is Sup s && s.IsImplicit
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
