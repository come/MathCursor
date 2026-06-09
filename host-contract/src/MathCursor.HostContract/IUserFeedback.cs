using System.Collections.Generic;

namespace MathCursor.HostContract;

/// <summary>
/// Logging et télémétrie locale (opt-in, pas de réseau par défaut).
/// VSTO → fichier JSON dans %AppData%\MathCursor\logs\.
/// </summary>
public interface IUserFeedback
{
    void LogSuggestionShown(IReadOnlyList<RankedCandidate> candidates);

    void LogSuggestionSelected(int index);

    void LogSuggestionRejected(string? reason);

    void LogParsingError(string input, string error);

    void LogPlatformCapability(string name, bool supported);
}
