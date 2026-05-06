using System;

namespace MathCursor.HostContract;

/// <summary>Texte lu autour du curseur pour alimenter le pipeline.</summary>
public sealed class ContextText
{
    public string TextBefore { get; init; } = "";
    public string TextAfter { get; init; } = "";
    public int CaretOffset { get; init; }
    public string? LanguageHint { get; init; }
}

/// <summary>Zone de texte dans le contexte, repérée par offsets.</summary>
public sealed class TextZone
{
    public int StartOffset { get; init; }
    public int EndOffset { get; init; }
    public string Text { get; init; } = "";
}

/// <summary>Sortie complète d'une conversion, formats multiples pour les hôtes variés.</summary>
public sealed class EquationOutput
{
    public string Source { get; init; } = "";
    public string Latex { get; init; } = "";
    public string? Omml { get; init; }
    public string? UnicodeFallback { get; init; }
    public EquationMetadata Metadata { get; init; } = new();
}

public sealed class EquationMetadata
{
    public string? SourceLanguage { get; init; }
    public int CandidatesConsidered { get; init; }
    public int SelectedCandidateIndex { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string CoreVersion { get; init; } = "";

    /// <summary>
    /// Sidecar de résolutions de désambiguïsation (JSON), persisté avec
    /// la source pour survivre reload Word, edit OMath, copy-paste, etc.
    /// Vide si l'équation n'a pas de pin/vote (tous les Rules en default).
    /// Cf. ADR 2026-05-06 resolution-sidecar-and-layers (Phase 3).
    /// Format : voir <c>MathCursor.Core.Resolution.SidecarSerializer</c>.
    /// </summary>
    public string? SidecarJson { get; init; }
}

/// <summary>Handle opaque identifiant une équation insérée (stable dans le temps).</summary>
public sealed class EquationHandle : IEquatable<EquationHandle>
{
    public string Id { get; }
    public EquationHandle(string id) { Id = id; }
    public bool Equals(EquationHandle? other) => other is not null && other.Id == Id;
    public override bool Equals(object? obj) => Equals(obj as EquationHandle);
    public override int GetHashCode() => Id.GetHashCode();
    public override string ToString() => $"EquationHandle({Id})";
}

public sealed class StoredEquation
{
    public string Source { get; init; } = "";
    public EquationMetadata Metadata { get; init; } = new();
}

public sealed class RankedCandidate
{
    public string Display { get; init; } = "";
    public string Latex { get; init; } = "";
    public double Score { get; init; }
    public object? Ast { get; init; }
}

public enum NotificationLevel { Info, Warning, Error }

public sealed class CaretPosition
{
    public int Offset { get; init; }
    public string? ParagraphId { get; init; }
}

public delegate void CaretMovedListener(CaretPosition position);
public delegate void EquationEnteredListener(EquationHandle handle);
public delegate void EquationExitedListener(EquationHandle handle);
public delegate void ConversionRequestedListener();
public delegate void SuggestionSelectedListener(int index);
public delegate void EditCommittedListener(string newSource);

/// <summary>Retourné par Add* pour permettre de se désabonner.</summary>
public delegate void Unsubscribe();
