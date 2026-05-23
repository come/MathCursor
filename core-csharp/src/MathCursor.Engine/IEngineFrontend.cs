namespace MathCursor.Engine
{
    /// <summary>
    /// Contrat du moteur de détection MathCursor v2. Drop-in derrière le
    /// contrat <c>ZoneResolver</c> existant (= L1 Core). Cf. ADR
    /// <c>2026-05-22-Feat-engine-poc-isolation</c>.
    /// </summary>
    public interface IEngineFrontend
    {
        /// <summary>
        /// Résout <paramref name="source"/> en <see cref="EngineResult"/>.
        /// Pas d'effet de bord (pure fonction). Pas d'exception sur input
        /// mal-formé : retourne <see cref="EngineResult.Empty"/> ou un
        /// résultat partiel avec <c>IsComplete=false</c>.
        /// </summary>
        EngineResult Resolve(string source);
    }
}
