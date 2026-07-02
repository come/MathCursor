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
            // Format aligné avec l'endpoint Cloudflare Pages Function
            // /api/v1/report (cf. docs/functions/api/v1/report.js et brief
            // 2026-04-30-feedback-form-with-cloudflare-backend.md).
            //
            // Les noms de propriétés C# (NerText, RecognizedFormula,
            // UserMessage) gardent leur nom historique pour compat ; la
            // sérialisation les renomme en clés snake_case attendues par
            // l'endpoint (source_text, proposed_latex, user_comment).
            var sb = new StringBuilder();
            sb.Append('{');
            WriteField(sb, "version", r.Version, first: true);
            WriteField(sb, "ts", r.Timestamp.ToString("o"));
            WriteField(sb, "user_id", r.UserId);
            WriteField(sb, "session_id", r.SessionId);
            WriteField(sb, "source_text", r.NerText);
            WriteField(sb, "proposed_latex", r.RecognizedFormula);
            WriteField(sb, "committed_latex", r.CommittedLatex);
            WriteField(sb, "paragraph_context", r.ParagraphContext);
            WriteField(sb, "user_comment", r.UserMessage);
            WriteField(sb, "user_email", r.UserEmail);
            // log_tail / screenshot_b64 omis si vides pour économiser le
            // payload (utile surtout côté screenshot ~500 KB).
            if (!string.IsNullOrEmpty(r.LogTail))
                WriteField(sb, "log_tail", r.LogTail);
            if (!string.IsNullOrEmpty(r.ScreenshotPngBase64))
                WriteField(sb, "screenshot_b64", r.ScreenshotPngBase64);
            // Métadonnées env. Imbriquées dans un objet "metadata" pour
            // matcher la convention de l'endpoint.
            sb.Append(",\"metadata\":{");
            WriteField(sb, "word_version", r.WordVersion, first: true);
            WriteField(sb, "os_version", r.OsVersion);
            WriteField(sb, "dotnet_version", r.DotNetVersion);
            sb.Append('}');
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
