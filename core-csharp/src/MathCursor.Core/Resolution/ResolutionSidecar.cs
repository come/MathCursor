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
        /// <summary>Pins explicites span → alt choisie. Ordre = ordre d'ajout.</summary>
        public IReadOnlyList<SpanPin> SpanPins { get; }

        /// <summary>Compteurs de votes par règle. Map <c>rule → (altIdx → count)</c>.
        /// Permet l'accumulation : choisir vec 3 fois sur une zone donne
        /// <c>{ "two-uppercase": { 0: 3 } }</c>.</summary>
        public IReadOnlyDictionary<string, IReadOnlyDictionary<int, int>> ZoneVotes { get; }

        /// <summary>Sidecar vide (pas de pins, pas de votes). Utilisé comme
        /// no-op pour le code qui ne sait pas (encore) gérer les sidecars.</summary>
        public static ResolutionSidecar Empty { get; } = new ResolutionSidecar(
            new List<SpanPin>(),
            new Dictionary<string, IReadOnlyDictionary<int, int>>());

        public ResolutionSidecar(
            IReadOnlyList<SpanPin> spanPins,
            IReadOnlyDictionary<string, IReadOnlyDictionary<int, int>> zoneVotes)
        {
            SpanPins = spanPins ?? new List<SpanPin>();
            ZoneVotes = zoneVotes ?? new Dictionary<string, IReadOnlyDictionary<int, int>>();
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

        /// <summary>True si le sidecar n'a aucun pin et aucun vote.</summary>
        public bool IsEmpty => SpanPins.Count == 0 && ZoneVotes.Count == 0;
    }
}
