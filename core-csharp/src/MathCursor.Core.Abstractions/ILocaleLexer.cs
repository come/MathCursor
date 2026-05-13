using System.Collections.Generic;

namespace MathCursor.Core.Abstractions;

/// <summary>
/// <b>Axe C — Locale d'entrée naturelle.</b>
/// Lexique locale-aware : sait quels mots-clés français (resp. anglais,
/// allemand…) traduire en symboles canoniques du moteur math.
///
/// <para>Le moteur de notation symbolique (LaTeX-like, math pure) reste
/// locale-agnostic — <c>\frac{a}{b}</c> est le même partout. La couche de
/// langage naturel (mots-clés, prépositions, ordre) est locale-specific.</para>
///
/// <para>Invariants imposés par l'archi :</para>
/// <list type="bullet">
/// <item>Aucune chaîne française hardcodée dans le Core. Toutes les
///   comparaisons de mots-clés passent par cette interface.</item>
/// <item>Une locale = un fichier YAML de ressources + une implémentation
///   triviale qui le charge.</item>
/// </list>
///
/// <para>Cf. brief <c>MATHCURSOR_ARCHITECTURE_EXTENSIBILITE.md</c> §3.C.</para>
/// </summary>
public interface ILocaleLexer
{
    /// <summary>Identifiant BCP 47 de la locale (ex. <c>"fr-FR"</c>,
    /// <c>"en-US"</c>, <c>"de-DE"</c>).</summary>
    string LocaleId { get; }

    /// <summary>Mapping mot-clé naturel → symbole canonique du moteur math.
    /// <para>Ex. FR : <c>"racine de" → "sqrt"</c>, <c>"fraction" → "frac"</c>,
    /// <c>"intégrale" → "int"</c>.</para>
    /// <para>Ex. EN : <c>"square root of" → "sqrt"</c>.</para>
    /// </summary>
    /// <remarks>Les clés sont en lowercased — le lexer Core applique
    /// <c>ToLowerInvariant</c> avant lookup.</remarks>
    IReadOnlyDictionary<string, string> Keywords { get; }
}
