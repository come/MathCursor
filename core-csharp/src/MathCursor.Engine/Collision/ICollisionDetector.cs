using System.Collections.Generic;

namespace MathCursor.Engine.Collision
{
    /// <summary>
    /// Détecteur de collision : scanne un <see cref="CollisionContext"/> et
    /// émet 0+ <see cref="EngineCandidate"/> alternatifs.
    ///
    /// <para>Chaque détecteur est <b>local</b> : il regarde les operands
    /// et l'op-stream, génère ses candidats via
    /// <see cref="CollisionContext.ReplaceOperand"/>. La règle s'applique
    /// uniformément au top-level et dans toute expression composée
    /// (= brief v5 §6).</para>
    /// </summary>
    public interface ICollisionDetector
    {
        IEnumerable<EngineCandidate> Detect(CollisionContext ctx);
    }
}
