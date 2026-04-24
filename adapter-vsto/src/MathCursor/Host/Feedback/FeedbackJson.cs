using System.Text;

namespace MathCursor.Host.Feedback
{
    /// <summary>
    /// Sérialiseur JSON minimal pour FeedbackReport. Fait exprès manuel (pas de
    /// Newtonsoft ni System.Text.Json pour rester sans dépendance externe sur
    /// .NET Framework 4.8). La structure de FeedbackReport est plat et connue :
    /// un assemblage direct est plus sûr et plus simple qu'une réflexion.
    /// </summary>
    internal static class FeedbackJson
    {
        public static string Serialize(FeedbackReport r)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            WriteField(sb, "version", r.Version, first: true);
            WriteField(sb, "timestamp", r.Timestamp.ToString("o"));
            WriteField(sb, "user_id", r.UserId);
            WriteField(sb, "session_id", r.SessionId);
            WriteField(sb, "ner_text", r.NerText);
            WriteField(sb, "recognized_formula", r.RecognizedFormula);
            WriteField(sb, "user_message", r.UserMessage);
            WriteField(sb, "user_email", r.UserEmail);
            WriteField(sb, "log_tail", r.LogTail);
            WriteField(sb, "word_version", r.WordVersion);
            WriteField(sb, "os_version", r.OsVersion);
            sb.Append('}');
            return sb.ToString();
        }

        private static void WriteField(StringBuilder sb, string name, string value, bool first = false)
        {
            if (!first) sb.Append(',');
            sb.Append('"').Append(name).Append("\":");
            AppendString(sb, value ?? "");
        }

        private static void AppendString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
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
    }
}
