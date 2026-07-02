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
