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
using System.IO;

namespace MathCursor.Host.Usage
{
    /// <summary>
    /// Compteur d'usage anonyme, persisté dans
    /// <c>%AppData%\MathCursor\usage.json</c> (<c>{ "pending": N }</c>).
    /// <para>
    /// <c>pending</c> = nombre de formules converties pas encore confirmées
    /// envoyées au serveur. Incrémenté à chaque conversion réussie (uniquement
    /// si l'opt-out <see cref="Settings.AppSettings.SendUsageStats"/> est actif),
    /// retranché du nombre effectivement transmis après un flush réussi.
    /// </para>
    /// <para>
    /// Aucune donnée personnelle, aucun identifiant, aucun contenu : juste un
    /// entier. Cf. ADR 2026-06-18-Feat-usage-counter-telemetry. Best-effort :
    /// un disque qui refuse l'écriture ne lève jamais vers l'appelant (au pire
    /// on perd quelques unités de comptage — sans gravité).
    /// </para>
    /// </summary>
    internal static class UsageCounter
    {
        private static readonly object _lock = new object();
        private static bool _loaded;
        private static int _pending;
        private static string _filePath;

        /// <summary>Chemin du fichier compteur. En prod : lazy
        /// <c>%AppData%\MathCursor\usage.json</c>. Le setter (réservé aux tests)
        /// repointe le fichier et réinitialise l'état en mémoire pour l'isolation.</summary>
        internal static string FilePath
        {
            get => _filePath ?? (_filePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MathCursor", "usage.json"));
            set { lock (_lock) { _filePath = value; _loaded = false; _pending = 0; } }
        }

        /// <summary>+1 conversion réussie. No-op silencieux si l'écriture échoue.</summary>
        public static void Increment()
        {
            lock (_lock)
            {
                EnsureLoaded();
                if (_pending < int.MaxValue) _pending++;
                Persist();
            }
        }

        /// <summary>Nombre en attente d'envoi.</summary>
        public static int GetPending()
        {
            lock (_lock)
            {
                EnsureLoaded();
                return _pending;
            }
        }

        /// <summary>
        /// Retranche <paramref name="n"/> unités après un envoi réussi (pas un
        /// reset brut : on préserve les incréments arrivés pendant l'envoi async).
        /// </summary>
        public static void ClearSent(int n)
        {
            if (n <= 0) return;
            lock (_lock)
            {
                EnsureLoaded();
                _pending = Math.Max(0, _pending - n);
                Persist();
            }
        }

        // ── Persistance ──────────────────────────────────────────────────

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _pending = ReadFromDisk();
            _loaded = true;
        }

        private static int ReadFromDisk()
        {
            try
            {
                if (!File.Exists(FilePath)) return 0;
                var json = File.ReadAllText(FilePath);
                var v = ExtractInt(json, "pending");
                return v.HasValue && v.Value > 0 ? v.Value : 0;
            }
            catch (Exception ex)
            {
                Log("usage_read_error: " + ex.Message + " → 0");
                return 0;
            }
        }

        private static void Persist()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, "{\"pending\":" + _pending + "}");
            }
            catch (Exception ex)
            {
                Log("usage_write_error: " + ex.Message);
                // Valeur gardée en mémoire pour la session même si le disque refuse.
            }
        }

        // Même approche que SettingsStore.ExtractInt : un petit littéral entier
        // par clé connue, pas de parseur JSON complet.
        private static int? ExtractInt(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;
            string needle = "\"" + key + "\":";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return null;
            i += needle.Length;
            while (i < json.Length && json[i] == ' ') i++;
            int start = i;
            while (i < json.Length && (char.IsDigit(json[i]) || json[i] == '-')) i++;
            if (i == start) return null;
            return int.TryParse(json.Substring(start, i - start), out var v) ? v : (int?)null;
        }

        private static void Log(string message)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MathCursor", "logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "mathcursor.log"),
                    $"{DateTime.UtcNow:o} usage {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
