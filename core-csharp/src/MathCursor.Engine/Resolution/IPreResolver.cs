using System.Collections.Generic;
using MathCursor.Engine.Tokenization;

namespace MathCursor.Engine.Resolution
{
    /// <summary>
    /// Pre-resolver : tente de résoudre un flux de tokens en short-circuit
    /// avant le main loop de <see cref="MathEngine.Resolve"/>. Si match,
    /// retourne un <see cref="EngineResult"/> direct. Si pas, retourne
    /// <c>null</c> et le main loop continue.
    ///
    /// <para>Utilisé pour les patterns structurels (= multi-line align*/cases,
    /// prefix-match popup as-you-type) qui ne s'expriment pas naturellement
    /// comme une <see cref="Rules.RuleSpec"/> YAML. Le main loop itère la
    /// liste des pre-resolvers dans l'ordre, premier match wins.</para>
    ///
    /// <para>Migration Chantier 3 (2026-05-25) : extraction des pre-passes
    /// inline de <c>MathEngine.Resolve</c> vers des modules dédiés.</para>
    /// </summary>
    public interface IPreResolver
    {
        /// <summary>Tente de résoudre <paramref name="tokens"/>. Retourne
        /// <c>null</c> si pas de match (= fallback main loop).</summary>
        EngineResult? TryResolve(IReadOnlyList<Token> tokens);
    }
}
