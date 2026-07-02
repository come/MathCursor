// MathCursor — capture d'intention mathématique depuis une saisie clavier linéaire.
// Copyright (C) 2026  Côme Percin
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

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
