using System;
using System.IO;
using System.Text;

namespace MathCursor.Host.Settings
{
    /// <summary>
    /// Persistance des <see cref="AppSettings"/> dans
    /// <c>%APPDATA%\MathCursor\settings.json</c> (à côté de <c>logs\</c>).
    /// JSON manuel plat — pas de Newtonsoft / System.Text.Json, alignement
    /// sur la convention du projet (cf. <c>MCMetaJson</c>, <c>FeedbackJson</c>).
    /// Fichier absent / corrompu / version inconnue → défauts + log, jamais
    /// de throw vers l'appelant.
    /// </summary>
    internal static class SettingsStore
    {
        private static AppSettings _current;
        private static string _filePath;

        /// <summary>Réglages courants (chargés au premier accès). Jamais null.</summary>
        public static AppSettings Current => _current ?? (_current = Load());

        /// <summary>Chemin du fichier réglages. En prod : lazy
        /// <c>%AppData%\MathCursor\settings.json</c>. Le setter (réservé aux tests)
        /// repointe le fichier et vide le cache courant pour l'isolation.</summary>
        public static string FilePath
        {
            get => _filePath ?? (_filePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MathCursor", "settings.json"));
            set { _filePath = value; _current = null; }
        }

        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new AppSettings();
                var json = File.ReadAllText(FilePath);
                var s = TryParse(json);
                if (s == null) { Log("settings_parse_failed → défauts"); return new AppSettings(); }
                return s;
            }
            catch (Exception ex)
            {
                Log("settings_load_error: " + ex.Message + " → défauts");
                return new AppSettings();
            }
        }

        /// <summary>Persiste et remplace les réglages courants. False si l'écriture échoue.</summary>
        public static bool Save(AppSettings s)
        {
            if (s == null) return false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, Serialize(s));
                _current = s;
                return true;
            }
            catch (Exception ex)
            {
                Log("settings_save_error: " + ex.Message);
                _current = s; // appliqué pour la session même si le disque refuse
                return false;
            }
        }

        // ── JSON plat ────────────────────────────────────────────────────

        private static string Serialize(AppSettings s)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"v\":").Append(s.V);
            sb.Append(",\"culture\":\"").Append(s.Culture).Append('"');
            // override absent = suit la culture (ne pas écrire null)
            if (s.IntervalSepOverride != null)
                sb.Append(",\"interval_sep\":\"").Append(s.IntervalSepOverride).Append('"');
            if (s.MatrixEnvOverride != null)
                sb.Append(",\"matrix_env\":\"").Append(s.MatrixEnvOverride).Append('"');
            sb.Append(",\"auto_detect\":").Append(s.AutoDetect ? "true" : "false");
            sb.Append(",\"tab_validate\":").Append(s.TabValidate ? "true" : "false");
            sb.Append(",\"send_usage_stats\":").Append(s.SendUsageStats ? "true" : "false");
            // Police math absente = défaut Word (ne pas écrire null). Les noms de
            // fontes n'ont ni guillemet ni backslash → littéral JSON direct.
            if (s.MathFont != null)
                sb.Append(",\"math_font\":\"").Append(s.MathFont).Append('"');
            sb.Append('}');
            return sb.ToString();
        }

        private static AppSettings TryParse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            if (ExtractInt(json, "v") != 1) return null;

            var s = new AppSettings();
            var culture = ExtractString(json, "culture");
            if (culture != AppSettings.CultureFr && culture != AppSettings.CultureUs) return null;
            s.Culture = culture;

            var sep = ExtractString(json, "interval_sep");
            if (sep == ";" || sep == ",") s.IntervalSepOverride = sep;

            var env = ExtractString(json, "matrix_env");
            if (env == "pmatrix" || env == "bmatrix") s.MatrixEnvOverride = env;

            // Clé absente (settings.json antérieur) → défaut true.
            var auto = ExtractBool(json, "auto_detect");
            if (auto.HasValue) s.AutoDetect = auto.Value;

            // Clé absente → défaut false (Tab = tabulation normale).
            var tab = ExtractBool(json, "tab_validate");
            if (tab.HasValue) s.TabValidate = tab.Value;

            // Clé absente (settings.json antérieur) → défaut true.
            var usage = ExtractBool(json, "send_usage_stats");
            if (usage.HasValue) s.SendUsageStats = usage.Value;

            // Clé absente → null = défaut Word (Cambria).
            var mathFont = ExtractString(json, "math_font");
            if (!string.IsNullOrEmpty(mathFont)) s.MathFont = mathFont;

            return s;
        }

        // Valeurs settings = petits littéraux sans échappement ; un parser
        // par clé connue suffit (même approche que MCMetaJson.ExtractString,
        // sans le décodage d'escapes inutile ici).
        private static string ExtractString(string json, string key)
        {
            string needle = "\"" + key + "\":";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return null;
            i += needle.Length;
            while (i < json.Length && json[i] == ' ') i++;
            if (i >= json.Length || json[i] != '"') return null;
            int end = json.IndexOf('"', i + 1);
            return end < 0 ? null : json.Substring(i + 1, end - i - 1);
        }

        private static bool? ExtractBool(string json, string key)
        {
            string needle = "\"" + key + "\":";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return null;
            i += needle.Length;
            while (i < json.Length && json[i] == ' ') i++;
            if (string.CompareOrdinal(json, i, "true", 0, 4) == 0) return true;
            if (string.CompareOrdinal(json, i, "false", 0, 5) == 0) return false;
            return null;
        }

        private static int? ExtractInt(string json, string key)
        {
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
                    $"{DateTime.UtcNow:o} settings {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
