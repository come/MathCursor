// MathCursor: capturing mathematical intent from linear keyboard input.
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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MathCursor.Host.Feedback;
using MathCursor.Host.Settings;

namespace MathCursor.Host.Usage
{
    /// <summary>
    /// Envoie le compteur d'usage anonyme accumulé localement
    /// (<see cref="UsageCounter"/>) vers l'endpoint <c>/api/v1/usage</c>.
    /// <para>
    /// Payload minimal : <c>{ "count": &lt;pending&gt;, "version": "x.y.z" }</c>.
    /// Aucun identifiant, aucun contenu. Cf. ADR
    /// 2026-06-18-Feat-usage-counter-telemetry.
    /// </para>
    /// <para>
    /// Best-effort : appelé en fire-and-forget sur perte de focus
    /// (<c>WindowDeactivate</c>) et au <c>Shutdown</c>. N'envoie rien si l'opt-out
    /// est coupé ou si le compteur est à zéro (anti-spam : un alt-tab après un
    /// flush réussi ne déclenche aucune requête). Ne lève jamais vers l'appelant.
    /// </para>
    /// </summary>
    internal static class UsageStatsClient
    {
        // HttpClient statique partagé (même rationale que HttpFeedbackSender :
        // évite l'épuisement des sockets, force TLS 1.2+ que Cloudflare exige).
        private static readonly HttpClient SharedClient = CreateClient();

        // Single-flight : un seul flush à la fois. Sans ça, deux flushs
        // concurrents (alt-tabs rapprochés) peuvent capturer le même `pending`
        // et l'envoyer deux fois (double-comptage serveur + ClearSent qui perd
        // des unités légitimes). 0 = libre, 1 = en cours.
        private static int _flushing;

        private static HttpClient CreateClient()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)12288; } catch { }
            }
            catch { }
            return new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        }

        /// <summary>
        /// Tente d'envoyer le compteur en attente. No-op si opt-out coupé ou
        /// compteur à zéro. Sur 2xx : retranche le nombre transmis ; sur échec :
        /// conserve le compteur (retry au prochain flush).
        /// </summary>
        public static async Task FlushAsync()
        {
            if (!SettingsStore.Current.SendUsageStats) return;

            // Un seul flush à la fois : si un est déjà en cours, on laisse tomber
            // (le compteur sera repris au prochain flush). Pas d'empilement de
            // requêtes ni de double-envoi.
            if (Interlocked.CompareExchange(ref _flushing, 1, 0) != 0) return;
            try
            {
                int pending = UsageCounter.GetPending();
                if (pending <= 0) return;

                string json = "{\"count\":" + pending
                    + ",\"version\":\"" + EscapeJson(SafeVersion()) + "\"}";

                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                {
                    var response = await SharedClient
                        .PostAsync(FeedbackSenderFactory.ResolveUsageUrl(), content)
                        .ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                        UsageCounter.ClearSent(pending);
                    else
                        Log($"usage_flush_http_{(int)response.StatusCode} (gardé {pending})");
                }
            }
            catch (Exception ex)
            {
                // Réseau/serveur injoignable : on garde le compteur, retry plus tard.
                Log("usage_flush_error: " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _flushing, 0);
            }
        }

        private static string SafeVersion()
        {
            try
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return $"{v.Major}.{v.Minor}.{v.Build}";
            }
            catch { return "?"; }
        }

        // La version est un littéral simple ("1.2.3"), mais on échappe par
        // prudence si l'AssemblyVersion renvoyait un caractère inattendu.
        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
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
                    $"{DateTime.UtcNow:o} usage {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
