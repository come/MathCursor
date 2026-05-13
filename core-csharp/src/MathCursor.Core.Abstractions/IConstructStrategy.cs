namespace MathCursor.Core.Abstractions;

/// <summary>
/// <b>Axe A — Vocabulaire mathématique.</b>
/// Contrat d'une construction notationnelle (fraction, racine, intégrale,
/// matrice, dérivée, etc.). Une stratégie = un fichier dédié qui sait
/// reconnaître un préfixe du flux de tokens et produire son nœud AST.
///
/// <para>Invariants imposés par l'archi :</para>
/// <list type="bullet">
/// <item>Une stratégie ne dépend JAMAIS d'une autre. La composition se fait
///   au niveau du Core via récursion du Visitor / re-parse des arguments.</item>
/// <item>La <see cref="Precedence"/> est déclarative, pas calculée.</item>
/// <item>L'identifiant <see cref="Id"/> est stable (clé d'enregistrement,
///   sérialisation de pin, log).</item>
/// </list>
///
/// <para>L'implémentation concrète arrive en étape 4 du refacto extensibilité.
/// L'interface est posée dès l'étape 2 pour figer le contrat.</para>
///
/// <para>Cf. brief <c>MATHCURSOR_ARCHITECTURE_EXTENSIBILITE.md</c> §3.A.</para>
/// </summary>
public interface IConstructStrategy
{
    /// <summary>Identifiant unique de la construction (ex. <c>"fraction"</c>,
    /// <c>"matrix"</c>, <c>"derivative"</c>, <c>"angle"</c>).</summary>
    string Id { get; }

    /// <summary>Précédence dans la table 0-9 (cf. lattice AST). Plus grand =
    /// plus prioritaire. Déclarative : pas de logique conditionnelle.</summary>
    int Precedence { get; }
}
