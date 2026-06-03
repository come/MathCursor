using System.Collections.Generic;

namespace MathCursor.Core.Lattice
{
    // DTOs de RÉSULTAT de désambiguïsation — modèle partagé, consommé aussi par
    // le moteur v2 (EngineToResolvedZone, ResolvedZone.Spot/AllMatches) et la
    // popup. Extraits de AlternativeGenerator.cs (palier 0 du retrait Lattice,
    // ADR 2026-06-03) pour qu'ils survivent à la suppression du générateur
    // legacy. Le NAMESPACE reste MathCursor.Core.Lattice (zéro churn de `using`).

    /// <summary>
    /// Mutation à appliquer sur la SOURCE d'entrée (la chaîne tapée par
    /// l'utilisateur, pas le LaTeX rendu) pour résoudre une ambiguïté. Quand
    /// <see cref="AmbiguitySpot.Mutation"/> est non null, l'adapter doit
    /// remplacer source[Offset..Offset+Length] par <see cref="Replacement"/>
    /// puis re-déclencher le pipeline complet (Lex → TopK → Parse → Render)
    /// au lieu de faire une sub-string sur le LaTeX rendu.
    ///
    /// Modèle plus robuste que la sub LaTeX : la source reste la vérité, le
    /// rendu est une projection. Permet aux règles comme V→forall de
    /// déclencher un re-parsing en mode scope où les espaces deviennent
    /// séparateurs d'arguments.
    /// </summary>
    public sealed class SourceMutation
    {
        public int Offset { get; }
        public int Length { get; }
        public string Replacement { get; }
        public SourceMutation(int offset, int length, string replacement)
        {
            Offset = offset;
            Length = length;
            Replacement = replacement;
        }
    }

    /// <summary>
    /// Une alternative dans une ambiguïté : son aperçu LaTeX et sa mutation
    /// source optionnelle. Si <see cref="Mutation"/> est null, l'alternative
    /// est appliquée par sub LaTeX (mode legacy AB/ABC/x2). Si non null,
    /// l'adapter doit muter la source brute et relancer le pipeline.
    /// Permet d'avoir des alts hétérogènes dans le même Spot (ex: V → [V
    /// identity sans mutation, ∀ avec mutation forall, √ avec mutation racine]).
    /// </summary>
    public sealed class AmbiguityAlternative
    {
        public string Latex { get; }
        public SourceMutation? Mutation { get; }
        public AmbiguityAlternative(string latex, SourceMutation? mutation = null)
        {
            Latex = latex;
            Mutation = mutation;
        }
    }

    /// <summary>
    /// Une ambiguïté détectée localement dans l'AST top-1 : un sous-AST qui a
    /// une lecture par défaut (le LaTeX intégré dans top-1) et N alternatives.
    /// <see cref="RuleId"/> identifie la RÈGLE qui a généré cette ambiguïté
    /// (ex: "two-uppercase") — utilisé par la popup pour appliquer une
    /// préférence apprise (l'utilisateur a déjà choisi vec pour AB → on
    /// applique vec automatiquement aux CD, EF, … de la session).
    ///
    /// Chaque alternative porte sa propre mutation source via
    /// <see cref="AmbiguityAlternative.Mutation"/>. Permet à un même Spot de
    /// proposer des choix hétérogènes (V → V identity / ∀ scope / √ scope).
    /// </summary>
    public sealed class AmbiguitySpot
    {
        public string RuleId { get; }
        public string DefaultLatex { get; }
        public IReadOnlyList<AmbiguityAlternative> Alternatives { get; }
        public AmbiguitySpot(string ruleId, string defaultLatex,
            IReadOnlyList<AmbiguityAlternative> alternatives)
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

        /// <summary>
        /// Identifiant léger et stable du match (cf. brief
        /// <c>2026-05-07-rule-pin-span-override-refactor</c>).
        /// </summary>
        public MathCursor.Core.Resolution.MatchSignature? Signature { get; }

        /// <summary>
        /// AltIdx que <c>ZoneResolver.ResolveBestAlt</c> a appliqué pour ce
        /// match (= alt active visible dans le TopLatex post-splice). <c>-1</c>
        /// = aucune alt appliquée (= default rule reste). Utilisé par la
        /// popup pour filtrer l'alt active de la liste affichée et garantir
        /// l'invariant « la finale n'apparaît jamais dans les alts »
        /// (demande user 2026-05-07).
        /// </summary>
        public int AppliedAltIdx { get; }

        public AmbiguityMatch(AmbiguitySpot spot, int start, int end)
            : this(spot, start, end, null, -1) { }

        public AmbiguityMatch(AmbiguitySpot spot, int start, int end,
            MathCursor.Core.Resolution.MatchSignature? signature)
            : this(spot, start, end, signature, -1) { }

        public AmbiguityMatch(AmbiguitySpot spot, int start, int end,
            MathCursor.Core.Resolution.MatchSignature? signature, int appliedAltIdx)
        {
            Spot = spot;
            Start = start;
            End = end;
            Signature = signature;
            AppliedAltIdx = appliedAltIdx;
        }

        public AmbiguityMatch WithSignature(MathCursor.Core.Resolution.MatchSignature signature)
            => new AmbiguityMatch(Spot, Start, End, signature, AppliedAltIdx);

        /// <summary>Marque l'altIdx appliqué par le ZoneResolver pour ce match.</summary>
        public AmbiguityMatch WithAppliedAlt(int appliedAltIdx)
            => new AmbiguityMatch(Spot, Start, End, Signature, appliedAltIdx);
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
}
