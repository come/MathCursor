using MathCursor.Engine;
using Microsoft.JSInterop;

namespace MathCursor.Demo.WebAssembly;

/// <summary>
/// Pont JS↔.NET de la démo web. Expose le <see cref="ForestEngine"/> — LE même
/// moteur que la version Word — via <c>[JSInvokable]</c>.
///
/// <para><see cref="Analyze"/> retourne la décision (auto/popup/erreur), les
/// candidats classés (LaTeX + coût) et le drapeau « expression dense ». C'est
/// l'image fidèle de ce que la popup Word affiche : le candidat 0 est le
/// présélectionné, les suivants sont les alternatives.</para>
/// </summary>
public static class Bridge
{
    /// <summary>DTO sérialisé vers JS via System.Text.Json (camelCase).</summary>
    public sealed class DemoResult
    {
        public string Decision { get; set; } = "erreur";   // "auto" | "popup" | "erreur"
        public bool HasNote { get; set; }
        public DemoCand[] Candidates { get; set; } = System.Array.Empty<DemoCand>();
    }

    public sealed class DemoCand
    {
        public string Latex { get; set; } = "";
        public double Cost { get; set; }
    }

    /// <summary>
    /// Analyse <paramref name="input"/> avec la culture math demandée
    /// (<paramref name="culture"/> = "us" → point décimal + matrices [],
    /// sinon FR = virgule décimale + matrices ()). Jamais d'exception vers le
    /// worker JS : tout échec retombe sur Decision="erreur".
    /// </summary>
    [JSInvokable]
    public static DemoResult Analyze(string input, string culture)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new DemoResult();

        try
        {
            var cult = string.Equals(culture, "us", System.StringComparison.OrdinalIgnoreCase)
                ? EngineCulture.Us
                : EngineCulture.Fr;

            var r = ForestEngine.Analyze(input, cult);
            var cands = new DemoCand[r.Ranked.Count];
            for (int i = 0; i < r.Ranked.Count; i++)
                cands[i] = new DemoCand { Latex = r.Ranked[i].Latex, Cost = r.Ranked[i].Cost };

            return new DemoResult { Decision = r.Decision, HasNote = r.HasNote, Candidates = cands };
        }
        catch
        {
            return new DemoResult { Decision = "erreur" };
        }
    }
}
