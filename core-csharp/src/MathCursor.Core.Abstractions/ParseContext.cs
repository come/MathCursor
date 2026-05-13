using System.Collections.Generic;

namespace MathCursor.Core.Abstractions;

/// <summary>
/// Contexte traversé tout au long du pipeline parse → render. Porte la locale
/// courante et un sac de signaux scoped (préfs user, sidecar, hints contextuels).
///
/// <para>Une stratégie / un parseur de domaine / un sérialiseur reçoit le
/// <see cref="ParseContext"/> à l'exécution pour qu'aucune décision n'ait à
/// passer par un paramètre <c>locale</c> ou <c>domain</c> nominal. Ce
/// porteur est ce qui rend les axes orthogonaux : un code Core ne « connaît »
/// pas la locale, il consulte le contexte qu'il reçoit.</para>
///
/// <para>Cf. brief <c>MATHCURSOR_ARCHITECTURE_EXTENSIBILITE.md</c>, anti-pattern
/// nº 2 : « Paramètre <c>domain</c>/<c>locale</c>/<c>language</c> dans une
/// méthode du moteur math → utiliser le ParseContext qui le porte
/// implicitement ».</para>
/// </summary>
public sealed class ParseContext
{
    /// <summary>Locale d'entrée active (BCP 47, ex. <c>"fr-FR"</c>, <c>"en-US"</c>).</summary>
    public string LocaleId { get; }

    /// <summary>Domaine de notation actif (ex. <c>"math"</c>, <c>"chemistry"</c>).
    /// Sert au routeur d'axe B ; un parseur de domaine n'a normalement pas besoin
    /// de le lire (il sait déjà dans quel domaine il opère).</summary>
    public string DomainId { get; }

    /// <summary>Sac de propriétés scoped, opaque au Core. Utilisé pour transporter
    /// des signaux additionnels (préfs user, sidecar fragment, hint de rendu, etc.)
    /// sans polluer la signature des stratégies.</summary>
    public IReadOnlyDictionary<string, object?> Properties { get; }

    public ParseContext(
        string localeId,
        string domainId = "math",
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        LocaleId = localeId ?? "fr-FR";
        DomainId = domainId ?? "math";
        Properties = properties ?? new Dictionary<string, object?>();
    }

    /// <summary>Contexte par défaut (FR / math / sans propriété) — utile aux
    /// tests et aux call-sites simples qui n'ont pas encore migré.</summary>
    public static ParseContext Default { get; } = new ParseContext("fr-FR");
}
