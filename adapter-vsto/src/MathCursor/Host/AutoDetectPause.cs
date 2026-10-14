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
using System.Collections.Generic;

namespace MathCursor.Host
{
    /// <summary>
    /// Documents dont la détection auto est en pause (ADR 2026-10-14-UX-
    /// escape-pauses-autodetect) : Échap sur la popup met le document actif en
    /// pause, Ctrl+Espace la lève. Mémoire de session uniquement — n'écrit
    /// jamais <c>AppSettings.AutoDetect</c>. Clé = <c>Document.FullName</c>.
    /// </summary>
    internal sealed class AutoDetectPause
    {
        private readonly HashSet<string> _paused =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool IsPaused(string docKey) =>
            !string.IsNullOrEmpty(docKey) && _paused.Contains(docKey);

        /// <summary>True si le document vient de passer en pause.</summary>
        public bool Pause(string docKey) =>
            !string.IsNullOrEmpty(docKey) && _paused.Add(docKey);

        /// <summary>True si une pause a effectivement été levée.</summary>
        public bool Resume(string docKey) =>
            !string.IsNullOrEmpty(docKey) && _paused.Remove(docKey);
    }
}
