using System;
using System.Collections.Generic;
using System.IO;
using MathCursor.HostContract;

namespace MathCursor.Host
{
    /// <summary>
    /// Implémentation VSTO de IUserFeedback : logging simple dans
    /// %AppData%\MathCursor\logs\mathcursor.log (un seul fichier, append).
    /// </summary>
    public sealed class VstoUserFeedback : IUserFeedback
    {
        private readonly string _logPath;

        public VstoUserFeedback()
        {
            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MathCursor", "logs");
            Directory.CreateDirectory(baseDir);
            _logPath = Path.Combine(baseDir, "mathcursor.log");
        }

        public void LogSuggestionShown(IReadOnlyList<RankedCandidate> candidates)
        {
            Append($"suggestion_shown n={candidates?.Count ?? 0}");
        }

        public void LogSuggestionSelected(int index)
        {
            Append($"suggestion_selected index={index}");
        }

        public void LogSuggestionRejected(string reason)
        {
            Append($"suggestion_rejected reason={reason ?? ""}");
        }

        public void LogParsingError(string input, string error)
        {
            Append($"parsing_error error={error} input={Truncate(input, 80)}");
        }

        public void LogPlatformCapability(string name, bool supported)
        {
            Append($"capability name={name} supported={supported}");
        }

        private void Append(string line)
        {
            try
            {
                File.AppendAllText(_logPath,
                    $"{DateTime.UtcNow:o} {line}{Environment.NewLine}");
            }
            catch
            {
                // Logging ne doit jamais remonter d'exception vers le core
            }
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max) + "…");
    }
}
