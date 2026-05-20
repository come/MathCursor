namespace MathCursor.Core.Abstractions;

/// <summary>
/// <b>Axe B — Domaine de notation.</b>
/// Contrat d'un parseur de domaine complet et indépendant (math, chimie,
/// physique avec unités, logique formelle…). Un domaine = un parseur sibling,
/// pas un flag dans le parseur math.
///
/// <para>Invariants imposés par l'archi :</para>
/// <list type="bullet">
/// <item>Un parseur de domaine ne sait PAS qu'un autre existe.</item>
/// <item>Pas de partage de logique métier entre domaines — uniquement
///   utilitaires bas-niveau (lexer générique de caractères, parsing de nombres).</item>
/// <item>Le routeur (à venir étape 6) sélectionne via auto-détection
///   (<see cref="DomainConfidence"/>) ou via choix utilisateur explicite.</item>
/// </list>
///
/// <para>Cf. brief <c>MATHCURSOR_ARCHITECTURE_EXTENSIBILITE.md</c> §3.B.</para>
/// </summary>
public interface IDomainParser
{
    /// <summary>Identifiant du domaine (ex. <c>"math"</c>, <c>"chemistry"</c>,
    /// <c>"logic"</c>). Stable, utilisé par le routeur et la persistance.</summary>
    string DomainId { get; }

    /// <summary>Score de confiance que <paramref name="rawSpan"/> appartient à
    /// ce domaine. <c>0.0</c> = sûr que non, <c>1.0</c> = sûr que oui. Le
    /// routeur sélectionne le domaine avec le score le plus élevé (sauf si
    /// l'utilisateur a forcé un domaine).</summary>
    /// <remarks>Doit être pur (pas d'effet de bord) et léger : appelé pour
    /// chaque candidat à chaque résolution.</remarks>
    float DomainConfidence(string rawSpan);
}
