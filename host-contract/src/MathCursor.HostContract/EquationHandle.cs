using System;

namespace MathCursor.HostContract;

/// <summary>
/// Handle opaque identifiant une équation insérée (id stable dans le temps).
/// Seul rescapé de l'ancien « contrat » host-contract : les 4 interfaces
/// d'inversion (core-pilote-hôte) ont été supprimées comme code mort
/// (ADR 2026-06-23-Refactor-delete-dead-host-contract). Type partagé, utilisé
/// par l'adapter (EditModeController / SourceMap).
/// </summary>
public sealed class EquationHandle : IEquatable<EquationHandle>
{
    public string Id { get; }
    public EquationHandle(string id) { Id = id; }
    public bool Equals(EquationHandle? other) => other is not null && other.Id == Id;
    public override bool Equals(object? obj) => Equals(obj as EquationHandle);
    public override int GetHashCode() => Id.GetHashCode();
    public override string ToString() => $"EquationHandle({Id})";
}
