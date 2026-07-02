// MathCursor — capture d'intention mathématique depuis une saisie clavier linéaire.
// Copyright (C) 2026  Côme de Percin
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

using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MathCursor.Host.Feedback;

namespace MathCursor.Host.Update
{
    /// <summary>
    /// Vérifie au démarrage s'il existe une version plus récente en ligne, pour
    /// l'indicateur « MAJ dispo » sur l'onglet ruban (cf. ADR
    /// 2026-06-18-Feat-ribbon-update-badge). Best-effort, fire-and-forget,
    /// single-flight, même esprit que <c>UsageStatsClient</c> : aucune donnée
    /// envoyée (simple GET), hors-ligne/erreur → pas de marqueur, jamais de gel.
    /// </summary>
    internal static class UpdateChecker
    {
        private static readonly HttpClient SharedClient = CreateClient();
        private static int _checked; // 0 = jamais lancé, 1 = en cours/fait

        /// <summary>Vrai si une version plus récente que la courante est dispo.</summary>
        public static bool UpdateAvailable { get; private set; }

        /// <summary>Dernière version annoncée par le serveur (pour le texte « X dispo »).</summary>
        public static string LatestVersion { get; private set; } = "";

        private static HttpClient CreateClient()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)12288; } catch { }
            }
            catch { }
            return new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        }

        /// <summary>
        /// GET de l'endpoint version (une seule fois par session). Si une version
        /// plus récente existe, lève <see cref="UpdateAvailable"/> et invoque
        /// <paramref name="onChanged"/> (pour rafraîchir le ruban).
        /// </summary>
        public static async Task CheckAsync(Action onChanged)
        {
            if (Interlocked.CompareExchange(ref _checked, 1, 0) != 0) return;
            try
            {
                var response = await SharedClient
                    .GetAsync(FeedbackSenderFactory.ResolveVersionUrl())
                    .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    Log($"version_http_{(int)response.StatusCode}");
                    return;
                }

                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                string latest = VersionCompare.ExtractLatest(json);
                if (string.IsNullOrEmpty(latest)) { Log("version_parse_empty"); return; }

                string current = CurrentVersion();
                if (VersionCompare.IsNewer(latest, current))
                {
                    LatestVersion = latest;
                    UpdateAvailable = true;
                    Log($"update_available latest={latest} current={current}");
                    try { onChanged?.Invoke(); } catch (Exception exC) { Log("onchanged_error: " + exC.Message); }
                }
                else
                {
                    Log($"up_to_date latest={latest} current={current}");
                }
            }
            catch (Exception ex)
            {
                Log("version_check_error: " + ex.Message);
            }
        }

        /// <summary>Version courante de l'add-in (Major.Minor.Build) — la même que
        /// celle affichée dans « À propos ».</summary>
        public static string CurrentVersion()
        {
            try
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return $"{v.Major}.{v.Minor}.{v.Build}";
            }
            catch { return "0.0.0"; }
        }

        private static void Log(string message)
        {
            try
            {
                var dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MathCursor", "logs");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "mathcursor.log"),
                    $"{DateTime.UtcNow:o} update {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
