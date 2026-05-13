namespace MathCursor.Core.Abstractions;

/// <summary>
/// Statut d'une sérialisation : succès, fallback (la cible n'a pas le natif
/// pour cette construction → on a émis du LaTeX brut ou un équivalent
/// approximatif), ou échec absolu.
/// </summary>
public enum SerializationStatus
{
    /// <summary>La construction est rendue nativement dans le format cible.</summary>
    Native,

    /// <summary>La construction n'a pas d'équivalent natif ; la sortie utilise
    /// un fallback documenté (LaTeX brut embarqué, image…). L'adapter peut
    /// alerter l'utilisateur.</summary>
    Fallback,

    /// <summary>La construction n'est pas supportable dans le format cible
    /// et l'adapter doit gérer l'erreur.</summary>
    Unsupported,
}

/// <summary>
/// Résultat d'une sérialisation typée. Porte la valeur produite + le statut
/// + un message optionnel (raison du fallback / erreur).
/// </summary>
/// <typeparam name="TFormat">Type concret de la sortie (<c>string</c> pour
/// LaTeX/MathJax/UnicodeMath, <c>System.Xml.Linq.XElement</c> pour OMath
/// natif, etc.).</typeparam>
public sealed class SerializationResult<TFormat>
{
    public TFormat Value { get; }
    public SerializationStatus Status { get; }
    public string? Message { get; }

    public SerializationResult(TFormat value, SerializationStatus status = SerializationStatus.Native, string? message = null)
    {
        Value = value;
        Status = status;
        Message = message;
    }
}

/// <summary>
/// <b>Axe E — Cible de sortie.</b>
/// Sérialiseur AST → format cible. Le parseur produit un AST + une chaîne
/// LaTeX pivot universelle ; chaque cible (OMath pour Word, LaTeX pour
/// VS Code, MathJax pour Obsidian, Unicode pour terminal) a son sérialiseur
/// dédié.
///
/// <para>Invariants imposés par l'archi :</para>
/// <list type="bullet">
/// <item>LaTeX reste le pivot universel. Tout sérialiseur prend l'AST en
///   entrée pour précision, mais peut fallback sur LaTeX si une construction
///   n'est pas supportée nativement.</item>
/// <item>Pas de logique métier dans le sérialiseur : pure projection AST →
///   format. Toute décision (afficher des fractions verticales ou en ligne,
///   etc.) doit être prise au niveau du parser et matérialisée dans l'AST.</item>
/// </list>
///
/// <para><c>TAstRoot</c> est délibérément générique (<c>object</c> côté
/// appelant pour l'instant) — étape 2 du refacto pose le contrat sans
/// coupler Abstractions au type AST concret. Étape 3 pourra spécialiser
/// avec une contrainte (<c>where TAstRoot : AstNode</c>) si pertinent.</para>
///
/// <para>Cf. brief <c>MATHCURSOR_ARCHITECTURE_EXTENSIBILITE.md</c> §3.E.</para>
/// </summary>
/// <typeparam name="TFormat">Type concret de la sortie (<c>string</c>,
/// <c>XElement</c>, etc.).</typeparam>
public interface IOutputSerializer<TFormat>
{
    /// <summary>Identifiant du format cible (ex. <c>"omath"</c>,
    /// <c>"latex"</c>, <c>"mathjax"</c>, <c>"unicode-math"</c>).</summary>
    string FormatId { get; }

    /// <summary>Sérialise un AST vers le format cible.
    /// <para>L'appelant fournit le nœud racine de l'AST (typage faible
    /// volontaire pour ne pas coupler Abstractions à Core ; étape 3 pourra
    /// re-spécialiser via overload ou extension method).</para></summary>
    SerializationResult<TFormat> Serialize(object astRoot, ParseContext context);
}
