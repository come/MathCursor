using System.Text;
using MathCursor.Core;
using MathCursor.Engine;

namespace MathCursor.Engine.Adapter
{
    /// <summary>
    /// Implémentation <see cref="IResolvedZoneSource"/> branchée sur
    /// <see cref="IEngineFrontend"/>. Permet de connecter un
    /// <see cref="ZoneResolver"/> au moteur v2 sans toucher le Core.
    ///
    /// <para>Politique P32.1 (2026-05-23) : <b>Engine v2 ne retourne JAMAIS
    /// <c>null</c></b>, sauf exception fatale (= rare). Quand
    /// <c>TopLatex</c> est vide, on synthétise un <see cref="ResolvedZone"/>
    /// identité (= source brute rendue telle quelle, pas de candidats).
    /// Conséquence : le legacy n'est plus appelé par le path normal — il
    /// reste comme filet de sécurité uniquement sur exception ou via le
    /// kill-switch <c>MATHCURSOR_ENGINE_V2=0</c>. Cf. ADR
    /// <c>2026-05-23-Feat-engine-v2-promotion</c>.</para>
    ///
    /// <para>Trace diag = liste lisible des décisions pour l'inspecteur VSTO.</para>
    /// </summary>
    public sealed class EngineZoneSource : IResolvedZoneSource
    {
        private readonly IEngineFrontend _engine;

        public EngineZoneSource(IEngineFrontend engine)
        {
            _engine = engine ?? throw new System.ArgumentNullException(nameof(engine));
        }

        public ResolvedZone? TryResolve(string rawSource, out string diagTrace)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"engine-v2 source=\"{rawSource ?? string.Empty}\"");
            try
            {
                var result = _engine.Resolve(rawSource ?? string.Empty);
                sb.AppendLine($"engine-v2 top=\"{Truncate(result.TopLatex, 200)}\"");
                sb.AppendLine($"engine-v2 rule={result.RuleId} complete={result.IsComplete} collisions={result.Collisions.Count}");
                if (result.Collisions.Count > 0)
                {
                    for (int i = 0; i < result.Collisions.Count; i++)
                    {
                        var c = result.Collisions[i];
                        sb.AppendLine($"  cand[{i}] rule={c.RuleId} score={c.Score} latex=\"{Truncate(c.Latex, 100)}\"");
                    }
                }

                if (string.IsNullOrEmpty(result.TopLatex))
                {
                    // P32.1 : on ne tombe PLUS sur le legacy quand TopLatex
                    // est vide. À la place : ResolvedZone identité (= source
                    // brute en topLatex, pas de candidats). Évite tout
                    // re-appel au moteur legacy `[Obsolete]`.
                    sb.AppendLine("engine-v2 empty → identity ResolvedZone (no legacy fallback)");
                    diagTrace = sb.ToString();
                    return EngineToResolvedZone.Map(
                        rawSource ?? string.Empty,
                        new EngineResult(
                            topLatex: rawSource ?? string.Empty,
                            isComplete: true,
                            collisions: System.Array.Empty<EngineCandidate>(),
                            ruleId: "engine-v2-identity"));
                }

                var resolved = EngineToResolvedZone.Map(rawSource ?? string.Empty, result);
                diagTrace = sb.ToString();
                return resolved;
            }
            catch (System.Exception ex)
            {
                // P32.1 : seule porte d'entrée résiduelle vers le legacy.
                // Cas attendu : exception fatale dans Engine v2 — le legacy
                // sert de filet pour ne pas casser l'utilisateur.
                sb.AppendLine($"engine-v2 ERROR {ex.GetType().Name}: {ex.Message} → fallback legacy (= seul cas restant)");
                diagTrace = sb.ToString();
                return null;
            }
        }

        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "…";
        }
    }
}
