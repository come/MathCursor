using System;
using System.Text;

namespace MathCursor.Host.CCMeta
{
    /// <summary>
    /// Sérialiseur / parseur manuel pour <see cref="MCMeta"/>. Pas de
    /// Newtonsoft / System.Text.Json — alignement sur la convention du
    /// projet (cf. <c>FeedbackJson</c>). Schéma plat + clés connues =
    /// parser minimal suffisant.
    /// </summary>
    internal static class MCMetaJson
    {
        /// <summary>Identifiant Title du CC qui marque une formule MathCursor.
        /// Sert de filtre rapide pour `cc.Title == CcTitle` lors du backlink.</summary>
        public const string CcTitle = "MathCursor";

        public static string Serialize(MCMeta m)
        {
            if (m == null) return "{}";
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"v\":").Append(m.V);
            WriteString(sb, "handle_id", m.HandleId);
            WriteString(sb, "steno", m.Steno);
            WriteString(sb, "latex", m.Latex);
            WriteString(sb, "version", m.Version);
            WriteString(sb, "omml_hash", m.OmmlHash);
            sb.Append(",\"parsedAt\":");
            AppendString(sb, m.ParsedAt.ToString("o"));
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>
        /// Parser tolérant minimal. Retourne null si le Tag n'est pas un
        /// JSON reconnaissable ou si <c>v</c> manque. Pour POC : extraction
        /// par recherche de clé "name": "value" — suffisant tant que le
        /// schéma reste plat.
        /// </summary>
        public static MCMeta TryParse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var m = new MCMeta();
                m.V = ExtractInt(json, "v") ?? 0;
                if (m.V == 0) return null;
                m.HandleId = ExtractString(json, "handle_id");
                m.Steno = ExtractString(json, "steno");
                m.Latex = ExtractString(json, "latex");
                m.Version = ExtractString(json, "version");
                m.OmmlHash = ExtractString(json, "omml_hash");
                var parsedStr = ExtractString(json, "parsedAt");
                if (!string.IsNullOrEmpty(parsedStr)
                    && DateTime.TryParse(parsedStr, null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                {
                    m.ParsedAt = dt;
                }
                return m;
            }
            catch { return null; }
        }

        // ── Internals ────────────────────────────────────────────────

        private static void WriteString(StringBuilder sb, string name, string value)
        {
            sb.Append(",\"").Append(name).Append("\":");
            AppendString(sb, value ?? "");
        }

        private static void AppendString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s ?? string.Empty)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        private static string ExtractString(string json, string key)
        {
            string needle = "\"" + key + "\":";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return null;
            i += needle.Length;
            while (i < json.Length && json[i] == ' ') i++;
            if (i >= json.Length || json[i] != '"') return null;
            i++;
            var sb = new StringBuilder();
            while (i < json.Length)
            {
                char c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    char n = json[i + 1];
                    switch (n)
                    {
                        case '\\': sb.Append('\\'); break;
                        case '"': sb.Append('"'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 5 < json.Length
                                && int.TryParse(json.Substring(i + 2, 4),
                                    System.Globalization.NumberStyles.HexNumber, null, out var code))
                            {
                                sb.Append((char)code);
                                i += 4;
                            }
                            break;
                        default: sb.Append(n); break;
                    }
                    i += 2;
                }
                else if (c == '"') return sb.ToString();
                else { sb.Append(c); i++; }
            }
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
            if (int.TryParse(json.Substring(start, i - start), out var v)) return v;
            return null;
        }
    }
}
