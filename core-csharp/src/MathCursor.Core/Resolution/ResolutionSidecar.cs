using System.Collections.Generic;

namespace MathCursor.Core.Resolution
{
    /// <summary>
    /// Sidecar de résolutions de désambiguïsation persisté à côté d'une
    /// source brute. Deux composants complémentaires :
    /// <list type="number">
    ///   <item><b>SpanPins</b> : choix explicite par offset (« cet AB précis
    ///     a été résolu en vec »). Réversible mais sans ambiguïté.</item>
    ///   <item><b>ZoneVotes</b> : compteurs par règle/alternative qui
    ///     boostent le ranking des futurs spans non-résolus dans la même
    ///     zone (« 3 votes vec ici → un nouveau span 2-majuscules tombe
    ///     auto sur vec »).</item>
    /// </list>
    ///
    /// Le sidecar vit avec la source au niveau zone (chaîne cross-merge,
    /// ou OMath single-line). Au cross-merge, fusion via <see cref="SidecarMerger"/>.
    ///
    /// Cf. brief 06-05 sidecar-de-resolutions, ADR à venir.
    /// </summary>
    public sealed class ResolutionSidecar
    {
        /// <summary>(Legacy v1) Pins explicites span → alt choisie. Ordre =
        /// ordre d'ajout. Conservés pendant la migration v2 (cf. brief
        /// 2026-05-07-rule-pin-span-override-refactor) — au load d'un sidecar
        /// v1, les SpanPins restent peuplés et continuent à fonctionner via
        /// le pin matching span-level dans <c>ZoneResolver</c>. Vides
        /// au save v2.</summary>
        public IReadOnlyList<SpanPin> SpanPins { get; }

        /// <summary>(Legacy v1) Compteurs de votes par règle. Au load v1,
        /// convertis en <see cref="RulePins"/> via argmax (cf. décision
        /// 2026-05-07 #3 « convertir »). ZoneVotes restent lus tant que
        /// du contenu legacy circule, jamais écrits en v2.</summary>
        public IReadOnlyDictionary<string, IReadOnlyDictionary<int, int>> ZoneVotes { get; }

        /// <summary>(v2) Pins de règle session-wide : « pour cette rule,
        /// l'utilisateur préfère cette alt ». S'applique à toutes les ambig
        /// de cette rule dans la zone, sans attache span-level.</summary>
        public IReadOnlyList<RulePin> RulePins { get; }

        /// <summary>(v2) Overrides locaux par span identifié via
        /// <see cref="MatchSignature"/>. Précédence dans
        /// <c>ZoneResolver</c> : SpanOverride > RulePin > scoring contextuel
        /// > default.</summary>
        public IReadOnlyList<SpanOverride> SpanOverrides { get; }

        /// <summary>Sidecar vide (aucun pin/override/vote). Utilisé comme
        /// no-op pour le code qui ne sait pas (encore) gérer les sidecars.</summary>
        public static ResolutionSidecar Empty { get; } = new ResolutionSidecar(
            new List<SpanPin>(),
            new Dictionary<string, IReadOnlyDictionary<int, int>>(),
            new List<RulePin>(),
            new List<SpanOverride>());

        // Constructeur legacy (rétrocompat des callers v1).
        public ResolutionSidecar(
            IReadOnlyList<SpanPin> spanPins,
            IReadOnlyDictionary<string, IReadOnlyDictionary<int, int>> zoneVotes)
            : this(spanPins, zoneVotes, null, null) { }

        public ResolutionSidecar(
            IReadOnlyList<SpanPin>? spanPins,
            IReadOnlyDictionary<string, IReadOnlyDictionary<int, int>>? zoneVotes,
            IReadOnlyList<RulePin>? rulePins,
            IReadOnlyList<SpanOverride>? spanOverrides)
        {
            SpanPins = spanPins ?? new List<SpanPin>();
            ZoneVotes = zoneVotes ?? new Dictionary<string, IReadOnlyDictionary<int, int>>();
            RulePins = rulePins ?? new List<RulePin>();
            SpanOverrides = spanOverrides ?? new List<SpanOverride>();
        }

        /// <summary>Renvoie le pin qui couvre exactement <paramref name="offset"/>
        /// avec longueur <paramref name="len"/> pour la règle, ou <c>null</c>
        /// si aucun pin ne match. Pas de match partiel : un pin sur [0..2)
        /// ne matche pas une lookup [0..3).</summary>
        public SpanPin? FindPin(string? rule, int offset, int len)
        {
            if (string.IsNullOrEmpty(rule)) return null;
            foreach (var p in SpanPins)
            {
                if (p.Rule == rule && p.Offset == offset && p.Len == len)
                    return p;
            }
            return null;
        }

        /// <summary>Compte de votes pour cette alt dans cette règle. 0 si absent.</summary>
        public int GetVote(string? rule, int altIdx)
        {
            if (rule == null || string.IsNullOrEmpty(rule)) return 0;
            if (!ZoneVotes.TryGetValue(rule, out var byAlt)) return 0;
            return byAlt.TryGetValue(altIdx, out var count) ? count : 0;
        }

        /// <summary>True si le sidecar n'a aucun pin (legacy ou v2),
        /// aucun vote, aucun override.</summary>
        public bool IsEmpty
            => SpanPins.Count == 0
               && ZoneVotes.Count == 0
               && RulePins.Count == 0
               && SpanOverrides.Count == 0;
    }
}
