using System.Collections.Generic;

namespace MathCursor.Core.Patterns
{
    /// <summary>
    /// Alias pour un opener de slot (ex. les openers du slot <c>domain</c> de
    /// <c>forall-belongs</c> : <c>"app a"</c>, <c>"appartient"</c>, <c>"(-"</c>,
    /// <c>"∈"</c>, <c>"in"</c>, <c>"dans"</c>). Permet de factoriser les
    /// reconnaissances d'aliases avec poids de désambig.
    ///
    /// <para><b>Structure data-ready</b> (option γ du plan P5) : les aliases
    /// vivent en C# pour P5, mais leur forme reflète celle d'un futur YAML
    /// <c>groups/belonging.yaml</c>. Migration P9+ ne touchera pas l'interface
    /// du template, juste la source de ces aliases.</para>
    ///
    /// <para>Si plusieurs aliases matchent à la même position (rare, ex.
    /// <c>in</c> alias mais aussi début d'un identifier <c>intérieur</c>), le
    /// template peut émettre N <see cref="PatternCompletion"/>s triées par
    /// <see cref="Weight"/> — la popup/ranker tranche.</para>
    ///
    /// <para>Cf. ADR <c>2026-05-21-Feat-forall-belongs-pattern</c> (P5).</para>
    /// </summary>
    public sealed class OpenerAlias
    {
        /// <summary>Token source à reconnaître (ex. <c>"app a"</c>,
        /// <c>"appartient"</c>, <c>"(-"</c>).</summary>
        public string Token { get; }

        /// <summary>Forme canonique pour le rendu / la mutation source
        /// (ex. <c>"in"</c> pour tous les openers belonging — le pipeline
        /// lattice rendra ensuite <c>\in</c>).</summary>
        public string Canonical { get; }

        /// <summary>Poids dans la désambig. Plus le token est ambigu (ex.
        /// <c>"in"</c> qui peut être début d'un mot anglais), plus le poids
        /// est bas. Plus le token est spécifique (ex. <c>"∈"</c> unicode
        /// direct), plus le poids est haut.</summary>
        public int Weight { get; }

        /// <summary>Indique si <see cref="Token"/> doit être suivi d'un
        /// caractère non-lettre (boundary droite) pour être considéré comme
        /// un opener. <c>true</c> pour les mots (<c>appartient</c>,
        /// <c>dans</c>, <c>in</c>) pour éviter les faux positifs sur des
        /// préfixes (ex. <c>"in"</c> dans <c>"intérieur"</c>). <c>false</c>
        /// pour les opérateurs (<c>(-</c>, <c>∈</c>) qui ont leurs propres
        /// limites.</summary>
        public bool RequiresWordBoundary { get; }

        /// <summary>Hints contextuels facultatifs (langue, domaine, etc.).
        /// Champ pré-réservé pour la migration YAML — non utilisé en P5.</summary>
        public IReadOnlyDictionary<string, string>? Hints { get; }

        public OpenerAlias(
            string token, string canonical, int weight,
            bool requiresWordBoundary = true,
            IReadOnlyDictionary<string, string>? hints = null)
        {
            Token = token ?? throw new System.ArgumentNullException(nameof(token));
            Canonical = canonical ?? throw new System.ArgumentNullException(nameof(canonical));
            Weight = weight;
            RequiresWordBoundary = requiresWordBoundary;
            Hints = hints;
        }
    }
}
